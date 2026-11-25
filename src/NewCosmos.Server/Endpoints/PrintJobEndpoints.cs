using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// Web 档案制作页推送打印：模板列表、任务入队/列表/重试/删除，以及 PC Agent 的领取/取数/回报。
/// <para>
/// Server 不生成 PDF（不引用 NewCosmos.Office）；Agent 按 payload 下发的模板字节 +
/// fields/tableRows/config_json 在 Windows 侧渲染。
/// </para>
/// </summary>
public static class PrintJobEndpoints
{
    public static IEndpointRouteBuilder MapPrintJobEndpoints(this IEndpointRouteBuilder app)
    {
        // Web 端（登录 + 打印权限）
        app.MapGet("/api/templates", ListTemplatesAsync);
        app.MapPost("/api/print/jobs", CreateJobAsync);
        app.MapGet("/api/print/jobs", ListJobsAsync);
        app.MapPost("/api/print/jobs/{id}/retry", RetryJobAsync);
        app.MapDelete("/api/print/jobs/{id}", DeleteJobAsync);

        // PC Agent（X-NC-Device-Key 设备密钥）
        app.MapPost("/api/agent/jobs/claim", ClaimJobsAsync);
        app.MapGet("/api/agent/jobs/{id}/payload", GetJobPayloadAsync);
        app.MapPost("/api/agent/jobs/{id}/complete", CompleteJobAsync);
        app.MapPost("/api/agent/jobs/{id}/fail", FailJobAsync);

        return app;
    }

    // ───────────────────────────── 模板 ─────────────────────────────

    /// <summary>
    /// GET /api/templates?businessType=FamilyApplication
    /// 只返回元数据（明确列，绝不带 file_data）；登录即可，模板查看权限码当前不存在。
    /// </summary>
    private static async Task<IResult> ListTemplatesAsync(
        HttpContext context,
        ArchivePrintQueueService queue,
        CancellationToken ct,
        string? businessType)
    {
        if (context.GetUserId() is null)
        {
            return Unauthorized(context);
        }

        string[]? categories = null;
        if (!string.IsNullOrWhiteSpace(businessType))
        {
            var domain = businessType.Trim();
            categories = ArchiveCategoryResolver.GetReprintDomainCategories(domain) ?? [domain];
        }

        var result = await queue.ListTemplatesAsync(categories, ct);
        if (result.IsFailure)
        {
            return MapResult(context, result);
        }

        var items = (result.Value ?? [])
            .Select(template => new TemplateListItem(
                template.Id,
                template.Name,
                template.DocType,
                template.FileType,
                template.SortOrder))
            .ToList();

        return Results.Json(items);
    }

    // ───────────────────────────── 任务 ─────────────────────────────

    /// <summary>
    /// POST /api/print/jobs
    /// body: { businessType, businessId, templateId, templateName?, printerName?, copies?, isDuplex?, mode? }
    /// mode = preview | print（默认 print）。预览与打印共用同一份数据快照，仅 JobKind 不同。
    /// </summary>
    private static async Task<IResult> CreateJobAsync(
        PrintJobCreateRequest? request,
        HttpContext context,
        ArchivePrintDataService printData,
        ArchivePrintQueueService queue,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.PRINT_FAMILYAPPLICATION, ct);
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

        var businessType = string.IsNullOrWhiteSpace(request.BusinessType)
            ? PrintBusinessTypes.FamilyApplication
            : request.BusinessType.Trim();
        if (!string.Equals(businessType, PrintBusinessTypes.FamilyApplication, StringComparison.OrdinalIgnoreCase))
        {
            return InvalidRequest(context, "当前仅支持 FamilyApplication 推送打印");
        }

        if (request.BusinessId is not > 0)
        {
            return InvalidRequest(context, "businessId 必须为正整数");
        }

        if (request.TemplateId is not > 0)
        {
            return InvalidRequest(context, "templateId 必须为正整数");
        }

        var mode = (request.Mode ?? "print").Trim().ToLowerInvariant();
        if (mode is not ("print" or "preview"))
        {
            return InvalidRequest(context, "mode 只能是 print 或 preview");
        }

        var templateResult = await queue.GetTemplateMetaAsync(request.TemplateId.Value, ct);
        if (templateResult.IsFailure)
        {
            return MapResult(context, templateResult);
        }

        var template = templateResult.Value;
        if (template is null)
        {
            return ApiErrors.Write(context, StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound, "模板不存在");
        }

        // 数据范围 + 业务存在性：复用表单服务（只读，越权与不存在统一 404）。
        var dataResult = await printData.BuildAsync(request.BusinessId.Value, user.Id, ct);
        if (dataResult.IsFailure)
        {
            return MapResult(context, dataResult);
        }

        var data = dataResult.Value;
        var printerName = NormalizeOptional(request.PrinterName, 300);
        var templateName = NormalizeOptional(request.TemplateName, 300) ?? template.Name;

        var jobRequest = new PrintJobRequest
        {
            BusinessType = PrintBusinessTypes.FamilyApplication,
            BusinessId = request.BusinessId,
            Classification = data.Classification,
            TemplateId = template.Id,
            TemplateName = templateName,
            ApplicantName = data.ApplicantName,
            ApplicantIdCard = data.ApplicantIdCard,
            PrinterName = printerName,
            Copies = Math.Clamp(request.Copies ?? 1, 1, 99),
            IsDuplex = request.IsDuplex ?? false,
            Fields = data.Fields,
            TableRows = data.TableRows,
            RequestedBy = user.Id,
            RequestedByName = OperatorName(user),
            JobKind = mode == "preview"
                ? PrintJobConstants.JobKindPreview
                : PrintJobConstants.JobKindTemplate
        };

        var enqueueResult = await queue.EnqueueAsync(jobRequest, ct);
        if (enqueueResult.IsFailure)
        {
            return MapResult(context, enqueueResult);
        }

        var job = enqueueResult.Value;
        return Results.Json(new PrintJobCreatedResponse(
            job.Id, job.JobNo, job.Status, job.JobKind));
    }

    /// <summary>GET /api/print/jobs?status=&amp;pageIndex=1&amp;pageSize=20（只返回当前登录用户的任务）。</summary>
    private static async Task<IResult> ListJobsAsync(
        HttpContext context,
        ArchivePrintQueueService queue,
        INewPermissionService permissions,
        CancellationToken ct,
        string? status,
        int? pageIndex,
        int? pageSize)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.PRINT_FAMILYAPPLICATION, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!TryNormalizeStatus(status, out var statusFilter))
        {
            return InvalidRequest(context, "status 只能是 Pending/Processing/Completed/Failed");
        }

        var result = await queue.GetPagedAsync(
            statusFilter, userId, pageIndex ?? 1, pageSize ?? 20, ct);
        if (result.IsFailure)
        {
            return MapResult(context, result);
        }

        var paged = result.Value;
        var items = paged.Items.Select(job => new PrintJobListItem(
            job.Id,
            job.JobNo,
            job.BusinessType,
            job.BusinessId,
            job.TemplateId,
            job.TemplateName,
            job.ApplicantName,
            job.PrinterName,
            job.Copies,
            job.IsDuplex,
            job.Status,
            job.StatusName,
            job.JobKind,
            job.JobKindName,
            job.JobKind == PrintJobConstants.JobKindPreview ? "preview" : "print",
            job.ErrorMessage,
            job.CreatedAt,
            job.ClaimedAt,
            job.CompletedAt)).ToList();

        return Results.Json(new PrintJobListResponse(
            items, paged.PageIndex, paged.PageSize, paged.TotalCount, paged.TotalPages));
    }

    /// <summary>POST /api/print/jobs/{id}/retry —— 仅 Failed 可重试。</summary>
    private static async Task<IResult> RetryJobAsync(
        string id,
        HttpContext context,
        ArchivePrintQueueService queue,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.PRINT_FAMILYAPPLICATION, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!TryParseId(id, out var jobId))
        {
            return InvalidRequest(context, "任务 ID 无效");
        }

        var jobResult = await queue.GetByIdAsync(jobId, ct);
        if (jobResult.IsFailure)
        {
            return MapResult(context, jobResult);
        }

        var job = jobResult.Value;
        if (job is null || job.RequestedBy != userId)
        {
            return NotFound(context, "打印任务不存在");
        }

        if (!string.Equals(job.Status, PrintJobConstants.StatusFailed, StringComparison.Ordinal))
        {
            return Conflict(context, "仅失败任务可以重试");
        }

        var retryResult = await queue.RetryAsync(jobId, ct);
        return retryResult.IsFailure
            ? MapResult(context, retryResult)
            : Results.Json(new PrintJobRetryResponse(jobId, PrintJobConstants.StatusPending));
    }

    /// <summary>DELETE /api/print/jobs/{id} —— 软删除，打印中不可删。</summary>
    private static async Task<IResult> DeleteJobAsync(
        string id,
        HttpContext context,
        ArchivePrintQueueService queue,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.PRINT_FAMILYAPPLICATION, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!TryParseId(id, out var jobId))
        {
            return InvalidRequest(context, "任务 ID 无效");
        }

        var jobResult = await queue.GetByIdAsync(jobId, ct);
        if (jobResult.IsFailure)
        {
            return MapResult(context, jobResult);
        }

        var job = jobResult.Value;
        if (job is null || job.RequestedBy != userId)
        {
            return NotFound(context, "打印任务不存在");
        }

        if (string.Equals(job.Status, PrintJobConstants.StatusProcessing, StringComparison.Ordinal))
        {
            return Conflict(context, "打印中的任务不可删除");
        }

        var deleteResult = await queue.DeleteAsync(jobId, ct);
        return deleteResult.IsFailure
            ? MapResult(context, deleteResult)
            : Results.NoContent();
    }

    // ───────────────────────────── Agent ─────────────────────────────

    /// <summary>POST /api/agent/jobs/claim body { max: 1 } -> 任务数组。</summary>
    private static async Task<IResult> ClaimJobsAsync(
        AgentClaimRequest? request,
        HttpContext context,
        ArchivePrintQueueService queue,
        CancellationToken ct)
    {
        var (device, error) = await AuthorizeDeviceAsync(context, queue, ct);
        if (error is not null)
        {
            return error;
        }

        var max = Math.Clamp(request?.Max ?? 1, 1, 50);
        var result = await queue.ClaimPendingAsync(max, device!.MachineName, device.Id, ct);
        if (result.IsFailure)
        {
            return MapResult(context, result);
        }

        var items = (result.Value ?? [])
            .Select(job => new AgentJobItem(
                job.Id,
                job.JobNo,
                job.JobKind,
                job.Status,
                job.TemplateId,
                job.TemplateName,
                job.PrinterName,
                job.Copies,
                job.IsDuplex,
                job.BusinessType,
                job.BusinessId))
            .ToList();

        return Results.Json(items);
    }

    /// <summary>
    /// GET /api/agent/jobs/{id}/payload
    /// 下发模板字节（Base64）+ 任务快照。configJson 一并下发，Agent 才能把 FieldKeys 映射到模板占位符。
    /// </summary>
    private static async Task<IResult> GetJobPayloadAsync(
        string id,
        HttpContext context,
        ArchivePrintQueueService queue,
        CancellationToken ct)
    {
        var (device, error) = await AuthorizeDeviceAsync(context, queue, ct);
        if (error is not null)
        {
            return error;
        }

        if (!TryParseId(id, out var jobId))
        {
            return InvalidRequest(context, "任务 ID 无效");
        }

        var jobResult = await queue.GetByIdAsync(jobId, ct);
        if (jobResult.IsFailure)
        {
            return MapResult(context, jobResult);
        }

        var job = jobResult.Value;
        if (job is null)
        {
            return NotFound(context, "打印任务不存在");
        }

        if (job.Status is not (PrintJobConstants.StatusPending or PrintJobConstants.StatusProcessing))
        {
            return Conflict(context, "任务已结束，无法获取打印数据");
        }

        if (!string.IsNullOrWhiteSpace(job.AgentMachine)
            && !string.Equals(job.AgentMachine, device!.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return Forbidden(context, "任务已被其他设备领取");
        }

        if (job.TemplateId is not > 0)
        {
            return InvalidRequest(context, "任务缺少模板 ID");
        }

        var templateResult = await queue.GetTemplateFileAsync(job.TemplateId.Value, ct);
        if (templateResult.IsFailure)
        {
            return MapResult(context, templateResult);
        }

        var template = templateResult.Value;
        if (template is null)
        {
            return NotFound(context, "模板不存在");
        }

        if (template.FileData is not { Length: > 0 })
        {
            return ApiErrors.Write(context, StatusCodes.Status409Conflict,
                "TEMPLATE_FILE_EMPTY", "模板文件为空，无法下发");
        }

        var fields = DeserializeFields(job.FieldsJson);
        var tableRows = DeserializeRows(job.TableRowsJson);
        var (fileType, extension) = ResolveFileType(template.FileType);
        var fileName = BuildFileName(template.Name, extension);
        var (tableStartMarker, tableEndMarker) = ReadTableMarkers(template.ConfigJson);

        return Results.Json(new AgentJobPayloadResponse(
            Convert.ToBase64String(template.FileData),
            fileName,
            fileType,
            fields,
            tableRows,
            job.JobKind,
            job.PrinterName,
            job.Copies,
            job.IsDuplex,
            job.ApplicantName,
            job.ApplicantIdCard,
            template.ConfigJson,
            tableStartMarker,
            tableEndMarker));
    }

    /// <summary>POST /api/agent/jobs/{id}/complete。</summary>
    private static async Task<IResult> CompleteJobAsync(
        string id,
        HttpContext context,
        ArchivePrintQueueService queue,
        CancellationToken ct)
    {
        var (device, error) = await AuthorizeDeviceAsync(context, queue, ct);
        if (error is not null)
        {
            return error;
        }

        if (!TryParseId(id, out var jobId))
        {
            return InvalidRequest(context, "任务 ID 无效");
        }

        var owned = await LoadOwnedProcessingJobAsync(jobId, device!, queue, context, ct);
        if (owned.Error is not null)
        {
            return owned.Error;
        }

        var result = await queue.CompleteAsync(jobId, ct);
        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    /// <summary>POST /api/agent/jobs/{id}/fail body { errorMessage }。</summary>
    private static async Task<IResult> FailJobAsync(
        string id,
        AgentFailRequest? request,
        HttpContext context,
        ArchivePrintQueueService queue,
        CancellationToken ct)
    {
        var (device, error) = await AuthorizeDeviceAsync(context, queue, ct);
        if (error is not null)
        {
            return error;
        }

        if (!TryParseId(id, out var jobId))
        {
            return InvalidRequest(context, "任务 ID 无效");
        }

        var owned = await LoadOwnedProcessingJobAsync(jobId, device!, queue, context, ct);
        if (owned.Error is not null)
        {
            return owned.Error;
        }

        var message = string.IsNullOrWhiteSpace(request?.ErrorMessage)
            ? "Agent 未提供失败原因"
            : request.ErrorMessage.Trim();

        var result = await queue.FailAsync(jobId, message, ct);
        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    private static async Task<(PrintJob? Job, IResult? Error)> LoadOwnedProcessingJobAsync(
        long jobId,
        PrintAgentDeviceRow device,
        ArchivePrintQueueService queue,
        HttpContext context,
        CancellationToken ct)
    {
        var jobResult = await queue.GetByIdAsync(jobId, ct);
        if (jobResult.IsFailure)
        {
            return (null, MapResult(context, jobResult));
        }

        var job = jobResult.Value;
        if (job is null)
        {
            return (null, NotFound(context, "打印任务不存在"));
        }

        if (!string.Equals(job.Status, PrintJobConstants.StatusProcessing, StringComparison.Ordinal))
        {
            return (null, Conflict(context, "任务未处于打印中状态"));
        }

        if (!string.Equals(job.AgentMachine, device.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return (null, Forbidden(context, "任务不属于当前设备"));
        }

        return (job, null);
    }

    private static async Task<(PrintAgentDeviceRow? Device, IResult? Error)> AuthorizeDeviceAsync(
        HttpContext context,
        ArchivePrintQueueService queue,
        CancellationToken ct)
    {
        var header = context.Request.Headers["X-NC-Device-Key"].ToString().Trim();
        if (!Guid.TryParse(header, out var deviceKey))
        {
            return (null, DeviceUnauthorized(context));
        }

        var result = await queue.GetDeviceByKeyAsync(deviceKey, ct);
        if (result.IsFailure)
        {
            return (null, MapResult(context, result));
        }

        return result.Value is null
            ? (null, DeviceUnauthorized(context))
            : (result.Value, null);
    }

    // ───────────────────────────── 工具 ─────────────────────────────

    private static Dictionary<string, string> DeserializeFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (System.Text.Json.JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private static List<Dictionary<string, string>> DeserializeRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
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
            var fallback = string.IsNullOrWhiteSpace(fileType) ? "docx" : fileType.Trim().ToLowerInvariant();
            return (fallback, ".docx");
        }
    }

    private static string BuildFileName(string name, string extension)
    {
        var safe = string.IsNullOrWhiteSpace(name) ? "template" : name.Trim();
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

    private static (string? Start, string? End) ReadTableMarkers(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return (null, null);
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(configJson);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return (null, null);
            }

            var root = document.RootElement;
            if (!TryGetProperty(root, "tables", out var tables)
                || tables.ValueKind != System.Text.Json.JsonValueKind.Array
                || tables.GetArrayLength() == 0)
            {
                return (null, null);
            }

            var table = tables[0];
            var start = TryGetProperty(table, "startMarker", out var startElement)
                        && startElement.ValueKind == System.Text.Json.JsonValueKind.String
                ? startElement.GetString()
                : null;
            var end = TryGetProperty(table, "endMarker", out var endElement)
                      && endElement.ValueKind == System.Text.Json.JsonValueKind.String
                ? endElement.GetString()
                : null;

            return (start, end);
        }
        catch (System.Text.Json.JsonException)
        {
            return (null, null);
        }
    }

    private static bool TryGetProperty(
        System.Text.Json.JsonElement element,
        string name,
        out System.Text.Json.JsonElement value)
    {
        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        // config_json 历史上有 camelCase / PascalCase 两种写法。
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryNormalizeStatus(string? status, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        normalized = status.Trim().ToLowerInvariant() switch
        {
            "pending" => PrintJobConstants.StatusPending,
            "processing" => PrintJobConstants.StatusProcessing,
            "completed" => PrintJobConstants.StatusCompleted,
            "failed" => PrintJobConstants.StatusFailed,
            _ => null
        };

        return normalized is not null;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static bool TryParseId(string id, out long value)
        => long.TryParse(id, out value) && value > 0;

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

    private static IResult DeviceUnauthorized(HttpContext context)
        => ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired, "设备不存在或已失效");

    private static IResult Forbidden(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status403Forbidden,
            ApiErrorCodes.Forbidden, message);

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound, message);

    private static IResult Conflict(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status409Conflict,
            "CONFLICT", message);
}

#region 请求/响应契约

/// <summary>GET /api/templates 单项（不含 file_data）。</summary>
public sealed record TemplateListItem(
    long Id,
    string Name,
    string DocType,
    string FileType,
    int SortOrder);

/// <summary>POST /api/print/jobs 请求体；mode=preview|print（默认 print）。</summary>
public sealed record PrintJobCreateRequest(
    string? BusinessType,
    long? BusinessId,
    long? TemplateId,
    string? TemplateName,
    string? PrinterName,
    int? Copies,
    bool? IsDuplex,
    string? Mode);

/// <summary>POST /api/print/jobs 响应。</summary>
public sealed record PrintJobCreatedResponse(
    long Id,
    string JobNo,
    string Status,
    string JobKind);

/// <summary>GET /api/print/jobs 列表项。</summary>
public sealed record PrintJobListItem(
    long Id,
    string JobNo,
    string BusinessType,
    long? BusinessId,
    long? TemplateId,
    string? TemplateName,
    string? ApplicantName,
    string? PrinterName,
    int Copies,
    bool IsDuplex,
    string Status,
    string StatusName,
    string JobKind,
    string JobKindName,
    string Mode,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? ClaimedAt,
    DateTime? CompletedAt);

/// <summary>GET /api/print/jobs 分页响应。</summary>
public sealed record PrintJobListResponse(
    IReadOnlyList<PrintJobListItem> Items,
    int PageIndex,
    int PageSize,
    int TotalCount,
    int TotalPages);

/// <summary>POST /api/print/jobs/{id}/retry 响应。</summary>
public sealed record PrintJobRetryResponse(long Id, string Status);

/// <summary>POST /api/agent/jobs/claim 请求体。</summary>
public sealed record AgentClaimRequest(int? Max);

/// <summary>POST /api/agent/jobs/claim 任务项。</summary>
public sealed record AgentJobItem(
    long Id,
    string JobNo,
    string JobKind,
    string Status,
    long? TemplateId,
    string? TemplateName,
    string? PrinterName,
    int Copies,
    bool IsDuplex,
    string BusinessType,
    long? BusinessId);

/// <summary>
/// GET /api/agent/jobs/{id}/payload 响应。
/// templateConfigJson 为附加字段：Agent 用它把 FieldKeys 映射到模板占位符（否则无法正确渲染）。
/// </summary>
public sealed record AgentJobPayloadResponse(
    string TemplateFileBase64,
    string TemplateFileName,
    string TemplateFileType,
    Dictionary<string, string> Fields,
    List<Dictionary<string, string>> TableRows,
    string JobKind,
    string? PrinterName,
    int Copies,
    bool IsDuplex,
    string? ApplicantName,
    string? ApplicantIdCard,
    string? TemplateConfigJson,
    string? TableStartMarker,
    string? TableEndMarker);

/// <summary>POST /api/agent/jobs/{id}/fail 请求体。</summary>
public sealed record AgentFailRequest(string? ErrorMessage);

#endregion
