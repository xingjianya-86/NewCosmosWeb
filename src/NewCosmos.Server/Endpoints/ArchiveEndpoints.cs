using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 救助档案管理接口：双库搜索、当前库/导入库详情、证明模板与单位模板、证明打印任务、打印历史。
/// <para>
/// 读操作 <c>ARCHIVE_VIEW</c>；单位模板写 <c>ARCHIVE_MANAGE</c>；证明打印 <c>PRINT_LOWINCOMEPROOF</c>。
/// 业务口径全部复用 Core（统计、导入库检索、申请详情、时间线、渐退期），Server 只做参数校验与装配。
/// </para>
/// </summary>
public static class ArchiveEndpoints
{
    private const string Prefix = "/api/archives";
    private const string ProofCategory = "证明文件";

    private static readonly string[] ProofModes = ["preview", "print", "save"];

    public static IEndpointRouteBuilder MapArchiveEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{Prefix}/stats", GetStatsAsync);
        app.MapGet($"{Prefix}/search", SearchAsync);
        app.MapGet($"{Prefix}/current/{{applicationId:long}}", GetCurrentDetailAsync);
        app.MapGet($"{Prefix}/imported", GetImportedDetailAsync);

        app.MapGet($"{Prefix}/proof/templates", GetProofTemplatesAsync);
        app.MapPost($"{Prefix}/proof/units", CreateProofUnitAsync).DisableAntiforgery();
        app.MapPut($"{Prefix}/proof/units/{{id:long}}", UpdateProofUnitAsync).DisableAntiforgery();
        app.MapDelete($"{Prefix}/proof/units/{{id:long}}", DeleteProofUnitAsync);

        app.MapPost($"{Prefix}/proof/jobs", CreateProofJobAsync);
        app.MapGet($"{Prefix}/proof/history", GetProofHistoryAsync);

        return app;
    }

    // ───────────────────────────── 查询 ─────────────────────────────

    /// <summary>GET /api/archives/stats → { totalArchives, monthlyArchives }</summary>
    private static async Task<IResult> GetStatsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IStatisticsService statistics,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var totalResult = await statistics.GetArchiveStatsAsync(ct);
        if (totalResult.IsFailure)
        {
            return MapResult(context, totalResult);
        }

        if (totalResult.Value is null)
        {
            return NotFound(context, "档案统计不可用");
        }

        var now = DateTime.Now;
        var monthlyResult = await statistics.GetMonthlyArchivesCountAsync(now.Year, now.Month, ct);
        if (monthlyResult.IsFailure)
        {
            return MapResult(context, monthlyResult);
        }

        return Results.Json(new
        {
            totalArchives = totalResult.Value.TotalArchives,
            monthlyArchives = monthlyResult.Value
        });
    }

    /// <summary>
    /// GET /api/archives/search?keyword=
    /// 当前库（前 20 条）在前，导入库（前 20 条）在后。
    /// </summary>
    private static async Task<IResult> SearchAsync(
        HttpContext context,
        INewPermissionService permissions,
        IApplicationService applications,
        IImportedArchiveService importedArchives,
        CancellationToken ct,
        string? keyword)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var trimmed = keyword?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return InvalidRequest(context, "请输入姓名或身份证号后再搜索");
        }

        var currentTask = applications.GetPagedAsync(1, 20, null, trimmed, ct);
        var importedTask = importedArchives.SearchAsync(trimmed, ct);
        await Task.WhenAll(currentTask, importedTask);

        if (currentTask.Result.IsFailure)
        {
            return MapResult(context, currentTask.Result);
        }

        if (importedTask.Result.IsFailure)
        {
            return MapResult(context, importedTask.Result);
        }

        var items = new List<ArchiveSearchItemResponse>();

        foreach (var app in currentTask.Result.Value?.Items ?? [])
        {
            items.Add(new ArchiveSearchItemResponse(
                Source: (int)ArchiveSearchSource.Current,
                SourceDisplay: "当前库",
                ApplicationId: app.Id,
                SourceTable: string.Empty,
                SourceId: 0,
                ApplicantName: app.ApplicantName ?? string.Empty,
                ApplicantIdCard: app.ApplicantIdCard ?? string.Empty,
                Address: app.Address ?? string.Empty,
                FamilySize: app.FamilySize,
                TotalAmount: app.TotalGuaranteeAmount,
                MatchedPersonInfo: string.Empty,
                IsFromCurrentLibrary: true));
        }

        foreach (var item in (importedTask.Result.Value ?? []).Take(20))
        {
            items.Add(new ArchiveSearchItemResponse(
                Source: (int)item.Source,
                SourceDisplay: item.SourceDisplay,
                ApplicationId: item.ApplicationId,
                SourceTable: item.SourceTable,
                SourceId: item.SourceId,
                ApplicantName: item.ApplicantName,
                ApplicantIdCard: item.ApplicantIdCard,
                Address: item.Address,
                FamilySize: item.FamilySize,
                TotalAmount: item.TotalAmount,
                MatchedPersonInfo: item.MatchedPersonInfo,
                IsFromCurrentLibrary: false));
        }

        return Results.Json(new { items });
    }

    /// <summary>
    /// GET /api/archives/current/{applicationId} → { applicationId, summary, form }
    /// </summary>
    private static async Task<IResult> GetCurrentDetailAsync(
        long applicationId,
        HttpContext context,
        INewPermissionService permissions,
        IApplicationService applications,
        ApplicationFormService forms,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        if (applicationId <= 0)
        {
            return InvalidRequest(context, "applicationId 必须为正整数");
        }

        var appResult = await applications.GetByIdAsync(applicationId, ct);
        if (appResult.IsFailure)
        {
            return MapResult(context, appResult);
        }

        var app = appResult.Value;
        if (app is null)
        {
            return NotFound(context, "当前库档案不存在");
        }

        var formResult = await forms.GetFormAsync(applicationId, user.Id, ct);
        if (formResult.IsFailure)
        {
            return MapResult(context, formResult);
        }

        var summary = new ArchiveCurrentSummary(
            ApplicantName: app.ApplicantName ?? string.Empty,
            ApplicantIdCard: app.ApplicantIdCard ?? string.Empty,
            ClassificationResult: ResolveClassification(app.ClassificationResult),
            Address: AddressResolver.BuildAddressFromText(
                app.District, app.Town, app.Community, app.Address),
            FamilySize: app.FamilySize,
            TotalGuaranteeAmount: app.TotalGuaranteeAmount,
            FirstApprovedAt: app.FirstApprovedAt);

        return Results.Json(new
        {
            applicationId,
            summary,
            form = formResult.Value
        });
    }

    /// <summary>
    /// GET /api/archives/imported?source=&amp;sourceTable=&amp;sourceId=
    /// source 为旧版 <see cref="ArchiveSearchSource"/> 数字；sourceTable 由 Core 白名单校验。
    /// </summary>
    private static async Task<IResult> GetImportedDetailAsync(
        HttpContext context,
        INewPermissionService permissions,
        IImportedArchiveService importedArchives,
        CancellationToken ct,
        int? source,
        string? sourceTable,
        long? sourceId)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (source is not > 0 || string.IsNullOrWhiteSpace(sourceTable) || sourceId is not > 0)
        {
            return InvalidRequest(context, "source、sourceTable、sourceId 不能为空");
        }

        if (source is < 1 or > 6)
        {
            return InvalidRequest(context, "source 必须是导入库来源（1-6）");
        }

        var item = new ArchiveSearchResultItem
        {
            Source = (ArchiveSearchSource)source.Value,
            SourceTable = sourceTable.Trim(),
            SourceId = sourceId.Value
        };

        var result = await importedArchives.GetImportedFamilyDetailAsync(item, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    // ───────────────────────────── 证明模板 / 单位模板 ─────────────────────────────

    /// <summary>
    /// GET /api/archives/proof/templates → { generic: [...], units: [...] }
    /// generic 为分类「证明文件」的通配模板；units 为单位专属模板映射。
    /// </summary>
    private static async Task<IResult> GetProofTemplatesAsync(
        HttpContext context,
        INewPermissionService permissions,
        ArchivePrintQueueService queue,
        ProofUnitTemplateService unitTemplates,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var genericResult = await queue.ListTemplatesAsync([ProofCategory], ct);
        if (genericResult.IsFailure)
        {
            return MapResult(context, genericResult);
        }

        var unitsResult = await unitTemplates.GetAllAsync(ct);
        if (unitsResult.IsFailure)
        {
            return MapResult(context, unitsResult);
        }

        var generic = (genericResult.Value ?? [])
            .Select(template => new
            {
                id = template.Id,
                name = template.Name,
                fileType = NormalizeFileType(template.FileType)
            })
            .ToList();

        var units = (unitsResult.Value ?? [])
            .Select(unit => new
            {
                id = unit.Id,
                unitName = unit.UnitName,
                templateId = unit.TemplateId,
                templateName = unit.TemplateName,
                fileType = NormalizeFileType(unit.FileType),
                createdAt = unit.CreatedAt,
                updatedAt = unit.UpdatedAt
            })
            .ToList();

        return Results.Json(new { generic, units });
    }

    /// <summary>POST /api/archives/proof/units（multipart：unitName、fileType、file）</summary>
    private static async Task<IResult> CreateProofUnitAsync(
        HttpContext context,
        INewPermissionService permissions,
        ProofUnitTemplateService unitTemplates,
        CancellationToken ct,
        [FromForm] string? unitName,
        [FromForm] string? fileType,
        [FromForm] string? configJson,
        IFormFile? file)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var data = await ReadAllBytesAsync(file, ct);
        var result = await unitTemplates.CreateAsync(
            unitName, fileType, data, file?.FileName, configJson, user.Id, ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new { id = result.Value });
    }

    /// <summary>PUT /api/archives/proof/units/{id}（multipart；file 可空，非空时替换模板）</summary>
    private static async Task<IResult> UpdateProofUnitAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ProofUnitTemplateService unitTemplates,
        CancellationToken ct,
        [FromForm] string? unitName,
        [FromForm] string? fileType,
        [FromForm] string? configJson,
        IFormFile? file)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        if (id <= 0)
        {
            return InvalidRequest(context, "单位模板 ID 无效");
        }

        var data = await ReadAllBytesAsync(file, ct);
        var result = await unitTemplates.UpdateAsync(
            id, unitName, fileType, data, file?.FileName, configJson, user.Id, ct);

        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    /// <summary>DELETE /api/archives/proof/units/{id}（只删映射，保留模板文件）</summary>
    private static async Task<IResult> DeleteProofUnitAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ProofUnitTemplateService unitTemplates,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (id <= 0)
        {
            return InvalidRequest(context, "单位模板 ID 无效");
        }

        var result = await unitTemplates.DeleteAsync(id, ct);
        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    // ───────────────────────────── 证明打印 ─────────────────────────────

    /// <summary>
    /// POST /api/archives/proof/jobs
    /// body: { applicationId?, source?, sourceTable?, sourceId?, templateId, targetUnitName?, mode, copies?, isDuplex? }
    /// 任务固定为 TemplateBatch，目标打印机取当前账号默认绑定。
    /// </summary>
    private static async Task<IResult> CreateProofJobAsync(
        ArchiveProofJobRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ArchiveProofDataService proofData,
        ArchivePrintQueueService queue,
        PrinterBindingService printers,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.PRINT_LOWINCOMEPROOF, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        if (request.TemplateId is not > 0)
        {
            return InvalidRequest(context, "templateId 必须为正整数");
        }

        var mode = (request.Mode ?? "print").Trim().ToLowerInvariant();
        if (!ProofModes.Contains(mode, StringComparer.Ordinal))
        {
            return InvalidRequest(context, "mode 只能是 preview / print / save");
        }

        var templateResult = await queue.GetTemplateMetaAsync(request.TemplateId.Value, ct);
        if (templateResult.IsFailure)
        {
            return MapResult(context, templateResult);
        }

        var template = templateResult.Value;
        if (template is null)
        {
            return NotFound(context, "证明模板不存在");
        }

        // 当前库优先；没有 applicationId 时按导入库 source/sourceTable/sourceId 取数。
        Result<ArchiveProofData> dataResult;
        long businessId;
        if (request.ApplicationId is > 0)
        {
            businessId = request.ApplicationId.Value;
            dataResult = await proofData.BuildCurrentAsync(
                businessId, request.TargetUnitName, user, ct);
        }
        else
        {
            if (request.Source is not > 0
                || string.IsNullOrWhiteSpace(request.SourceTable)
                || request.SourceId is not > 0)
            {
                return InvalidRequest(
                    context, "applicationId 或 source/sourceTable/sourceId 必须提供");
            }

            if (request.Source is < 1 or > 6)
            {
                return InvalidRequest(context, "source 必须是导入库来源（1-6）");
            }

            businessId = request.SourceId.Value;
            var item = new ArchiveSearchResultItem
            {
                Source = (ArchiveSearchSource)request.Source.Value,
                SourceTable = request.SourceTable.Trim(),
                SourceId = request.SourceId.Value
            };
            dataResult = await proofData.BuildImportedAsync(
                item, request.TargetUnitName, user, ct);
        }

        if (dataResult.IsFailure)
        {
            return MapResult(context, dataResult);
        }

        var proof = dataResult.Value;
        if (proof is null)
        {
            return NotFound(context, "证明字段装配失败");
        }

        // 目标打印机：优先前端选择的绑定，其次默认绑定（preview/print/save 均必须有绑定）。
        var targetResult = await printers.ResolveTargetAsync(
            user.Id, request.PrinterBindingId, request.PrinterName, ct);
        if (targetResult.IsFailure)
        {
            return MapResult(context, targetResult);
        }

        var target = targetResult.Value;
        if (target is null || target.DeviceId <= 0 || string.IsNullOrWhiteSpace(target.PrinterName))
        {
            return InvalidRequest(
                context, "当前账号未绑定打印机，请先在「我的打印机」中生成绑定码并完成绑定");
        }

        var period = DateTime.Now.ToString("yyyyMM", CultureInfo.InvariantCulture);
        var (_, extension) = ResolveFileType(template.FileType);

        var batch = new PrintBatchDefinition
        {
            Mode = mode,
            Category = ProofCategory,
            Period = period
        };

        batch.Parts.Add(new PrintBatchPart
        {
            TemplateId = template.Id,
            TemplateName = template.Name,
            FileName = BuildFileName(template.Name, extension),
            Fields = proof.Fields,
            TableRows = []
        });

        var jobRequest = new PrintJobRequest
        {
            BusinessType = PrintBusinessTypes.LowIncomeProof,
            BusinessId = businessId,
            Classification = ProofCategory,
            TemplateId = null,
            TemplateName = template.Name,
            ApplicantName = proof.ApplicantName,
            ApplicantIdCard = proof.ApplicantIdCard,
            PrinterName = target.PrinterName,
            TargetUserId = target.UserId,
            TargetDeviceId = target.DeviceId,
            TargetPrinter = target.PrinterName,
            Copies = Math.Clamp(request.Copies ?? 1, 1, 99),
            IsDuplex = request.IsDuplex ?? false,
            Fields = new Dictionary<string, string>(StringComparer.Ordinal),
            TableRows = [],
            RequestedBy = user.Id,
            RequestedByName = OperatorName(user),
            JobKind = PrintJobConstants.JobKindTemplateBatch,
            Batch = batch
        };

        var enqueueResult = await queue.EnqueueAsync(jobRequest, ct);
        if (enqueueResult.IsFailure)
        {
            return MapResult(context, enqueueResult);
        }

        var job = enqueueResult.Value;
        return Results.Json(new ArchiveProofJobResponse(
            job.Id, job.JobNo, job.Status, job.JobKind));
    }

    /// <summary>
    /// GET /api/archives/proof/history?applicationId=&amp;idCard=
    /// 只返回同一档案 + 同一户主身份证的 LowIncomeProof 任务（1:1 防串台）。
    /// </summary>
    private static async Task<IResult> GetProofHistoryAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        CancellationToken ct,
        long? applicationId,
        string? idCard)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (applicationId is not > 0 || string.IsNullOrWhiteSpace(idCard))
        {
            return Results.Json(new List<ArchiveProofHistoryItem>());
        }

        const string sql = @"SELECT id,
                                    template_name,
                                    printer_name,
                                    requested_by_name AS operator_name,
                                    status,
                                    created_at,
                                    completed_at
                             FROM nc_biz_print_jobs
                             WHERE business_type = $1
                               AND business_id = $2
                               AND applicant_id_card = $3
                               AND deleted_at IS NULL
                             ORDER BY created_at DESC
                             LIMIT 50";

        var result = await database.QueryAsync<ArchiveProofHistoryRow>(
            sql, ct, PrintBusinessTypes.LowIncomeProof, applicationId.Value, idCard.Trim());
        if (result.IsFailure)
        {
            return MapResult(context, result);
        }

        var items = (result.Value ?? [])
            .Select(row => new ArchiveProofHistoryItem(
                row.Id,
                row.TemplateName,
                row.PrinterName,
                row.OperatorName,
                row.Status,
                row.CreatedAt,
                row.CompletedAt))
            .ToList();

        return Results.Json(items);
    }

    // ───────────────────────────── 工具 ─────────────────────────────

    private static async Task<byte[]?> ReadAllBytesAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
        {
            return null;
        }

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static (string FileType, string Extension) ResolveFileType(string? fileType)
    {
        try
        {
            var normalized = TemplateFileTypes.Normalize(fileType);
            return (normalized, TemplateFileTypes.ToExtension(normalized));
        }
        catch (NotSupportedException)
        {
            var fallback = string.IsNullOrWhiteSpace(fileType)
                ? TemplateFileTypes.Word
                : fileType.Trim().ToLowerInvariant();
            return (fallback, ".docx");
        }
    }

    private static string NormalizeFileType(string? fileType)
    {
        try
        {
            return TemplateFileTypes.Normalize(fileType);
        }
        catch (NotSupportedException)
        {
            return fileType?.Trim().ToLowerInvariant() ?? string.Empty;
        }
    }

    private static string BuildFileName(string? name, string extension)
    {
        var safe = string.IsNullOrWhiteSpace(name) ? "证明" : name.Trim();
        if (safe.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
            || safe.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            safe = Path.GetFileNameWithoutExtension(safe);
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, '_');
        }

        return safe + extension;
    }

    private static string ResolveClassification(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        var fullName = ClassificationConstants.ConvertToFullName(code.Trim());
        return string.IsNullOrWhiteSpace(fullName) ? code.Trim() : fullName;
    }

    private static string OperatorName(UserDto user)
        => string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName!;

    private static IResult MapResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.APPLICATION_NOT_FOUND
            or ErrorCodes.RECORD_NOT_FOUND
            or ErrorCodes.NOT_FOUND)
        {
            return NotFound(context, result.Message ?? "请求的资源不存在");
        }

        if (result.ErrorCode == ErrorCodes.VALIDATION_FAILED)
        {
            return InvalidRequest(context, result.Message ?? "请求参数校验失败");
        }

        return ApiErrors.FromResult(context, result);
    }

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError, message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired, "请先登录");

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound, message);
}

#region 请求/响应契约

/// <summary>档案搜索结果项（当前库 + 导入库统一结构）。</summary>
public sealed record ArchiveSearchItemResponse(
    int Source,
    string SourceDisplay,
    long ApplicationId,
    string SourceTable,
    long SourceId,
    string ApplicantName,
    string ApplicantIdCard,
    string Address,
    int FamilySize,
    decimal TotalAmount,
    string MatchedPersonInfo,
    bool IsFromCurrentLibrary);

/// <summary>当前库档案摘要。</summary>
public sealed record ArchiveCurrentSummary(
    string ApplicantName,
    string ApplicantIdCard,
    string ClassificationResult,
    string Address,
    int FamilySize,
    decimal TotalGuaranteeAmount,
    DateTime? FirstApprovedAt);

/// <summary>POST /api/archives/proof/jobs 请求体。</summary>
public sealed record ArchiveProofJobRequest(
    long? ApplicationId,
    int? Source,
    string? SourceTable,
    long? SourceId,
    long? TemplateId,
    string? TargetUnitName,
    string? Mode,
    int? Copies,
    bool? IsDuplex,
    long? PrinterBindingId,
    string? PrinterName);

/// <summary>POST /api/archives/proof/jobs 响应。</summary>
public sealed record ArchiveProofJobResponse(
    long Id,
    string JobNo,
    string Status,
    string JobKind);

/// <summary>GET /api/archives/proof/history 列表项。</summary>
public sealed record ArchiveProofHistoryItem(
    long Id,
    string? TemplateName,
    string? PrinterName,
    string? OperatorName,
    string Status,
    DateTime CreatedAt,
    DateTime? CompletedAt);

/// <summary>证明打印历史查询行。</summary>
public sealed class ArchiveProofHistoryRow
{
    public long Id { get; set; }

    public string? TemplateName { get; set; }

    public string? PrinterName { get; set; }

    public string? OperatorName { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}

#endregion
