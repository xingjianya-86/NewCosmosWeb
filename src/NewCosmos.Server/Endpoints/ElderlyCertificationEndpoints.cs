using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 高龄年度认证 HTTP API（合同 2.1~2.4，报表 2.5 不在本批）。
/// 查询 ELDERLY_VIEW；新建/生成名单/改结果/导入/停发 ELDERLY_EDIT。
/// 批次/任务/凭证的数据范围在 QueryService 内按户籍乡镇/村落实；写操作先 scoped 校验再落库。
/// </summary>
public static class ElderlyCertificationEndpoints
{
    public static IEndpointRouteBuilder MapElderlyCertificationEndpoints(this IEndpointRouteBuilder app)
    {
        // 2.1 批次 / 统计
        app.MapGet("/api/elderly/certifications/campaigns", ListCampaignsAsync);
        app.MapPost("/api/elderly/certifications/campaigns", CreateCampaignAsync);
        app.MapPost(
            "/api/elderly/certifications/campaigns/{id:long}/roster",
            GenerateRosterAsync);
        app.MapGet(
            "/api/elderly/certifications/campaigns/{id:long}/stats",
            GetStatsAsync);
        app.MapGet(
            "/api/elderly/certifications/campaigns/{id:long}/reports/{formKey}.xlsx",
            GetReportAsync);
        app.MapGet(
            "/api/elderly/certifications/campaigns/{id:long}/reports/{formKey}.pdf",
            GetReportPdfAsync);

        // 2.2 名单 / 任务 / 凭证
        app.MapGet(
            "/api/elderly/certifications/campaigns/{id:long}/tasks",
            ListTasksAsync);
        app.MapGet("/api/elderly/certifications/tasks/{id:long}", GetTaskAsync);
        app.MapPut(
            "/api/elderly/certifications/tasks/{id:long}/result",
            UpdateTaskResultAsync);
        app.MapGet(
            "/api/elderly/certifications/tasks/{id:long}/evidence",
            ListEvidenceAsync);
        app.MapGet(
            "/api/elderly/certifications/evidence/{id:long}/content",
            GetEvidenceContentAsync);

        // 2.4 停发预览 / 停发
        app.MapGet(
            "/api/elderly/certifications/campaigns/{id:long}/stop-preview",
            GetStopPreviewAsync);
        app.MapPost(
            "/api/elderly/certifications/tasks/{id:long}/stop-decision",
            SetStopDecisionAsync);
        app.MapPost(
            "/api/elderly/certifications/tasks/{id:long}/stop",
            StopTaskAsync);

        // 2.3 批量导入（multipart；端点手动读表单，先放开单请求体上限再读）
        app.MapPost(
                "/api/elderly/certifications/import/preview",
                PreviewImportAsync)
            .WithMetadata(new RequestSizeLimitAttribute(
                ElderlyCertificationImportSessionService.MaxRequestBodyBytes));
        app.MapPost("/api/elderly/certifications/import/apply", ApplyImportAsync);

        return app;
    }

    // ───────────────────────────── 2.1 批次 / 统计 ─────────────────────────────

    private static async Task<IResult> ListCampaignsAsync(
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationQueryService query,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await query.GetCampaignsAsync(userId, ct);
        return result.IsFailure
            ? MapCertificationResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> CreateCampaignAsync(
        ElderlyCertCampaignCreateRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationWriteService write,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_EDIT, ct);
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

        var result = await write.CreateCampaignAsync(request, OperatorName(user), ct);
        return result.IsFailure
            ? MapCertificationResult(context, result)
            : Results.Json(new ElderlyCertCampaignCreatedDto(result.Value));
    }

    private static async Task<IResult> GenerateRosterAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationWriteService write,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var result = await write.GenerateRosterAsync(user.Id, id, OperatorName(user), ct);
        return result.IsFailure
            ? MapCertificationResult(context, result)
            : Results.Json(new ElderlyCertRosterResultDto(result.Value));
    }

    private static async Task<IResult> GetStatsAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationQueryService query,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await query.GetStatsAsync(userId, id, ct);
        return result.IsFailure
            ? MapCertificationResult(context, result)
            : Results.Json(result.Value);
    }

    private static Task<IResult> GetReportAsync(
        long id,
        string formKey,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationReportQueryService reports,
        CancellationToken ct)
        => GetReportFileAsync(id, formKey, isPdf: false, context, permissions, reports, ct);

    private static Task<IResult> GetReportPdfAsync(
        long id,
        string formKey,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationReportQueryService reports,
        CancellationToken ct)
        => GetReportFileAsync(id, formKey, isPdf: true, context, permissions, reports, ct);

    private static async Task<IResult> GetReportFileAsync(
        long id,
        string formKey,
        bool isPdf,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationReportQueryService reports,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = isPdf
            ? await reports.ExportPdfAsync(userId, id, formKey, ct)
            : await reports.ExportExcelAsync(userId, id, formKey, ct);
        if (result.IsFailure)
        {
            return MapCertificationResult(context, result);
        }

        var file = result.Value;
        return Results.File(
            file.Content,
            isPdf
                ? "application/pdf"
                : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileDownloadName: file.FileName,
            enableRangeProcessing: true);
    }

    // ───────────────────────────── 2.2 名单 / 任务 / 凭证 ─────────────────────────────

    private static async Task<IResult> ListTasksAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationQueryService query,
        CancellationToken ct,
        int? page,
        int? pageSize,
        string? keyword,
        string? certStatus,
        string? evidenceStatus)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await query.GetTasksPagedAsync(
            userId,
            id,
            page ?? 1,
            pageSize ?? ElderlyCertificationQueryService.DefaultPageSize,
            keyword,
            certStatus,
            evidenceStatus,
            ct);

        return result.IsFailure
            ? MapCertificationResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetTaskAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationQueryService query,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await query.GetTaskDetailAsync(userId, id, ct);
        return result.IsFailure
            ? MapCertificationResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> UpdateTaskResultAsync(
        long id,
        ElderlyCertResultUpdateRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationWriteService write,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_EDIT, ct);
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

        var result = await write.UpdateResultAsync(
            user.Id, id, request, OperatorName(user), ct);
        return result.IsFailure
            ? MapCertificationResult(context, result)
            : Results.NoContent();
    }

    private static async Task<IResult> ListEvidenceAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationQueryService query,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await query.GetEvidenceAsync(userId, id, ct);
        return result.IsFailure
            ? MapCertificationResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetEvidenceContentAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationQueryService query,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await query.GetEvidenceContentAsync(userId, id, ct);
        if (result.IsFailure)
        {
            return MapCertificationResult(context, result);
        }

        var content = result.Value;
        return Results.File(content.Data, content.ContentType, enableRangeProcessing: true);
    }

    // ───────────────────────────── 2.4 停发预览 / 停发 ─────────────────────────────

    private static async Task<IResult> GetStopPreviewAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationQueryService query,
        CancellationToken ct,
        int? page,
        int? pageSize,
        string? keyword,
        string? certStatus)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await query.GetStopPreviewPagedAsync(
            userId,
            id,
            page ?? 1,
            pageSize ?? ElderlyCertificationQueryService.DefaultPageSize,
            keyword,
            certStatus,
            ct);

        return result.IsFailure
            ? MapCertificationResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> SetStopDecisionAsync(
        long id,
        ElderlyCertStopDecisionRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationWriteService write,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_EDIT, ct);
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

        var result = await write.SetStopDecisionAsync(
            user.Id, id, request, OperatorName(user), ct);
        return result.IsFailure
            ? MapCertificationResult(context, result)
            : Results.NoContent();
    }

    private static async Task<IResult> StopTaskAsync(
        long id,
        ElderlyCertStopRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationWriteService write,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_EDIT, ct);
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

        var result = await write.StopAsync(user.Id, id, request, OperatorName(user), ct);
        return result.IsFailure
            ? MapCertificationResult(context, result)
            : Results.NoContent();
    }

    // ───────────────────────────── 2.3 批量导入 ─────────────────────────────

    /// <summary>
    /// multipart: excel（≤20MB）、zip（≤200MB）、campaignId。
    /// 端点先放开单请求体上限再手动读表单，避免 200MB ZIP 被全局 128MB 默认值截断。
    /// </summary>
    private static async Task<IResult> PreviewImportAsync(
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationQueryService query,
        ElderlyCertificationImportSessionService sessions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!context.Request.HasFormContentType)
        {
            return InvalidRequest(context, "请使用 multipart/form-data 上传 Excel 与 ZIP");
        }

        var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = ElderlyCertificationImportSessionService.MaxRequestBodyBytes;
        }

        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(
                new FormOptions
                {
                    BufferBody = false,
                    MultipartBodyLengthLimit = ElderlyCertificationImportSessionService.MaxRequestBodyBytes,
                    ValueLengthLimit = 64 * 1024,
                    ValueCountLimit = 64
                },
                ct);
        }
        catch (InvalidDataException ex)
        {
            return InvalidRequest(context, $"上传表单无效或文件超出大小限制：{ex.Message}");
        }
        catch (BadHttpRequestException ex)
        {
            return InvalidRequest(context, $"上传请求无效：{ex.Message}");
        }

        if (!long.TryParse(form["campaignId"].FirstOrDefault(), out var campaignId)
            || campaignId <= 0)
        {
            return InvalidRequest(context, "campaignId 不能为空");
        }

        var excel = form.Files.GetFile("excel");
        var zip = form.Files.GetFile("zip");

        var visible = await query.GetVisibleCampaignAsync(userId, campaignId, ct);
        if (visible.IsFailure)
        {
            return MapCertificationResult(context, visible);
        }

        var result = await sessions.PreviewAsync(userId, campaignId, excel, zip, ct);
        return result.IsFailure
            ? MapImportResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ApplyImportAsync(
        ElderlyCertImportApplyRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyCertificationQueryService query,
        ElderlyCertificationImportSessionService sessions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        if (request is null || request.PreviewId <= 0)
        {
            return InvalidRequest(context, "previewId 不能为空");
        }

        if (!sessions.TryGetSessionCampaignId(request.PreviewId, user.Id, out var campaignId))
        {
            return ApiErrors.Write(
                context,
                StatusCodes.Status409Conflict,
                ElderlyCertificationApiCodes.PreviewExpired,
                "预览已过期或临时文件已清理，请重新上传并预览");
        }

        // apply 前再按批次做一次数据范围校验，防止预览后权限/范围变更。
        var visible = await query.GetVisibleCampaignAsync(user.Id, campaignId, ct);
        if (visible.IsFailure)
        {
            return MapCertificationResult(context, visible);
        }

        var result = await sessions.ApplyAsync(user.Id, request, OperatorName(user), ct);
        if (result.IsFailure)
        {
            return MapImportResult(context, result);
        }

        var applied = result.Value;
        return Results.Json(new ElderlyCertImportApplyResponse(
            applied.BatchId,
            applied.BatchNo,
            applied.AppliedRows,
            applied.SkippedRows,
            applied.ErrorRows,
            applied.EvidenceInserted,
            applied.EvidenceSkipped,
            applied.EvidenceUnmatched,
            applied.InfoChangesCreated,
            applied.InfoChangesSkipped,
            applied.InfoChangeErrors,
            applied.SummaryText));
    }

    // ───────────────────────────── 通用 ─────────────────────────────

    /// <summary>操作人一律取会话中的登录用户名，不接受前端传入。</summary>
    private static string OperatorName(UserDto user)
        => string.IsNullOrWhiteSpace(user.Username) ? user.FullName ?? "System" : user.Username;

    /// <summary>
    /// 认证模块错误出口：合同错误码 VALIDATION_FAILED / NOT_FOUND / CONFLICT，
    /// 其余（数据库等）交统一 ApiErrors 映射。
    /// </summary>
    private static IResult MapCertificationResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.RECORD_NOT_FOUND
            or ErrorCodes.NOT_FOUND
            or ErrorCodes.APPLICATION_NOT_FOUND
            or ApiErrorCodes.NotFound)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound, result.Message);
        }

        if (result.ErrorCode is ErrorCodes.DB_UNIQUE_VIOLATION or ErrorCodes.CONCURRENCY_CONFLICT)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status409Conflict,
                ElderlyCertificationApiCodes.Conflict, result.Message);
        }

        if (result.ErrorCode is ErrorCodes.VALIDATION_FAILED
            or ErrorCodes.INVALID_ID_CARD
            or ErrorCodes.REQUIRED_FIELD_MISSING
            or ErrorCodes.CONFIG_NOT_FOUND)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status400BadRequest,
                ErrorCodes.VALIDATION_FAILED, result.Message);
        }

        return ApiErrors.FromResult(context, result);
    }

    /// <summary>导入错误出口：PREVIEW_EXPIRED / IMPORT_FILE_CHANGED 固定 409，其余沿用认证映射。</summary>
    private static IResult MapImportResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ElderlyCertificationApiCodes.PreviewExpired
            or ElderlyCertificationApiCodes.ImportFileChanged)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status409Conflict,
                result.ErrorCode!, result.Message);
        }

        return MapCertificationResult(context, result);
    }

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status400BadRequest,
            ErrorCodes.VALIDATION_FAILED,
            message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context,
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired,
            "请先登录");
}
