using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.Recovery;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.UserManagement;
using NewCosmos.Services.Utilities;

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
    /// <summary>支持的输出模式：preview 预览 / print 打印 / save 保存 / open-folder 打开输出目录。</summary>
    private static readonly string[] PrintJobModes = ["preview", "print", "save", "open-folder"];

    /// <summary>打印任务列表/管理的任一权限（列表不限定单一业务域）。</summary>
    private static readonly string[] AnyPrintPermissions =
    [
        PermissionCodes.PRINT_FAMILYAPPLICATION,
        PermissionCodes.PRINT_ASSETVERIFICATION,
        PermissionCodes.PRINT_RECOVERY,
        PermissionCodes.PRINT_LOWINCOMEPROOF,
        PermissionCodes.TEMP_VIEW,
        PermissionCodes.ELDERLY_VIEW,
        PermissionCodes.DUTY_VIEW
    ];

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
    /// body: { businessType, businessId, templateId?, templateName?, bindingId?, printerName?, copies?, isDuplex?, mode? }
    /// mode = preview | print | save | open-folder（默认 print）。
    /// 支持 FamilyApplication / AssetVerification / TempRelief / Recovery；
    /// Recovery 与 save/open-folder 走 TemplateBatch，返回 missingFields 只提示不阻断。
    /// </summary>
    private static async Task<IResult> CreateJobAsync(
        PrintJobCreateRequest? request,
        HttpContext context,
        ArchivePrintDataService printData,
        ArchivePrintQueueService queue,
        PrinterBindingService printers,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var businessType = string.IsNullOrWhiteSpace(request.BusinessType)
            ? PrintBusinessTypes.FamilyApplication
            : request.BusinessType.Trim();

        var permissionCode = ResolvePrintPermission(businessType);
        if (permissionCode is null)
        {
            return InvalidRequest(context,
                "当前仅支持 FamilyApplication / AssetVerification / TempRelief / Recovery 推送打印");
        }

        var denied = await EndpointSecurity.RequirePermissionAsync(context, permissions, permissionCode, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        if (request.BusinessId is not > 0)
        {
            return InvalidRequest(context, "businessId 必须为正整数");
        }

        var mode = (request.Mode ?? "print").Trim().ToLowerInvariant();
        if (!PrintJobModes.Contains(mode, StringComparer.Ordinal))
        {
            return InvalidRequest(context, "mode 只能是 preview / print / save / open-folder");
        }

        var isRecovery = string.Equals(
            businessType, PrintBusinessTypes.Recovery, StringComparison.OrdinalIgnoreCase);
        var isOpenFolder = string.Equals(mode, "open-folder", StringComparison.Ordinal);

        // 追缴（含 preview/print）与任何 save/open-folder 走批量任务契约（Agent 已支持 TemplateBatch）。
        var useBatch = isRecovery || isOpenFolder || string.Equals(mode, "save", StringComparison.Ordinal);

        // 追缴模板按分类 + 名称精确解析（前端不必知道模板 ID）；其余业务沿用请求模板。
        PrintTemplateMetaRow? template = null;
        if (isRecovery && !isOpenFolder)
        {
            var templatesResult = await queue.ListTemplatesAsync([RecoveryPrintDataService.Category], ct);
            if (templatesResult.IsFailure)
            {
                return MapResult(context, templatesResult);
            }

            template = (templatesResult.Value ?? [])
                .FirstOrDefault(item => string.Equals(
                    item.Name, RecoveryPrintDataService.TemplateName, StringComparison.Ordinal));
            if (template is null)
            {
                return NotFound(context, $"模板不存在：{RecoveryPrintDataService.TemplateName}");
            }
        }
        else if (!isOpenFolder)
        {
            if (request.TemplateId is not > 0)
            {
                return InvalidRequest(context, "templateId 必须为正整数");
            }

            var templateResult = await queue.GetTemplateMetaAsync(request.TemplateId.Value, ct);
            if (templateResult.IsFailure)
            {
                return MapResult(context, templateResult);
            }

            template = templateResult.Value;
            if (template is null)
            {
                return NotFound(context, "模板不存在");
            }
        }

        // 数据装配：Recovery 用追缴打印数据服务；FamilyApplication 复用表单服务（数据范围 + 业务存在性）；
        // AssetVerification 复用资产核查打印数据服务；TempRelief 复用 Core 打印数据组装。
        ArchivePrintData data;
        RecoveryPrintData? recoveryData = null;
        if (isRecovery)
        {
            var recoveryPrintData = context.RequestServices.GetRequiredService<RecoveryPrintDataService>();
            var recoveryResult = await recoveryPrintData.BuildAsync(
                request.BusinessId.Value, user.OrganizationId, user.Username, ct);
            if (recoveryResult.IsFailure)
            {
                return MapResult(context, recoveryResult);
            }

            recoveryData = recoveryResult.Value;
            data = new ArchivePrintData
            {
                ApplicantName = recoveryData.PersonName,
                ApplicantIdCard = recoveryData.IdCard ?? string.Empty,
                Classification = RecoveryPrintDataService.Category
            };
            foreach (var (key, value) in recoveryData.Fields)
            {
                if (!string.IsNullOrWhiteSpace(value)) data.Fields[key] = value;
            }
        }
        else if (string.Equals(businessType, PrintBusinessTypes.AssetVerification, StringComparison.OrdinalIgnoreCase))
        {
            var checkService = context.RequestServices.GetRequiredService<AssetCheckPrintDataService>();
            var checkResult = await checkService.BuildAsync(request.BusinessId.Value, ct);
            if (checkResult.IsFailure)
            {
                return MapResult(context, checkResult);
            }

            var payload = checkResult.Value;
            data = new ArchivePrintData
            {
                ApplicantName = payload.Fields.GetValueOrDefault(FieldKeys.APPLICANT_NAME) ?? string.Empty,
                ApplicantIdCard = payload.Fields.GetValueOrDefault(FieldKeys.APPLICANT_ID_CARD) ?? string.Empty
            };
            foreach (var (key, value) in payload.Fields)
            {
                if (!string.IsNullOrWhiteSpace(value)) data.Fields[key] = value;
            }
            data.TableRows.AddRange(payload.TableRows);
        }
        else if (string.Equals(businessType, PrintBusinessTypes.TempRelief, StringComparison.OrdinalIgnoreCase))
        {
            var tempRelief = context.RequestServices.GetRequiredService<ITempReliefService>();
            var timeline = context.RequestServices.GetRequiredService<IBusinessTimelineService>();

            var appResult = await tempRelief.GetByIdAsync(request.BusinessId.Value, ct);
            if (appResult.IsFailure || appResult.Value is null)
            {
                return appResult.IsFailure
                    ? MapResult(context, appResult)
                    : NotFound(context, "临时救助申请不存在");
            }

            var app = appResult.Value;

            var membersResult = await tempRelief.GetMembersByApplicationIdAsync(app.Id, ct);
            if (membersResult.IsFailure)
            {
                return MapResult(context, membersResult);
            }

            var diseasesResult = await tempRelief.GetDiseasesByApplicationIdAsync(app.Id, ct);
            if (diseasesResult.IsFailure)
            {
                return MapResult(context, diseasesResult);
            }
            app.Diseases = diseasesResult.Value ?? new List<TempReliefDisease>();

            var accidentsResult = await tempRelief.GetAccidentsByApplicationIdAsync(app.Id, ct);
            if (accidentsResult.IsFailure)
            {
                return MapResult(context, accidentsResult);
            }
            app.Accidents = accidentsResult.Value ?? new List<TempReliefAccident>();

            var educationsResult = await tempRelief.GetEducationsByApplicationIdAsync(app.Id, ct);
            if (educationsResult.IsFailure)
            {
                return MapResult(context, educationsResult);
            }
            app.Educations = educationsResult.Value ?? new List<TempReliefEducation>();

            var (acceptanceDate, investigationDate) =
                await TempReliefPrintDataBuilder.ResolveScheduleAsync(timeline, app);

            var phoneResult = await tempRelief.GetReportUnitPhoneAsync(app.ReportUnit, ct);
            var contactPhone = phoneResult.IsSuccess ? phoneResult.Value ?? string.Empty : string.Empty;

            var fields = TempReliefPrintDataBuilder.BuildSingleFields(
                app,
                membersResult.Value ?? new List<TempReliefMember>(),
                contactPhone,
                acceptanceDate,
                investigationDate);

            var categories = ArchiveCategoryResolver.GetRecordCategories(
                TempReliefConstants.BusinessType, app.ReliefType);

            data = new ArchivePrintData
            {
                ApplicantName = app.ApplicantName ?? string.Empty,
                ApplicantIdCard = app.ApplicantIdCard ?? string.Empty,
                Classification = categories.FirstOrDefault() ?? app.ReliefType
            };
            foreach (var (key, value) in fields)
            {
                if (!string.IsNullOrWhiteSpace(value)) data.Fields[key] = value;
            }
        }
        else
        {
            // 数据范围 + 业务存在性：复用表单服务（只读，越权与不存在统一 404）。
            var dataResult = await printData.BuildAsync(request.BusinessId.Value, user.Id, ct);
            if (dataResult.IsFailure)
            {
                return MapResult(context, dataResult);
            }
            data = dataResult.Value;
        }

        var configJson = template is null
            ? null
            : (await queue.GetTemplateConfigAsync(template.Id, ct)) is { IsSuccess: true } configResult
                ? configResult.Value
                : null;

        // businessId 只有 FamilyApplication 才等于 nc_biz_applications.id；
        // 其他业务类型仅按身份证匹配，避免把核查记录 ID 误当申请 ID 取到别人照片。
        var photoApplicationId = string.Equals(businessType, PrintBusinessTypes.FamilyApplication, StringComparison.OrdinalIgnoreCase)
            ? request.BusinessId
            : null;

        // 批量任务由 Agent 按 batch.parts 渲染，照片占位符不参与批量契约。
        var images = useBatch
            ? []
            : await printData.BuildPhotoImagesAsync(
                data.ApplicantIdCard, photoApplicationId, configJson, ct);

        var printerName = NormalizeOptional(request.PrinterName, 300);
        var templateName = NormalizeOptional(request.TemplateName, 300)
            ?? template?.Name
            ?? (isRecovery ? RecoveryPrintDataService.TemplateName : businessType);

        // 任务目标固化：bindingId 优先；只有 printerName 时按当前账号绑定匹配；
        // 都没传时退回默认绑定（预览兼容旧前端），无绑定时目标为空（新认领协议不会领取）。
        var targetResult = await printers.ResolveTargetAsync(
            user.Id, request.BindingId, request.PrinterName, ct);
        if (targetResult.IsFailure)
        {
            return MapResult(context, targetResult);
        }

        var target = targetResult.Value;

        // 批量月报口径：preview/print/save 必须有绑定；open-folder 只打开输出目录，不强制绑定。
        if (useBatch && !isOpenFolder
            && (target is null || target.DeviceId <= 0 || string.IsNullOrWhiteSpace(target.PrinterName)))
        {
            return InvalidRequest(context,
                "当前账号未绑定打印机，请先在「我的打印机」中生成绑定码并完成绑定");
        }

        PrintBatchDefinition? batch = null;
        if (useBatch)
        {
            var period = recoveryData?.Period ?? DateTime.Now.ToString("yyyyMM");
            batch = new PrintBatchDefinition
            {
                Mode = mode,
                Category = isRecovery
                    ? RecoveryPrintDataService.Category
                    : (data.Classification ?? businessType),
                Period = period
            };

            if (!isOpenFolder && template is not null)
            {
                var (_, extension) = ResolveFileType(template.FileType);
                batch.Parts.Add(new PrintBatchPart
                {
                    TemplateId = template.Id,
                    TemplateName = template.Name,
                    FileName = isRecovery
                        ? BuildRecoveryFileName(period, recoveryData!.PersonName, extension)
                        : BuildFileName(template.Name, extension),
                    Fields = data.Fields,
                    TableRows = data.TableRows
                });
            }
        }

        var jobRequest = new PrintJobRequest
        {
            BusinessType = businessType,
            BusinessId = request.BusinessId,
            Classification = data.Classification,
            TemplateId = useBatch ? null : template!.Id,
            TemplateName = templateName,
            ApplicantName = data.ApplicantName,
            ApplicantIdCard = data.ApplicantIdCard,
            PrinterName = target?.PrinterName ?? printerName,
            TargetUserId = target?.UserId,
            TargetDeviceId = target?.DeviceId,
            TargetPrinter = target?.PrinterName,
            Copies = Math.Clamp(request.Copies ?? 1, 1, 99),
            IsDuplex = request.IsDuplex ?? false,
            Fields = useBatch ? new Dictionary<string, string>(StringComparer.Ordinal) : data.Fields,
            TableRows = useBatch ? [] : data.TableRows,
            Images = images,
            RequestedBy = user.Id,
            RequestedByName = OperatorName(user),
            JobKind = useBatch
                ? PrintJobConstants.JobKindTemplateBatch
                : mode == "preview"
                    ? PrintJobConstants.JobKindPreview
                    : PrintJobConstants.JobKindTemplate,
            Batch = batch
        };

        var enqueueResult = await queue.EnqueueAsync(jobRequest, ct);
        if (enqueueResult.IsFailure)
        {
            return MapResult(context, enqueueResult);
        }

        var job = enqueueResult.Value;
        var missingFields = useBatch
            ? (isOpenFolder || template is null
                ? []
                : FindMissingFields(configJson, data.Fields, []))
            : FindMissingFields(configJson, data.Fields, images);

        return Results.Json(new PrintJobCreatedResponse(
            job.Id, job.JobNo, job.Status, job.JobKind, missingFields));
    }

    /// <summary>业务类型 → 打印权限码（未登记的类型返回 null）。</summary>
    private static string? ResolvePrintPermission(string businessType)
    {
        if (string.Equals(businessType, PrintBusinessTypes.FamilyApplication, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.PRINT_FAMILYAPPLICATION;
        if (string.Equals(businessType, PrintBusinessTypes.AssetVerification, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.PRINT_ASSETVERIFICATION;
        if (string.Equals(businessType, PrintBusinessTypes.TempRelief, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.TEMP_VIEW;
        if (string.Equals(businessType, PrintBusinessTypes.Recovery, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.PRINT_RECOVERY;
        if (string.Equals(businessType, PrintBusinessTypes.LowIncomeProof, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.PRINT_LOWINCOMEPROOF;
        if (string.Equals(businessType, PrintBusinessTypes.DeathStatement, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.PRINT_LOWINCOMEPROOF;
        return null;
    }

    /// <summary>
    /// 缺字段提示：模板 config.fields 中声明了 fieldKey，但本次数据无值（图片字段已下发图片则不算缺）。
    /// 只提示，不阻断任务创建。
    /// </summary>
    private static List<string> FindMissingFields(
        string? configJson,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<PrintImagePayload> images)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(configJson)) return missing;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(configJson);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return missing;
            if (!TryGetProperty(document.RootElement, "fields", out var fieldsElement)
                || fieldsElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return missing;
            }

            var providedImageKeys = images
                .Select(image => image.FieldKey)
                .OfType<string>()
                .Where(key => key.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fieldsElement.EnumerateArray())
            {
                if (field.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

                var fieldKey = TryGetProperty(field, "fieldKey", out var keyElement)
                               && keyElement.ValueKind == System.Text.Json.JsonValueKind.String
                    ? keyElement.GetString()?.Trim()
                    : null;
                if (string.IsNullOrEmpty(fieldKey) || !seen.Add(fieldKey)) continue;

                if (fields.TryGetValue(fieldKey, out var value) && !string.IsNullOrWhiteSpace(value)) continue;
                if (providedImageKeys.Contains(fieldKey)) continue;

                missing.Add(fieldKey);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // config 损坏时不做缺字段提示，不影响创建
        }

        return missing;
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
        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var denied = await RequireAnyPermissionAsync(
            context, permissions, userId, AnyPrintPermissions, ct);
        if (denied is not null)
        {
            return denied;
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

        var denied = await RequireJobPermissionAsync(
            context, permissions, userId, job.BusinessType, ct);
        if (denied is not null)
        {
            return denied;
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

        var denied = await RequireJobPermissionAsync(
            context, permissions, userId, job.BusinessType, ct);
        if (denied is not null)
        {
            return denied;
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

    /// <summary>
    /// POST /api/agent/jobs/claim body { max: 1, cachedTemplates: [{ templateId, templateHash }] }
    /// cachedTemplates 可选：老 Agent 不传时完全兼容，按原逻辑认领。
    /// 服务端受理并留痕该缓存清单；模板字节的真正裁剪以 payload 请求的 cachedTemplateHash 为准。
    /// </summary>
    private static async Task<IResult> ClaimJobsAsync(
        AgentClaimRequest? request,
        HttpContext context,
        ArchivePrintQueueService queue,
        ILoggerService logger,
        CancellationToken ct)
    {
        var (device, error) = await AuthorizeDeviceAsync(context, queue, ct);
        if (error is not null)
        {
            return error;
        }

        var max = Math.Clamp(request?.Max ?? 1, 1, 50);
        var cachedTemplates = NormalizeCachedTemplates(request?.CachedTemplates);
        if (cachedTemplates.Count > 0)
        {
            // 仅做受理/留痕：真正的裁剪以 payload 请求携带的 cachedTemplateHash 为准，
            // 避免认领时再读一次模板文件（file_data 较大）。
            logger.Info(
                $"[AgentClaim] 设备上报模板缓存 {cachedTemplates.Count} 条: Device={device!.MachineName}, Max={max}");
        }

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
    /// GET /api/agent/jobs/{id}/payload?cachedTemplateHash=xxx
    /// 下发模板字节（Base64）+ 任务快照。configJson 一并下发，Agent 才能把 FieldKeys 映射到模板占位符。
    /// cachedTemplateHash 可选：与任务模板 hash 一致时返回 cached=true 且不再下发 templateFileBase64
    /// （Agent 直接用本地模板缓存）；缺省/不一致/老 Agent 一律按原逻辑下发完整模板。
    /// </summary>
    private static async Task<IResult> GetJobPayloadAsync(
        string id,
        HttpContext context,
        ArchivePrintQueueService queue,
        CancellationToken ct,
        string? cachedTemplateHash = null)
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

        // 批量任务：template_id 为空，模板集合由 batch.parts 的 template_id 去重后逐个下发。
        if (!string.IsNullOrWhiteSpace(job.BatchJson))
        {
            return await BuildBatchPayloadAsync(context, job, queue, ct);
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
        var images = DeserializeImages(job.ImagesJson);
        var (fileType, extension) = ResolveFileType(template.FileType);
        var fileName = BuildFileName(template.Name, extension);
        var (tableStartMarker, tableEndMarker) = ReadTableMarkers(template.ConfigJson);
        var templateHash = ResolveTemplateHash(template.FileHash, template.FileData);
        var cacheHit = IsTemplateCacheHit(cachedTemplateHash, templateHash);

        return Results.Json(new AgentJobPayloadResponse(
            cacheHit ? null : Convert.ToBase64String(template.FileData),
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
            tableEndMarker,
            templateHash,
            images,
            cacheHit,
            Batch: null));
    }

    /// <summary>
    /// 批量任务 payload：反序列化 batch_json，按 distinct templateId 逐个取模板文件，
    /// 一次下发模板集合（Base64 + config_json + file_hash）与逐 part 字段，Agent 逐 part 渲染。
    /// </summary>
    private static async Task<IResult> BuildBatchPayloadAsync(
        HttpContext context,
        PrintJob job,
        ArchivePrintQueueService queue,
        CancellationToken ct)
    {
        PrintBatchDefinition? batch;
        try
        {
            batch = System.Text.Json.JsonSerializer.Deserialize<PrintBatchDefinition>(
                job.BatchJson!,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException)
        {
            batch = null;
        }

        if (batch is null)
        {
            return Conflict(context, "批量任务数据损坏，无法下发");
        }

        // jsonb 历史/异常数据可能把 parts 写成 null；统一按空批次处理。
        batch.Parts ??= [];

        // open-folder 只让 Agent 打开输出目录，允许没有 part；其他模式必须有输出内容。
        if (batch.Parts.Count == 0
            && !string.Equals(batch.Mode, "open-folder", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(context, "批量任务没有可输出的明细页");
        }

        var templates = new List<AgentBatchTemplate>();
        var templateIds = batch.Parts
            .Select(part => part.TemplateId)
            .Where(templateId => templateId > 0)
            .Distinct()
            .ToList();

        foreach (var templateId in templateIds)
        {
            var templateResult = await queue.GetTemplateFileAsync(templateId, ct);
            if (templateResult.IsFailure)
            {
                return MapResult(context, templateResult);
            }

            var template = templateResult.Value;
            if (template is null)
            {
                return NotFound(context, $"批量任务模板不存在（ID={templateId}）");
            }

            if (template.FileData is not { Length: > 0 })
            {
                return ApiErrors.Write(context, StatusCodes.Status409Conflict,
                    "TEMPLATE_FILE_EMPTY", $"模板文件为空，无法下发（ID={templateId}）");
            }

            var (fileType, extension) = ResolveFileType(template.FileType);
            templates.Add(new AgentBatchTemplate
            {
                TemplateId = template.Id,
                FileName = BuildFileName(template.Name, extension),
                FileType = fileType,
                FileBase64 = Convert.ToBase64String(template.FileData),
                ConfigJson = template.ConfigJson,
                TemplateHash = ResolveTemplateHash(template.FileHash, template.FileData),
            });
        }

        var payload = new AgentBatchPayload
        {
            Mode = batch.Mode,
            Category = batch.Category,
            Period = batch.Period,
            Templates = templates,
            Parts = batch.Parts.Select(part => new AgentBatchPart
            {
                TemplateId = part.TemplateId,
                FileName = part.FileName,
                Fields = part.Fields,
                TableRows = part.TableRows,
            }).ToList(),
        };

        return Results.Json(new AgentJobPayloadResponse(
            null,
            string.Empty,
            string.Empty,
            new Dictionary<string, string>(StringComparer.Ordinal),
            [],
            job.JobKind,
            job.PrinterName,
            job.Copies,
            job.IsDuplex,
            job.ApplicantName,
            job.ApplicantIdCard,
            null,
            null,
            null,
            null,
            [],
            false,
            payload));
    }

    /// <summary>POST /api/agent/jobs/{id}/complete（body 可选 { message }：降级打印等警告文案）。</summary>
    private static async Task<IResult> CompleteJobAsync(
        string id,
        HttpContext context,
        ArchivePrintQueueService queue,
        IRecoveryService recovery,
        ILoggerService logger,
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

        // 老版本 Agent 不带 body（空请求体也允许），只有显式 message 才作为警告回写。
        var warning = await ReadOptionalCompleteMessageAsync(context, ct);
        var result = await queue.CompleteAsync(jobId, warning, ct);
        if (result.IsFailure)
        {
            return MapResult(context, result);
        }

        // 追缴文书实际出纸后推进记录状态（预览/保存/打开目录不推进）；失败只记日志不阻断。
        await TryMarkRecoveryPrintedAsync(owned.Job, recovery, logger, ct);
        return Results.NoContent();
    }

    /// <summary>
    /// 追缴打印完成回调：BusinessType=Recovery 且为实际 print（batch.mode=print / 单模板 JobKind=Template）时，
    /// 将记录推进到 Printed。幂等重入由 Core 状态机保证。
    /// </summary>
    private static async Task TryMarkRecoveryPrintedAsync(
        PrintJob? job,
        IRecoveryService recovery,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (job is null
            || !string.Equals(job.BusinessType, PrintBusinessTypes.Recovery, StringComparison.OrdinalIgnoreCase)
            || job.BusinessId is not > 0
            || !IsPrintOutput(job))
        {
            return;
        }

        var result = await recovery.UpdateRecoveryRecordStatusAsync(
            job.BusinessId.Value, RecoveryConstants.STATUS_PRINTED, ct);
        if (result.IsFailure)
        {
            logger.Warn(
                $"追缴记录打印状态回写失败: RecordId={job.BusinessId} {result.ErrorCode} {result.Message}");
        }
    }

    /// <summary>是否实际出纸的 print（预览/保存/打开目录均不推进业务状态）。</summary>
    private static bool IsPrintOutput(PrintJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.BatchJson))
        {
            try
            {
                var batch = System.Text.Json.JsonSerializer.Deserialize<PrintBatchDefinition>(
                    job.BatchJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (batch is not null)
                {
                    return string.Equals(batch.Mode, "print", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // 批量快照损坏：按非 print 处理，避免误推进状态
                return false;
            }
        }

        return string.Equals(job.JobKind, PrintJobConstants.JobKindTemplate, StringComparison.Ordinal);
    }

    private static async Task<string?> ReadOptionalCompleteMessageAsync(HttpContext context, CancellationToken ct)
    {
        if (context.Request.ContentLength is null or 0) return null;

        try
        {
            using var document = await System.Text.Json.JsonDocument
                .ParseAsync(context.Request.Body, cancellationToken: ct)
                .ConfigureAwait(false);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!TryGetProperty(document.RootElement, "message", out var messageElement)) return null;
            if (messageElement.ValueKind != System.Text.Json.JsonValueKind.String) return null;

            var message = messageElement.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(message) ? null : message;
        }
        catch (System.Text.Json.JsonException)
        {
            // 非法 JSON 不阻断完成回写
            return null;
        }
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

    private static List<PrintImagePayload> DeserializeImages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<PrintImagePayload>>(
                       json,
                       new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>payload.templateHash：优先 file_hash；历史数据为空时按 file_data 现算 SHA256。</summary>
    private static string? ResolveTemplateHash(string? storedHash, byte[]? fileData)
    {
        if (!string.IsNullOrWhiteSpace(storedHash)) return storedHash.Trim().ToLowerInvariant();
        if (fileData is not { Length: > 0 }) return null;

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fileData)).ToLowerInvariant();
    }

    /// <summary>claim 上报缓存清单上限（与 Agent 侧一致）。</summary>
    private const int MaxCachedTemplateRefs = 64;

    /// <summary>清洗 claim 上报的模板缓存清单：丢弃非法 hash、去重、限制条数。</summary>
    private static List<AgentCachedTemplate> NormalizeCachedTemplates(
        IReadOnlyList<AgentCachedTemplate>? cachedTemplates)
    {
        var result = new List<AgentCachedTemplate>();
        if (cachedTemplates is not { Count: > 0 }) return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in cachedTemplates)
        {
            if (item is null || item.TemplateId <= 0) continue;

            var hash = NormalizeHash(item.TemplateHash);
            if (hash is null) continue;
            if (!seen.Add($"{item.TemplateId}:{hash}")) continue;

            result.Add(new AgentCachedTemplate(item.TemplateId, hash));
            if (result.Count >= MaxCachedTemplateRefs) break;
        }

        return result;
    }

    /// <summary>payload 缓存命中判定：Agent 上报 hash 与任务模板 hash 均为合法 SHA256 且完全一致。</summary>
    private static bool IsTemplateCacheHit(string? agentHash, string? templateHash)
    {
        var agent = NormalizeHash(agentHash);
        var expected = NormalizeHash(templateHash);
        return agent is not null
               && expected is not null
               && string.Equals(agent, expected, StringComparison.Ordinal);
    }

    /// <summary>规范化 hash（小写、16..128 位十六进制）；非法返回 null。</summary>
    private static string? NormalizeHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return null;

        var trimmed = hash.Trim().ToLowerInvariant();
        if (trimmed.Length is < 16 or > 128) return null;

        foreach (var c in trimmed)
        {
            if (!char.IsAsciiHexDigitLower(c) && !char.IsAsciiDigit(c)) return null;
        }

        return trimmed;
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

    /// <summary>追缴批量输出文件名：{yyyyMM}_追缴资金办理单_{姓名}.xlsx（姓名非法字符替换为下划线）。</summary>
    private static string BuildRecoveryFileName(string period, string? personName, string extension)
        => $"{period}_追缴资金办理单_{SanitizeFileNameSegment(personName)}{extension}";

    private static string SanitizeFileNameSegment(string? value)
    {
        var safe = string.IsNullOrWhiteSpace(value) ? "未命名" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safe = safe.Replace(invalid, '_');
        }

        return safe;
    }

    /// <summary>任一权限命中即放行（打印任务列表跨业务域）。</summary>
    private static async Task<IResult?> RequireAnyPermissionAsync(
        HttpContext context,
        INewPermissionService permissions,
        int userId,
        IReadOnlyCollection<string> permissionCodes,
        CancellationToken ct)
    {
        var checks = await permissions.CheckPermissionsAsync(userId, permissionCodes, ct);
        return checks.Values.Any(granted => granted)
            ? null
            : Forbidden(context, "没有权限执行该操作");
    }

    /// <summary>按任务 businessType 解析对应打印权限；未登记的历史业务类型退化为「任一打印权限」。</summary>
    private static async Task<IResult?> RequireJobPermissionAsync(
        HttpContext context,
        INewPermissionService permissions,
        int userId,
        string? businessType,
        CancellationToken ct)
    {
        var permissionCode = ResolveJobPermission(businessType);
        return permissionCode is not null
            ? await EndpointSecurity.RequirePermissionAsync(context, permissions, permissionCode, ct)
            : await RequireAnyPermissionAsync(context, permissions, userId, AnyPrintPermissions, ct);
    }

    /// <summary>打印任务 businessType → 管理/重试权限（与创建端保持同域）。</summary>
    private static string? ResolveJobPermission(string? businessType)
    {
        if (string.IsNullOrWhiteSpace(businessType))
        {
            return null;
        }

        var type = businessType.Trim();
        if (string.Equals(type, PrintBusinessTypes.FamilyApplication, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.PRINT_FAMILYAPPLICATION;
        if (string.Equals(type, PrintBusinessTypes.AssetVerification, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.PRINT_ASSETVERIFICATION;
        if (string.Equals(type, PrintBusinessTypes.AssetVerificationMonthlyReport, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.PRINT_ASSETVERIFICATIONMONTHLYREPORT;
        if (string.Equals(type, PrintBusinessTypes.TempRelief, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.TEMP_VIEW;
        if (string.Equals(type, PrintBusinessTypes.Recovery, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.PRINT_RECOVERY;
        if (string.Equals(type, PrintBusinessTypes.LowIncomeProof, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.PRINT_LOWINCOMEPROOF;
        if (string.Equals(type, PrintBusinessTypes.DeathStatement, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.PRINT_LOWINCOMEPROOF;
        if (string.Equals(type, PrintBusinessTypes.ElderlyBenefits, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.ELDERLY_VIEW;
        if (string.Equals(type, PrintBusinessTypes.NearRelative, StringComparison.OrdinalIgnoreCase))
            return NearRelativeConstants.PermissionManage;
        if (string.Equals(type, PrintBusinessTypes.Duty, StringComparison.OrdinalIgnoreCase))
            return PermissionCodes.DUTY_MANAGE;
        return null;
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

/// <summary>
/// POST /api/print/jobs 请求体。
/// mode=preview|print|save|open-folder（默认 print）：preview 预览 / print 出纸 / save 保存文件 / open-folder 打开输出目录。
/// Recovery 及 save/open-folder 走 TemplateBatch（模板由服务端按分类+名称解析，templateId 可空）。
/// </summary>
public sealed record PrintJobCreateRequest(
    string? BusinessType,
    long? BusinessId,
    long? TemplateId,
    string? TemplateName,
    string? PrinterName,
    int? Copies,
    bool? IsDuplex,
    string? Mode,
    // 打印机绑定 ID（GET /api/printers 的 id），后端据此固化 target_device_id/target_printer。
    long? BindingId = null);

/// <summary>POST /api/print/jobs 响应。missingFields 只做提示：模板声明但本次无值的字段键。</summary>
public sealed record PrintJobCreatedResponse(
    long Id,
    string JobNo,
    string Status,
    string JobKind,
    IReadOnlyList<string> MissingFields);

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

/// <summary>POST /api/agent/jobs/claim 请求体。cachedTemplates 可选（老 Agent 不传）。</summary>
public sealed record AgentClaimRequest(
    int? Max,
    IReadOnlyList<AgentCachedTemplate>? CachedTemplates);

/// <summary>Agent 上报的本地已缓存模板（templateId + SHA256）。</summary>
public sealed record AgentCachedTemplate(long TemplateId, string? TemplateHash);

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
/// templateHash + images 供 Agent 本地模板缓存与照片内嵌（images.placeholder 与 config 一致）。
/// cached=true 表示按 Agent 上报的 cachedTemplateHash 命中缓存，此时 templateFileBase64 为 null。
/// </summary>
public sealed record AgentJobPayloadResponse(
    [property: System.Text.Json.Serialization.JsonIgnore(
        Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? TemplateFileBase64,
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
    string? TableEndMarker,
    string? TemplateHash,
    IReadOnlyList<PrintImagePayload> Images,
    bool Cached,
    AgentBatchPayload? Batch);

/// <summary>
/// GET /api/agent/jobs/{id}/payload 批量任务段（JobKind=TemplateBatch）。
/// 模板按 templateId 去重后放在 templates；parts 逐页引用 templateId 并提供 fields/tableRows。
/// </summary>
public sealed class AgentBatchPayload
{
    /// <summary>preview | print | save | open-folder</summary>
    public string Mode { get; set; } = "preview";

    public string Category { get; set; } = "高龄月报";

    public string Period { get; set; } = "";

    public List<AgentBatchTemplate> Templates { get; set; } = new();

    public List<AgentBatchPart> Parts { get; set; } = new();
}

/// <summary>批量任务下发的模板文件（Base64 + config_json + file_hash）。</summary>
public sealed class AgentBatchTemplate
{
    public long TemplateId { get; set; }

    public string FileName { get; set; } = "";

    public string FileType { get; set; } = "xlsx";

    public string FileBase64 { get; set; } = "";

    public string? ConfigJson { get; set; }

    public string? TemplateHash { get; set; }
}

/// <summary>批量任务的单页输出单元（引用 templates 中的 templateId）。</summary>
public sealed class AgentBatchPart
{
    public long TemplateId { get; set; }

    public string? FileName { get; set; }

    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.Ordinal);

    public List<Dictionary<string, string>> TableRows { get; set; } = new();
}

/// <summary>POST /api/agent/jobs/{id}/fail 请求体。</summary>
public sealed record AgentFailRequest(string? ErrorMessage);

#endregion
