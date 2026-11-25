using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.NearRelative;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 近亲属备案接口（低收入人口救助帮扶 → 近亲属备案）。
/// <para>
/// 权限统一为 <see cref="PermissionCodes.NEAR_RELATIVE_MANAGE"/>；业务规则（版本校验、
/// 对象归属校验、档案带出、软删）全部复用 Core <see cref="INearRelativeService"/>，
/// 端点只做契约映射与分页筛选，不重抄任何规则。
/// </para>
/// </summary>
public static class NearRelativeEndpoints
{
    public static IEndpointRouteBuilder MapNearRelativeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/social-assistance/near-relatives", ListAsync);
        app.MapGet("/api/social-assistance/near-relatives/staff-lookup", LookupStaffAsync);
        app.MapGet("/api/social-assistance/near-relatives/archive-fill", FillArchiveAsync);
        app.MapGet("/api/social-assistance/near-relatives/{id:long}", GetByIdAsync);
        app.MapPost("/api/social-assistance/near-relatives", CreateAsync);
        app.MapPut("/api/social-assistance/near-relatives/{id:long}", UpdateAsync);
        app.MapDelete("/api/social-assistance/near-relatives/{id:long}", DeleteAsync);
        return app;
    }

    // ───────────────────────────── 查询 ─────────────────────────────

    private static async Task<IResult> ListAsync(
        HttpContext context,
        INearRelativeService nearRelatives,
        INewPermissionService permissions,
        CancellationToken ct,
        string? town,
        string? keyword,
        int? page,
        int? pageSize)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.NEAR_RELATIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        var pageIndex = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? 20, 1, 200);
        var townFilter = string.IsNullOrWhiteSpace(town) || town == "全部" ? null : town.Trim();

        var entriesResult = await nearRelatives.GetAllForPrintAsync(null, ct);
        if (entriesResult.IsFailure)
        {
            return MapResult(context, entriesResult);
        }

        var allEntries = entriesResult.Value;
        var towns = BuildTowns(allEntries);
        var keywordFilter = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
        var entries = allEntries
            .Where(entry => townFilter is null
                || string.Equals(entry.Staff.Town?.Trim(), townFilter, StringComparison.Ordinal))
            .Where(entry => keywordFilter is null
                || Contains(entry.Staff.StaffName, keywordFilter)
                || Contains(entry.Staff.WorkUnit, keywordFilter))
            .OrderByDescending(entry => entry.Staff.CreatedAt ?? entry.Staff.UpdatedAt ?? DateTime.MinValue)
            .ThenByDescending(entry => entry.Staff.Id)
            .ToList();

        var totalCount = entries.Count;
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)size);

        var items = entries
            .Skip((pageIndex - 1) * size)
            .Take(size)
            .Select(entry => new NearRelativeListItemDto(
                entry.Staff.Id,
                entry.Staff.StaffName,
                DataMasker.MaskIdCard(entry.Staff.StaffIdCard),
                entry.Staff.WorkUnit,
                entry.Staff.Position,
                entry.Staff.Town,
                entry.Links.Count,
                entry.Staff.CreatedAt,
                entry.Staff.UpdatedAt))
            .ToList();

        return Results.Json(new NearRelativeListResponse(
            items, pageIndex, size, totalCount, allEntries.Count, totalPages, towns));
    }

    private static async Task<IResult> GetByIdAsync(
        long id,
        HttpContext context,
        INearRelativeService nearRelatives,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.NEAR_RELATIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (id <= 0)
        {
            return InvalidRequest(context, "备案 ID 无效");
        }

        var result = await nearRelatives.GetByIdAsync(id, ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(ToDto(result.Value));
    }

    /// <summary>
    /// 工作人员身份证查询：先查既有有效备案（命中即返回，用于"载入编辑"提示），
    /// 未命中再按系统用户 + 档案照料护理人合并工作信息。
    /// </summary>
    private static async Task<IResult> LookupStaffAsync(
        HttpContext context,
        INearRelativeService nearRelatives,
        INewPermissionService permissions,
        CancellationToken ct,
        string? idCard)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.NEAR_RELATIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(idCard))
        {
            return InvalidRequest(context, "请填写工作人员身份证号");
        }

        var card = idCard.Trim();
        var existingResult = await nearRelatives.GetByStaffIdCardAsync(card, ct);
        if (existingResult.IsFailure)
        {
            return MapResult(context, existingResult);
        }

        if (existingResult.Value is { } existing)
        {
            return Results.Json(new NearRelativeStaffLookupResponse(
                ToDto(existing.Staff),
                false,
                false,
                false,
                false,
                new NearRelativeExistingFilingDto(
                    existing.Staff.Id, existing.Staff.StaffName, existing.Links.Count)));
        }

        var lookupResult = await nearRelatives.GetStaffWorkInfoByIdCardAsync(card, ct);
        if (lookupResult.IsFailure)
        {
            return MapResult(context, lookupResult);
        }

        var lookup = lookupResult.Value;
        return Results.Json(new NearRelativeStaffLookupResponse(
            lookup is null ? null : ToDto(lookup.Staff),
            lookup?.FoundInUsers ?? false,
            lookup?.FoundInCaregivers ?? false,
            lookup?.FoundInDestituteArchive ?? false,
            lookup?.HasAny ?? false,
            null));
    }

    /// <summary>档案带出：applicationId 优先，其次救助对象身份证；未命中返回 null。</summary>
    private static async Task<IResult> FillArchiveAsync(
        HttpContext context,
        INearRelativeService nearRelatives,
        INewPermissionService permissions,
        CancellationToken ct,
        string? idCard,
        long? applicationId,
        long? excludeStaffId)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.NEAR_RELATIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        var excludeId = excludeStaffId is > 0 ? excludeStaffId : null;
        Result<NearRelativeArchiveFill?> result;

        if (applicationId is > 0)
        {
            result = await nearRelatives.BuildLinkByApplicationAsync(applicationId.Value, excludeId, ct);
        }
        else if (!string.IsNullOrWhiteSpace(idCard))
        {
            result = await nearRelatives.BuildLinkByIdCardAsync(idCard.Trim(), excludeId, ct);
        }
        else
        {
            return InvalidRequest(context, "请提供救助对象身份证号或申请 ID");
        }

        if (result.IsFailure)
        {
            return MapResult(context, result);
        }

        if (result.Value is not { } fill)
        {
            return Results.Json((NearRelativeArchiveFillResponse?)null);
        }

        return Results.Json(new NearRelativeArchiveFillResponse(
            fill.ApplicationId,
            fill.ApplicationNo,
            fill.Status,
            fill.StatusDisplay,
            fill.ApplicationCreatedAt,
            ToDto(fill.Link),
            fill.OccupiedByOtherStaff,
            fill.OccupiedByStaffName));
    }

    // ───────────────────────────── 写操作 ─────────────────────────────

    private static async Task<IResult> CreateAsync(
        NearRelativeSaveRequest? request,
        HttpContext context,
        INearRelativeService nearRelatives,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.NEAR_RELATIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request?.Staff is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var entry = ToEntity(request, 0, OperatorName(context));
        var result = await nearRelatives.SaveAsync(entry, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new NearRelativeSaveResponse(result.Value));
    }

    private static async Task<IResult> UpdateAsync(
        long id,
        NearRelativeSaveRequest? request,
        HttpContext context,
        INearRelativeService nearRelatives,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.NEAR_RELATIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (id <= 0)
        {
            return InvalidRequest(context, "备案 ID 无效");
        }

        if (request?.Staff is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var entry = ToEntity(request, id, OperatorName(context));
        var result = await nearRelatives.SaveAsync(entry, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new NearRelativeSaveResponse(result.Value));
    }

    private static async Task<IResult> DeleteAsync(
        long id,
        HttpContext context,
        INearRelativeService nearRelatives,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.NEAR_RELATIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (id <= 0)
        {
            return InvalidRequest(context, "备案 ID 无效");
        }

        var result = await nearRelatives.DeleteAsync(id, ct);
        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    // ───────────────────────────── 契约映射 ─────────────────────────────

    private static IReadOnlyList<string> BuildTowns(IReadOnlyList<NearRelativeEntry> entries)
        => entries
            .Select(entry => entry.Staff.Town)
            .Where(town => !string.IsNullOrWhiteSpace(town))
            .Select(town => town.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(town => town, StringComparer.Ordinal)
            .ToList();

    private static NearRelativeEntry ToEntity(
        NearRelativeSaveRequest request,
        long id,
        string operatorName)
    {
        var staff = request.Staff!;
        var links = (request.Links ?? Array.Empty<NearRelativeLinkDto>())
            .Where(link => !string.IsNullOrWhiteSpace(link.Name))
            .Select(link => new NearRelativeLink
            {
                Id = link.Id,
                StaffId = id,
                ApplicationId = link.ApplicationId is > 0 ? link.ApplicationId : null,
                Relation = Text(link.Relation),
                Name = Text(link.Name),
                IdCard = Text(link.IdCard),
                Gender = Text(link.Gender),
                BirthDate = Text(link.BirthDate),
                FamilyAddress = Text(link.FamilyAddress),
                HukouAddress = Text(link.HukouAddress),
                ResidenceAddress = Text(link.ResidenceAddress),
                FamilySize = link.FamilySize is > 0 ? link.FamilySize : null,
                HelpType = Text(link.HelpType),
                MonthAmount = link.MonthAmount,
                ReportAmount = link.ReportAmount,
                StartTime = Text(link.StartTime),
                FamilyRelation = Text(link.FamilyRelation),
                ApplyReason = Text(link.ApplyReason),
                ApplyTime = Text(link.ApplyTime),
                FamilyDifficulty = Text(link.FamilyDifficulty),
                EconomyInvestigate = Text(link.EconomyInvestigate),
                TownOpinion = Text(link.TownOpinion),
                DynamicRecord = Text(link.DynamicRecord),
                SortOrder = link.SortOrder,
            })
            .ToList();

        return new NearRelativeEntry
        {
            Staff = new NearRelativeStaff
            {
                Id = id,
                StaffName = Text(staff.StaffName),
                StaffIdCard = Text(staff.StaffIdCard),
                StaffPhone = Text(staff.StaffPhone),
                WorkUnit = Text(staff.WorkUnit),
                Position = Text(staff.Position),
                Town = Text(staff.Town),
                UpdatedAt = staff.UpdatedAt,
                CreatedBy = operatorName,
                UpdatedBy = operatorName,
            },
            Links = links,
        };
    }

    private static NearRelativeEntryDto ToDto(NearRelativeEntry entry)
        => new(ToDto(entry.Staff), entry.Links.Select(ToDto).ToList());

    private static NearRelativeStaffDto ToDto(NearRelativeStaff staff)
        => new(
            staff.Id,
            staff.StaffName,
            staff.StaffIdCard,
            staff.StaffPhone,
            staff.WorkUnit,
            staff.Position,
            staff.Town,
            staff.CreatedAt,
            staff.UpdatedAt);

    private static NearRelativeLinkDto ToDto(NearRelativeLink link)
        => new(
            link.Id,
            link.StaffId,
            link.ApplicationId,
            link.Relation,
            link.Name,
            link.IdCard,
            link.Gender,
            link.BirthDate,
            link.FamilyAddress,
            link.HukouAddress,
            link.ResidenceAddress,
            link.FamilySize,
            link.HelpType,
            link.MonthAmount,
            link.ReportAmount,
            link.StartTime,
            link.FamilyRelation,
            link.ApplyReason,
            link.ApplyTime,
            link.FamilyDifficulty,
            link.EconomyInvestigate,
            link.TownOpinion,
            link.DynamicRecord,
            link.SortOrder);

    // ───────────────────────────── 通用 ─────────────────────────────

    private static bool Contains(string? source, string keyword)
        => !string.IsNullOrEmpty(source)
           && source.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    private static string Text(string? value) => value?.Trim() ?? string.Empty;

    private static string OperatorName(HttpContext context)
    {
        var user = context.GetCurrentUser();
        if (user is null)
        {
            return "System";
        }

        return string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;
    }

    /// <summary>
    /// 业务错误统一出口：版本冲突 / 身份证重复 → 409；校验 → 400；不存在 → 404。
    /// 前端据 409 展示"版本冲突，请刷新后重试"。
    /// </summary>
    private static IResult MapResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.RECORD_NOT_FOUND
            or ErrorCodes.NOT_FOUND
            or ErrorCodes.APPLICATION_NOT_FOUND)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound, result.Message ?? "备案不存在或已删除");
        }

        if (result.ErrorCode is ErrorCodes.CONCURRENCY_CONFLICT
            or ErrorCodes.DUPLICATE_ID_CARD)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status409Conflict,
                "CONFLICT", result.Message ?? "备案已被他人更新，请刷新后再保存");
        }

        if (result.ErrorCode is ErrorCodes.VALIDATION_FAILED
            or ErrorCodes.REQUIRED_FIELD_MISSING
            or ErrorCodes.INVALID_ID_CARD)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, result.Message ?? "请求参数校验失败");
        }

        return ApiErrors.FromResult(context, result);
    }

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context, StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError, message);
}
