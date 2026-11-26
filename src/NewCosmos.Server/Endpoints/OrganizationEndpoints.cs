using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 用户权限 · 经办机构管理接口（对齐 PC <c>OrganizationPage</c> / <c>OrganizationImportPage</c>）。
/// <para>
/// 读接口要求 <c>ORG_VIEW</c>，写接口分别要求 <c>ORG_CREATE</c> / <c>ORG_EDIT</c> / <c>ORG_DELETE</c>。
/// 数据范围来自 Core <see cref="IDataScopeService"/>：读接口只返回当前用户可管理组织子树
/// （父节点在范围外时该节点按顶级返回，详情范围外 404）；新增/编辑/删除用
/// <see cref="IDataScopeService.CanManageOrganizationAsync"/> 校验，越权 403。
/// 新增根机构与 legacy 一致，不做范围拦截。
/// </para>
/// <para>
/// 业务校验/导入解析全部在 Core <see cref="IOrganizationService"/>；本层只做权限、数据范围、上传落盘与错误映射。
/// </para>
/// </summary>
public static class OrganizationEndpoints
{
    private const string Prefix = "/api/system/organizations";

    /// <summary>组织导入单文件上限 20MB（与任务约定一致）。</summary>
    private const long MaxImportFileBytes = 20L * 1024 * 1024;

    /// <summary>导入结果最多返回的错误明细条数（ErrorCount 仍为全量）。</summary>
    private const int MaxImportErrorDetails = 200;

    /// <summary>
    /// Core <see cref="DictionaryTypeCodes"/> 目前只有 InstitutionCategory / InstitutionType，
    /// 没有 InstitutionNature 常量；此处按字典分类键字面量读取，缺失时返回空数组。
    /// </summary>
    private const string InstitutionNatureCategory = "InstitutionNature";

    private static readonly string[] AllowedImportExtensions = [".xlsx", ".xls", ".csv"];

    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{Prefix}/tree", GetTreeAsync);
        app.MapGet($"{Prefix}/options", GetOptionsAsync);
        app.MapGet($"{Prefix}/{{id:int}}", GetDetailAsync);

        app.MapPost(Prefix, CreateAsync);
        app.MapPut($"{Prefix}/{{id:int}}", UpdateAsync);
        app.MapDelete($"{Prefix}/{{id:int}}", DeleteAsync);

        app.MapPost($"{Prefix}/preview-import", PreviewImportAsync).DisableAntiforgery();
        app.MapPost($"{Prefix}/import", ImportAsync).DisableAntiforgery();

        return app;
    }

    // ───────────────────────────── 查询 ─────────────────────────────

    /// <summary>GET /api/system/organizations/tree — 数据范围内的组织树与统计。</summary>
    private static async Task<IResult> GetTreeAsync(
        HttpContext context,
        INewPermissionService permissions,
        IOrganizationService organizations,
        IDataScopeService dataScope,
        IDatabaseService database,
        CancellationToken ct)
    {
        if (await EndpointSecurity.RequirePermissionAsync(
                context, permissions, PermissionCodes.ORG_VIEW, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var allResult = await organizations.GetAllAsync(ct);
        if (allResult.IsFailure)
        {
            return ApiErrors.FromResult(context, allResult);
        }

        var all = allResult.Value ?? new List<Organization>();
        var scope = await ResolveScopeAsync(userId, dataScope, ct);
        var filtered = scope.IsUnrestricted
            ? all
            : all.Where(org => scope.OrganizationIds.Contains(org.Id)).ToList();

        var scopedIds = filtered.Select(org => org.Id).ToHashSet();
        var parentIds = filtered
            .Where(org => org.ParentId.HasValue && scopedIds.Contains(org.ParentId.Value))
            .Select(org => org.ParentId!.Value)
            .ToHashSet();
        var rootCount = filtered.Count(
            org => org.ParentId is null || !scopedIds.Contains(org.ParentId.Value));

        var totalUsers = 0L;
        if (scopedIds.Count > 0)
        {
            var usersResult = await database.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM nc_sys_users WHERE organization_id = ANY($1)",
                ct,
                scopedIds.ToArray());
            if (usersResult.IsFailure)
            {
                return ApiErrors.FromResult(context, usersResult);
            }

            totalUsers = usersResult.Value;
        }

        var nodes = MapNodes(organizations.BuildTree(filtered));
        var stats = new OrganizationStatsDto(
            filtered.Count,
            (int)totalUsers,
            parentIds.Count,
            rootCount,
            filtered.Count(org => org.IsSpecialCareInstitution));

        return Results.Json(new OrganizationTreeResponse(nodes, stats));
    }

    /// <summary>GET /api/system/organizations/options — 可管理组织 + 机构编辑字典。</summary>
    private static async Task<IResult> GetOptionsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IOrganizationService organizations,
        IDictionaryService dictionaries,
        IDataScopeService dataScope,
        CancellationToken ct)
    {
        if (await EndpointSecurity.RequirePermissionAsync(
                context, permissions, PermissionCodes.ORG_VIEW, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var allResult = await organizations.GetAllAsync(ct);
        if (allResult.IsFailure)
        {
            return ApiErrors.FromResult(context, allResult);
        }

        var all = allResult.Value ?? new List<Organization>();
        var scope = await ResolveScopeAsync(userId, dataScope, ct);
        var scoped = scope.IsUnrestricted
            ? all
            : all.Where(org => scope.OrganizationIds.Contains(org.Id)).ToList();

        var options = scoped
            .OrderBy(org => org.Name)
            .Select(org => new OrganizationOptionDto(org.Id, org.Name, org.ParentId))
            .ToList();

        var naturesResult = await dictionaries.GetItemsByCategoryAsync(InstitutionNatureCategory, ct);
        if (naturesResult.IsFailure)
        {
            return ApiErrors.FromResult(context, naturesResult);
        }

        var typesResult = await dictionaries.GetItemsByCategoryAsync(
            DictionaryTypeCodes.InstitutionType, ct);
        if (typesResult.IsFailure)
        {
            return ApiErrors.FromResult(context, typesResult);
        }

        var categoriesResult = await dictionaries.GetItemsByCategoryAsync(
            DictionaryTypeCodes.InstitutionCategory, ct);
        if (categoriesResult.IsFailure)
        {
            return ApiErrors.FromResult(context, categoriesResult);
        }

        return Results.Json(new OrganizationOptionsResponse(
            options,
            MapDictionaryKeys(naturesResult.Value),
            MapDictionaryKeys(typesResult.Value),
            MapDictionaryKeys(categoriesResult.Value)));
    }

    /// <summary>GET /api/system/organizations/{id} — 组织详情（范围外 404，含 userCount）。</summary>
    private static async Task<IResult> GetDetailAsync(
        int id,
        HttpContext context,
        INewPermissionService permissions,
        IOrganizationService organizations,
        IDataScopeService dataScope,
        CancellationToken ct)
    {
        if (await EndpointSecurity.RequirePermissionAsync(
                context, permissions, PermissionCodes.ORG_VIEW, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (id <= 0)
        {
            return Invalid(context, "组织 ID 无效");
        }

        var result = await organizations.GetByIdAsync(id, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        if (!result.HasValue)
        {
            return NotFound(context, "组织不存在");
        }

        if (!await IsInScopeAsync(userId, id, dataScope, ct))
        {
            return NotFound(context, "组织不存在");
        }

        var userCountResult = await organizations.GetUserCountAsync(id, ct);
        if (userCountResult.IsFailure)
        {
            return ApiErrors.FromResult(context, userCountResult);
        }

        return Results.Json(MapDetail(result.Value, userCountResult.Value));
    }

    // ───────────────────────────── 写入 ─────────────────────────────

    /// <summary>POST /api/system/organizations — 新增组织（上级必须在可管理范围内）。</summary>
    private static async Task<IResult> CreateAsync(
        OrganizationSaveInput? request,
        HttpContext context,
        INewPermissionService permissions,
        IOrganizationService organizations,
        IDataScopeService dataScope,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await EndpointSecurity.RequirePermissionAsync(
                context, permissions, PermissionCodes.ORG_CREATE, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var name = Normalize(request.Name);
        if (name.Length == 0)
        {
            return Invalid(context, "组织名称不能为空");
        }

        var parentId = NormalizeId(request.ParentId);

        // 与 legacy 一致：无上级（新建根机构）不做数据范围拦截；
        // 指定上级时，上级必须存在且在当前用户可管理范围内，否则 403。
        if (parentId is { } pid)
        {
            var parentResult = await organizations.GetByIdAsync(pid, ct);
            if (parentResult.IsFailure)
            {
                return ApiErrors.FromResult(context, parentResult);
            }

            if (!parentResult.HasValue)
            {
                return NotFound(context, "上级组织不存在");
            }

            if (!await dataScope.CanManageOrganizationAsync(userId, pid, ct))
            {
                return Forbidden(context, "无权在该组织下创建下级机构");
            }
        }

        var coreRequest = new OrganizationCreateRequest
        {
            Name = name,
            Abbreviation = Normalize(request.Abbreviation),
            Principal = Normalize(request.Principal),
            PrincipalIdentityCard = Normalize(request.PrincipalIdentityCard),
            Phone = Normalize(request.Phone),
            Address = Normalize(request.Address),
            PostalCode = Normalize(request.PostalCode),
            ParentId = parentId,
            InstitutionNature = Normalize(request.InstitutionNature),
            InstitutionType = Normalize(request.InstitutionType),
            InstitutionCategory = Normalize(request.InstitutionCategory),
            IsSpecialCareInstitution = request.IsSpecialCareInstitution,
            ProvinceId = NormalizeId(request.ProvinceId),
            CityId = NormalizeId(request.CityId),
            CountyId = NormalizeId(request.CountyId),
            TownId = NormalizeId(request.TownId),
            VillageId = NormalizeId(request.VillageId),
            CreatedBy = userId.ToString()
        };

        var createResult = await organizations.CreateAsync(coreRequest, ct);
        if (createResult.IsFailure)
        {
            return WriteFailure(context, createResult.ErrorCode, createResult.Message);
        }

        var newId = createResult.Value;
        await dataScope.InvalidateAllCacheAsync();
        logger.LogSecurity("创建组织",
            ("Operator", userId),
            ("OrganizationId", newId),
            ("Name", name));

        return Results.Json(
            new OrganizationCreatedResponse(newId, "组织创建成功"),
            statusCode: StatusCodes.Status201Created);
    }

    /// <summary>PUT /api/system/organizations/{id} — 编辑组织（无管理权限 403）。</summary>
    private static async Task<IResult> UpdateAsync(
        int id,
        OrganizationSaveInput? request,
        HttpContext context,
        INewPermissionService permissions,
        IOrganizationService organizations,
        IDataScopeService dataScope,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await EndpointSecurity.RequirePermissionAsync(
                context, permissions, PermissionCodes.ORG_EDIT, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (id <= 0)
        {
            return Invalid(context, "组织 ID 无效");
        }

        if (request is null)
        {
            return Invalid(context, "请求体不能为空");
        }

        var name = Normalize(request.Name);
        if (name.Length == 0)
        {
            return Invalid(context, "组织名称不能为空");
        }

        var existingResult = await organizations.GetByIdAsync(id, ct);
        if (existingResult.IsFailure)
        {
            return ApiErrors.FromResult(context, existingResult);
        }

        if (!existingResult.HasValue)
        {
            return NotFound(context, "组织不存在");
        }

        var existing = existingResult.Value;

        if (!await dataScope.CanManageOrganizationAsync(userId, id, ct))
        {
            return Forbidden(context, "没有权限修改该组织");
        }

        var parentId = NormalizeId(request.ParentId);
        if (parentId != NormalizeId(existing.ParentId) && parentId is { } newParentId)
        {
            var parentResult = await organizations.GetByIdAsync(newParentId, ct);
            if (parentResult.IsFailure)
            {
                return ApiErrors.FromResult(context, parentResult);
            }

            if (!parentResult.HasValue)
            {
                return NotFound(context, "上级组织不存在");
            }

            var allResult = await organizations.GetAllAsync(ct);
            if (allResult.IsFailure)
            {
                return ApiErrors.FromResult(context, allResult);
            }

            if (CreatesCycle(allResult.Value, id, newParentId))
            {
                return Invalid(context, "不能将组织设置为自身或自身下级机构");
            }
        }

        // 使用 OrganizationUpdateRequest 而不是 (id, SaveRequest) 重载：
        // 后者在 Core 里未把 ProvinceId 传给更新语句，会把已有 province_id 清空。
        var updateRequest = new OrganizationUpdateRequest
        {
            OrganizationId = id,
            Name = name,
            Abbreviation = Normalize(request.Abbreviation),
            Principal = Normalize(request.Principal),
            PrincipalIdentityCard = Normalize(request.PrincipalIdentityCard),
            Phone = Normalize(request.Phone),
            Address = Normalize(request.Address),
            PostalCode = Normalize(request.PostalCode),
            ParentId = parentId,
            InstitutionNature = Normalize(request.InstitutionNature),
            InstitutionType = Normalize(request.InstitutionType),
            InstitutionCategory = Normalize(request.InstitutionCategory),
            IsSpecialCareInstitution = request.IsSpecialCareInstitution,
            ProvinceId = NormalizeId(request.ProvinceId),
            CityId = NormalizeId(request.CityId),
            CountyId = NormalizeId(request.CountyId),
            TownId = NormalizeId(request.TownId),
            VillageId = NormalizeId(request.VillageId),
            UpdatedBy = userId.ToString()
        };

        var updateResult = await organizations.UpdateAsync(updateRequest, ct);
        if (updateResult.IsFailure)
        {
            return WriteFailure(context, updateResult.ErrorCode, updateResult.Message);
        }

        await dataScope.InvalidateAllCacheAsync();
        logger.LogSecurity("修改组织",
            ("Operator", userId),
            ("OrganizationId", id),
            ("Name", name));

        return Results.Json(new OrganizationActionResult("组织更新成功"));
    }

    /// <summary>DELETE /api/system/organizations/{id} — 删除组织（Core 校验下级/用户）。</summary>
    private static async Task<IResult> DeleteAsync(
        int id,
        HttpContext context,
        INewPermissionService permissions,
        IOrganizationService organizations,
        IDataScopeService dataScope,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await EndpointSecurity.RequirePermissionAsync(
                context, permissions, PermissionCodes.ORG_DELETE, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (id <= 0)
        {
            return Invalid(context, "组织 ID 无效");
        }

        var existingResult = await organizations.GetByIdAsync(id, ct);
        if (existingResult.IsFailure)
        {
            return ApiErrors.FromResult(context, existingResult);
        }

        if (!existingResult.HasValue)
        {
            return NotFound(context, "组织不存在");
        }

        if (!await dataScope.CanManageOrganizationAsync(userId, id, ct))
        {
            return Forbidden(context, "没有权限删除该组织");
        }

        var deleteResult = await organizations.DeleteAsync(id, ct);
        if (deleteResult.IsFailure)
        {
            return WriteFailure(context, deleteResult.ErrorCode, deleteResult.Message);
        }

        await dataScope.InvalidateAllCacheAsync();
        logger.LogSecurity("删除组织",
            ("Operator", userId),
            ("OrganizationId", id),
            ("Name", existingResult.Value.Name));

        return Results.Json(new OrganizationActionResult("组织删除成功"));
    }

    // ───────────────────────────── 导入 ─────────────────────────────

    /// <summary>POST /api/system/organizations/preview-import — 导入预览（前 5 行）。</summary>
    private static async Task<IResult> PreviewImportAsync(
        IFormFile? file,
        HttpContext context,
        INewPermissionService permissions,
        IOrganizationService organizations,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await EndpointSecurity.RequirePermissionAsync(
                context, permissions, PermissionCodes.ORG_CREATE, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is null)
        {
            return Unauthorized(context);
        }

        if (ValidateUpload(file, context) is { } invalid)
        {
            return invalid;
        }

        string? tempPath = null;
        try
        {
            tempPath = await SaveUploadToTempAsync(file!, ct);
            var preview = organizations.PreviewImport(tempPath, 5);

            var rows = (preview.PreviewRows ?? new List<OrganizationPreviewRow>())
                .Select(row => new OrganizationPreviewRowDto(
                    row.RowNumber,
                    Normalize(row.Name),
                    Normalize(row.Abbreviation),
                    Normalize(row.Principal),
                    Normalize(row.Phone),
                    Normalize(row.Address),
                    Normalize(row.Region)))
                .ToList();

            return Results.Json(new OrganizationPreviewResponse(preview.TotalRows, rows, preview.Error));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Error($"保存组织导入预览文件失败: {ex.Message}");
            return ApiErrors.Write(
                context,
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError,
                "保存上传文件失败，请重试");
        }
        finally
        {
            if (tempPath is not null)
            {
                DeleteTempFile(tempPath, logger);
            }
        }
    }

    /// <summary>POST /api/system/organizations/import — 从 Excel / CSV 导入组织。</summary>
    private static async Task<IResult> ImportAsync(
        IFormFile? file,
        HttpContext context,
        INewPermissionService permissions,
        IOrganizationService organizations,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await EndpointSecurity.RequirePermissionAsync(
                context, permissions, PermissionCodes.ORG_CREATE, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (ValidateUpload(file, context) is { } invalid)
        {
            return invalid;
        }

        string? tempPath = null;
        try
        {
            tempPath = await SaveUploadToTempAsync(file!, ct);
            var result = await organizations.ImportFromFileAsync(tempPath, progress: null, ct: ct);

            logger.LogSecurity("导入组织",
                ("Operator", userId),
                ("Imported", result.ImportedCount),
                ("Failed", result.ErrorCount));

            var errors = result.Errors ?? new List<string>();
            var errorDetails = errors.Count > MaxImportErrorDetails
                ? errors.Take(MaxImportErrorDetails).ToList()
                : errors;

            return Results.Json(new OrganizationImportResponse(
                result.Success,
                result.ImportedCount,
                result.ErrorCount,
                errorDetails,
                result.Warnings ?? new List<string>(),
                Normalize(result.Message)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Error($"保存组织导入文件失败: {ex.Message}");
            return ApiErrors.Write(
                context,
                StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError,
                "保存上传文件失败，请重试");
        }
        finally
        {
            if (tempPath is not null)
            {
                DeleteTempFile(tempPath, logger);
            }
        }
    }

    // ───────────────────────────── 数据范围 / 映射 ─────────────────────────────

    /// <summary>
    /// 当前用户数据范围：<see cref="DataScopeConstants.ALL"/> 为不限制（含超管），
    /// 其余取 Core 计算的可管理组织 id 集合。
    /// </summary>
    private sealed record ScopeContext(bool IsUnrestricted, HashSet<int> OrganizationIds);

    private static async Task<ScopeContext> ResolveScopeAsync(
        int userId,
        IDataScopeService dataScope,
        CancellationToken ct)
    {
        var scope = await dataScope.GetEffectiveScopeAsync(userId, ct);
        if (string.Equals(scope.Scope, DataScopeConstants.ALL, StringComparison.OrdinalIgnoreCase))
        {
            return new ScopeContext(true, new HashSet<int>());
        }

        var ids = await dataScope.GetManageableOrganizationIdsAsync(userId, ct);
        return new ScopeContext(false, ids.ToHashSet());
    }

    private static async Task<bool> IsInScopeAsync(
        int userId,
        int organizationId,
        IDataScopeService dataScope,
        CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(userId, dataScope, ct);
        return scope.IsUnrestricted || scope.OrganizationIds.Contains(organizationId);
    }

    /// <summary>把候选上级沿 parent_id 向上回溯，命中目标组织即构成环。</summary>
    private static bool CreatesCycle(
        List<Organization>? allOrganizations,
        int organizationId,
        int candidateParentId)
    {
        var parentMap = (allOrganizations ?? new List<Organization>())
            .GroupBy(org => org.Id)
            .ToDictionary(group => group.Key, group => group.First().ParentId);

        int? cursor = candidateParentId;
        var guard = 0;
        while (cursor is { } current && guard++ <= parentMap.Count)
        {
            if (current == organizationId)
            {
                return true;
            }

            cursor = parentMap.TryGetValue(current, out var parentId) ? parentId : null;
        }

        return false;
    }

    private static OrganizationDetailDto MapDetail(Organization org, int userCount) => new(
        org.Id,
        Normalize(org.Name),
        Normalize(org.Abbreviation),
        Normalize(org.Principal),
        Normalize(org.PrincipalIdentityCard),
        Normalize(org.Phone),
        Normalize(org.Address),
        Normalize(org.PostalCode),
        org.ParentId,
        Normalize(org.ParentName),
        Normalize(org.InstitutionNature),
        Normalize(org.InstitutionType),
        Normalize(org.InstitutionCategory),
        org.IsSpecialCareInstitution,
        org.ProvinceId,
        org.CityId,
        org.CountyId,
        org.TownId,
        org.VillageId,
        Normalize(org.ProvinceName),
        Normalize(org.CityName),
        Normalize(org.CountyName),
        Normalize(org.TownName),
        Normalize(org.VillageName),
        userCount,
        org.CreatedAt,
        org.UpdatedAt);

    private static List<OrganizationTreeNodeDto> MapNodes(IEnumerable<OrganizationTreeNode> nodes)
        => nodes
            .Select(node => new OrganizationTreeNodeDto(
                node.Id,
                Normalize(node.Name),
                Normalize(node.Abbreviation),
                node.Depth,
                MapNodes(node.Children ?? new List<OrganizationTreeNode>())))
            .ToList();

    /// <summary>字典项 → 启用项键值（ItemKey，按 SortOrder）；Core 查询已过滤停用项。</summary>
    private static List<string> MapDictionaryKeys(List<DictItemView>? items)
        => (items ?? new List<DictItemView>())
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select(item => Normalize(item.ItemKey))
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static int? NormalizeId(int? value) => value is > 0 ? value : null;

    // ───────────────────────────── 上传辅助 ─────────────────────────────

    private static IResult? ValidateUpload(IFormFile? file, HttpContext context)
    {
        if (file is null)
        {
            return Invalid(context, "请选择要导入的文件");
        }

        if (file.Length <= 0)
        {
            return Invalid(context, "上传文件为空，请重新选择");
        }

        if (file.Length > MaxImportFileBytes)
        {
            return Invalid(context, $"文件大小不能超过 {FormatSize(MaxImportFileBytes)}");
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension)
            || !AllowedImportExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return Invalid(context, "仅支持 .xlsx / .xls / .csv 文件");
        }

        return null;
    }

    /// <summary>
    /// 落盘到系统临时目录：<c>nc-org-{guid}{扩展名}</c>。
    /// 失败时自行清理，成功返回文件路径（由调用方 finally 删除）。
    /// </summary>
    private static async Task<string> SaveUploadToTempAsync(
        IFormFile file,
        CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var target = Path.Combine(
            Path.GetTempPath(),
            "nc-org-" + Guid.NewGuid().ToString("N") + extension);

        try
        {
            await using var stream = new FileStream(
                target,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous);
            await file.CopyToAsync(stream, ct);

            return target;
        }
        catch
        {
            TryDeleteFile(target);
            throw;
        }
    }

    private static void DeleteTempFile(string filePath, ILoggerService logger)
        => TryDeleteFile(filePath, logger);

    private static void TryDeleteFile(string filePath, ILoggerService? logger = null)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时文件清理失败不影响业务结果，但要留痕便于运维排查。
            logger?.Warn($"清理组织导入临时文件失败: {filePath}, {ex.Message}");
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024L * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }

        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }

    // ───────────────────────────── 错误出口 ─────────────────────────────

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context,
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired,
            "请先登录");

    private static IResult Invalid(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            message);

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound,
            message);

    private static IResult Forbidden(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status403Forbidden,
            ApiErrorCodes.Forbidden,
            message);

    private static IResult Conflict(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status409Conflict,
            ApiErrorCodes.ValidationError,
            message);

    /// <summary>
    /// 写操作错误映射：名称重复/存在下级/存在用户 → 409（保留 Core 中文消息）；
    /// 不存在 → 404；普通校验 → 400；其余走统一映射。
    /// </summary>
    private static IResult WriteFailure(HttpContext context, string? errorCode, string? message)
    {
        var text = string.IsNullOrWhiteSpace(message) ? "操作失败" : message.Trim();

        return errorCode switch
        {
            ErrorCodes.ORGANIZATION_NAME_DUPLICATE or ErrorCodes.DB_UNIQUE_VIOLATION
                => Conflict(context, "组织名称已存在"),

            ErrorCodes.NOT_FOUND or ErrorCodes.ORGANIZATION_LOAD_FAILED
                => NotFound(context, text),

            ErrorCodes.DB_FOREIGN_KEY_VIOLATION
                => Conflict(context, "该组织存在下级数据或已被引用，无法删除"),

            ErrorCodes.VALIDATION_FAILED or ErrorCodes.REQUIRED_FIELD_MISSING
                => text.Contains("存在子组织", StringComparison.Ordinal)
                   || text.Contains("存在用户", StringComparison.Ordinal)
                    ? Conflict(context, text)
                    : Invalid(context, text),

            _ => ApiErrors.FromResult(
                context,
                Result.Failure(errorCode ?? ErrorCodes.OPERATION_FAILED, text))
        };
    }
}


