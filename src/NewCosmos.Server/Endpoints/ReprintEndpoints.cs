using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Server.Services.Reprint;
using NewCosmos.Services.Core;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 历史补打中心（Server）：
/// 五域检索（低收入人口 / 临时救助 / 普惠高龄 / 资产核查 / 动态管理档案）、
/// 单条补打（模板 / 原样）、按月逐户批量任务与批次进度。
/// <para>
/// 权限：入口任一 INCOME_VIEW/TEMP_VIEW/ELDERLY_VIEW/ASSET_VIEW/CHANGE_VIEW；
/// 模板打印按域对应 PRINT_*；批量要求 ARCHIVE_MANAGE 或对应打印权限。
/// 原样补打仅返回历史 PDF 下载/预览端点（Agent 只能渲染模板，不能直接打印历史文件）。
/// </para>
/// </summary>
public static class ReprintEndpoints
{
    private const string Prefix = "/api/reprint";

    /// <summary>补打中心入口权限（任一命中即可查看列表/详情/留痕）。</summary>
    private static readonly string[] EntryPermissions =
    [
        PermissionCodes.INCOME_VIEW,
        PermissionCodes.TEMP_VIEW,
        PermissionCodes.ELDERLY_VIEW,
        PermissionCodes.ASSET_VIEW,
        PermissionCodes.CHANGE_VIEW
    ];

    public static IEndpointRouteBuilder MapReprintEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{Prefix}/domains", GetDomainsAsync);
        app.MapGet($"{Prefix}/persons", SearchPersonsAsync);
        app.MapGet($"{Prefix}/persons/{{key}}/domains", GetPersonDomainsAsync);
        app.MapGet($"{Prefix}/records", SearchRecordsAsync);
        app.MapGet($"{Prefix}/records/{{domain}}/{{id}}", GetRecordDetailAsync);
        app.MapGet($"{Prefix}/records/{{domain}}/{{id}}/templates", GetRecordTemplatesAsync);
        app.MapGet($"{Prefix}/records/{{domain}}/{{id}}/history", GetRecordHistoryAsync);
        app.MapGet($"{Prefix}/records/{{domain}}/{{id}}/original", GetRecordOriginalAsync);
        app.MapPost($"{Prefix}/jobs", CreateJobAsync);
        app.MapPost($"{Prefix}/batch", CreateBatchAsync);
        app.MapGet($"{Prefix}/batches/{{batchNo}}", GetBatchProgressAsync);

        // PC Agent（X-NC-Device-Key 设备密钥）：回传 PDF/源文件字节，写打印留痕。
        app.MapPost("/api/agent/jobs/{id}/records", SaveAgentRecordsAsync);

        return app;
    }

    // ───────────────────────────── 域 / 人员 ─────────────────────────────

    /// <summary>GET /api/reprint/domains —— 五域清单与打印权限码。</summary>
    private static async Task<IResult> GetDomainsAsync(
        HttpContext context,
        INewPermissionService permissions,
        ReprintProviderRegistry registry,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequireAnyPermissionAsync(context, permissions, EntryPermissions, ct);
        if (denied is not null) return denied;

        var domains = registry.All
            .Select(provider => new ReprintDomainDto(
                provider.DomainKey,
                provider.DisplayName,
                provider.Mode,
                provider.MonthWindow.ToString(),
                PrintPermissionFor(provider.DomainKey)))
            .ToList();

        return Results.Json(domains);
    }

    /// <summary>
    /// GET /api/reprint/persons?keyword=&amp;year=&amp;month=&amp;domain=
    /// 跨域按身份证聚合人员，返回命中域/最近时间/数量。
    /// </summary>
    private static async Task<IResult> SearchPersonsAsync(
        HttpContext context,
        INewPermissionService permissions,
        ReprintProviderRegistry registry,
        ILoggerService logger,
        CancellationToken ct,
        string? keyword,
        int? year,
        int? month,
        string? domain)
    {
        var denied = await EndpointSecurity.RequireAnyPermissionAsync(context, permissions, EntryPermissions, ct);
        if (denied is not null) return denied;

        if (!TryValidateMonth(year, month, out var monthError))
        {
            return InvalidRequest(context, monthError!);
        }

        var providers = ResolveProviders(registry, domain, out var domainError);
        if (providers is null)
        {
            return InvalidRequest(context, domainError!);
        }

        var records = new List<ReprintRecordItem>();
        Result<List<ReprintRecordItem>>? firstFailure = null;

        if (year is { } y && month is { } m)
        {
            var tasks = providers
                .Select(provider => provider.SearchByMonthAsync(y, m, 500, ct))
                .ToArray();
            await Task.WhenAll(tasks);

            for (var i = 0; i < tasks.Length; i++)
            {
                Collect(await tasks[i], records, ref firstFailure, providers[i].DisplayName, logger);
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim();
                records = records
                    .Where(record => record.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                                     || record.IdCard.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }
        else
        {
            var tasks = providers
                .Select(provider => provider.SearchByPersonAsync(keyword?.Trim(), 20, ct))
                .ToArray();
            await Task.WhenAll(tasks);

            for (var i = 0; i < tasks.Length; i++)
            {
                Collect(await tasks[i], records, ref firstFailure, providers[i].DisplayName, logger);
            }
        }

        if (records.Count == 0 && firstFailure is not null)
        {
            return MapResult(context, firstFailure);
        }

        var names = registry.All.ToDictionary(provider => provider.DomainKey, provider => provider.DisplayName);

        var persons = records
            .GroupBy(KeyOf, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.OrderByDescending(item => item.BusinessTime).ToList();
                var first = items[0];
                var domains = items.Select(item => item.DomainKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return new ReprintPersonDto(
                    group.Key,
                    first.Name,
                    first.IdCard,
                    DataMasker.MaskIdCard(first.IdCard),
                    domains,
                    domains.Where(names.ContainsKey).Select(key => names[key]).ToList(),
                    LatestTimeOf(items),
                    items.Count);
            })
            .OrderByDescending(person => person.LatestTime)
            .ThenBy(person => person.Name, StringComparer.Ordinal)
            .ToList();

        return Results.Json(persons);
    }

    /// <summary>
    /// GET /api/reprint/persons/{key}/domains?year=&amp;month=
    /// key = 身份证（或无名人员 name:姓名）。返回该人在各域的命中记录数。
    /// </summary>
    private static async Task<IResult> GetPersonDomainsAsync(
        string key,
        HttpContext context,
        INewPermissionService permissions,
        ReprintProviderRegistry registry,
        ILoggerService logger,
        CancellationToken ct,
        int? year,
        int? month)
    {
        var denied = await EndpointSecurity.RequireAnyPermissionAsync(context, permissions, EntryPermissions, ct);
        if (denied is not null) return denied;

        if (!TryValidateMonth(year, month, out var monthError))
        {
            return InvalidRequest(context, monthError!);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return InvalidRequest(context, "人员 key 不能为空");
        }

        var isNameKey = key.StartsWith("name:", StringComparison.Ordinal);
        var keyword = isNameKey ? key["name:".Length..] : key;

        var records = new List<ReprintRecordItem>();
        Result<List<ReprintRecordItem>>? firstFailure = null;

        foreach (var provider in registry.All)
        {
            Result<List<ReprintRecordItem>> result;
            if (year is { } y && month is { } m)
            {
                var monthly = await provider.SearchByMonthAsync(y, m, 500, ct);
                result = monthly.IsFailure
                    ? monthly
                    : Result.Success((monthly.Value ?? [])
                        .Where(item => MatchesPerson(item, key, keyword, isNameKey))
                        .ToList());
            }
            else
            {
                var search = await provider.SearchByPersonAsync(keyword, 20, ct);
                result = search.IsFailure
                    ? search
                    : Result.Success((search.Value ?? [])
                        .Where(item => MatchesPerson(item, key, keyword, isNameKey))
                        .ToList());
            }

            Collect(result, records, ref firstFailure, provider.DisplayName, logger);
        }

        if (records.Count == 0 && firstFailure is not null)
        {
            return MapResult(context, firstFailure);
        }

        var domains = records
            .GroupBy(item => item.DomainKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var items = group.ToList();
                var provider = registry.Resolve(group.Key);
                return new ReprintPersonDomainDto(
                    group.Key,
                    provider?.DisplayName ?? group.Key,
                    items.Count,
                    LatestTimeOf(items));
            })
            .OrderByDescending(item => item.LatestTime)
            .ToList();

        return Results.Json(domains);
    }

    // ───────────────────────────── 记录 ─────────────────────────────

    /// <summary>GET /api/reprint/records?domain=&amp;idCard=&amp;year=&amp;month=</summary>
    private static async Task<IResult> SearchRecordsAsync(
        HttpContext context,
        INewPermissionService permissions,
        ReprintProviderRegistry registry,
        CancellationToken ct,
        string? domain,
        string? idCard,
        int? year,
        int? month)
    {
        var denied = await EndpointSecurity.RequireAnyPermissionAsync(context, permissions, EntryPermissions, ct);
        if (denied is not null) return denied;

        var provider = registry.Resolve(domain);
        if (provider is null)
        {
            return InvalidRequest(context, "domain 不受支持");
        }

        if (!TryValidateMonth(year, month, out var monthError))
        {
            return InvalidRequest(context, monthError!);
        }

        Result<List<ReprintRecordItem>> result;
        var isNameKey = idCard?.TrimStart().StartsWith("name:", StringComparison.Ordinal) == true;
        var personKeyword = isNameKey
            ? idCard!.TrimStart()["name:".Length..]
            : idCard?.Trim();

        if (year is { } y && month is { } m)
        {
            var monthly = await provider.SearchByMonthAsync(y, m, 500, ct);
            if (monthly.IsFailure)
            {
                return MapResult(context, monthly);
            }

            var filtered = monthly.Value ?? [];
            if (!string.IsNullOrWhiteSpace(personKeyword))
            {
                filtered = filtered
                    .Where(item => isNameKey
                        ? string.Equals(item.Name, personKeyword, StringComparison.OrdinalIgnoreCase)
                        : string.Equals(item.IdCard, personKeyword, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            result = Result.Success(filtered);
        }
        else if (!string.IsNullOrWhiteSpace(personKeyword))
        {
            result = await provider.SearchByPersonAsync(personKeyword, 20, ct);
        }
        else
        {
            return InvalidRequest(context, "idCard 或 year+month 至少提供一个");
        }

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json((result.Value ?? []).Select(ToRecordDto).ToList());
    }

    /// <summary>GET /api/reprint/records/{domain}/{id}</summary>
    private static async Task<IResult> GetRecordDetailAsync(
        string domain,
        string id,
        HttpContext context,
        INewPermissionService permissions,
        ReprintProviderRegistry registry,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequireAnyPermissionAsync(context, permissions, EntryPermissions, ct);
        if (denied is not null) return denied;

        var provider = registry.Resolve(domain);
        if (provider is null)
        {
            return InvalidRequest(context, "domain 不受支持");
        }

        if (!TryParseId(id, out var businessId))
        {
            return InvalidRequest(context, "记录 ID 无效");
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var result = await provider.GetDetailAsync(businessId, user.Id, ct);
        if (result.IsFailure || result.Value is null)
        {
            return MapResult(context, result);
        }

        var detail = result.Value;
        return Results.Json(new ReprintRecordDetailDto(
            detail.DomainKey,
            detail.BusinessId,
            detail.Name,
            detail.IdCard,
            DataMasker.MaskIdCard(detail.IdCard),
            detail.No,
            detail.Status,
            detail.Classification,
            detail.BusinessTime,
            detail.Extra,
            detail.Mode,
            detail.Fields,
            detail.TableRows));
    }

    /// <summary>GET /api/reprint/records/{domain}/{id}/templates</summary>
    private static async Task<IResult> GetRecordTemplatesAsync(
        string domain,
        string id,
        HttpContext context,
        INewPermissionService permissions,
        ReprintProviderRegistry registry,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequireAnyPermissionAsync(context, permissions, EntryPermissions, ct);
        if (denied is not null) return denied;

        var provider = registry.Resolve(domain);
        if (provider is null)
        {
            return InvalidRequest(context, "domain 不受支持");
        }

        if (!TryParseId(id, out var businessId))
        {
            return InvalidRequest(context, "记录 ID 无效");
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var result = await provider.GetTemplatesAsync(businessId, user.Id, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json((result.Value ?? []).Select(ToTemplateDto).ToList());
    }

    /// <summary>GET /api/reprint/records/{domain}/{id}/history —— 打印留痕（倒序）。</summary>
    private static async Task<IResult> GetRecordHistoryAsync(
        string domain,
        string id,
        HttpContext context,
        INewPermissionService permissions,
        ReprintPrintRecordService printRecords,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequireAnyPermissionAsync(context, permissions, EntryPermissions, ct);
        if (denied is not null) return denied;

        if (!TryParseId(id, out var businessId))
        {
            return InvalidRequest(context, "记录 ID 无效");
        }

        var result = await printRecords.GetHistoryAsync(domain, businessId, 50, ct);
        if (result.IsFailure)
        {
            return MapResult(context, result);
        }

        var history = (result.Value ?? [])
            .Select((item, index) => ToHistoryDto(domain, businessId, item, index))
            .ToList();

        return Results.Json(history);
    }

    /// <summary>
    /// GET /api/reprint/records/{domain}/{id}/original?index=0
    /// 返回历史 PDF（application/pdf，inline）。无 PDF 数据时 404 提示改用模板补打。
    /// </summary>
    private static async Task<IResult> GetRecordOriginalAsync(
        string domain,
        string id,
        HttpContext context,
        INewPermissionService permissions,
        ReprintPrintRecordService printRecords,
        CancellationToken ct,
        int? index)
    {
        var denied = await EndpointSecurity.RequireAnyPermissionAsync(context, permissions, EntryPermissions, ct);
        if (denied is not null) return denied;

        if (!TryParseId(id, out var businessId))
        {
            return InvalidRequest(context, "记录 ID 无效");
        }

        var result = await printRecords.GetOriginalAsync(domain, businessId, index ?? 0, ct);
        if (result.IsFailure || result.Value is null)
        {
            return MapResult(context, result);
        }

        var pdf = result.Value;
        context.Response.Headers.ContentDisposition = "inline";
        return Results.File(pdf.Data, "application/pdf", enableRangeProcessing: true);
    }

    // ───────────────────────────── 补打任务 ─────────────────────────────

    /// <summary>
    /// POST /api/reprint/jobs
    /// body: { domain, id, mode, source, templateIds, copies, isDuplex, printerBindingId?, printerName? }
    /// source=original 返回历史 PDF 预览/下载地址；source=template 一记录一任务（多模板 = 多 part）。
    /// </summary>
    private static async Task<IResult> CreateJobAsync(
        ReprintJobRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ReprintProviderRegistry registry,
        ReprintJobService jobs,
        ReprintPrintRecordService printRecords,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequireAnyPermissionAsync(context, permissions, EntryPermissions, ct);
        if (denied is not null) return denied;

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var provider = registry.Resolve(request.Domain);
        if (provider is null)
        {
            return InvalidRequest(context, "domain 不受支持");
        }

        if (request.Id is not > 0)
        {
            return InvalidRequest(context, "id 必须为正整数");
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var businessId = request.Id.Value;
        var source = string.IsNullOrWhiteSpace(request.Source)
            ? "template"
            : request.Source.Trim().ToLowerInvariant();

        if (source == "original")
        {
            var historyResult = await printRecords.GetHistoryAsync(provider.DomainKey, businessId, 50, ct);
            if (historyResult.IsFailure)
            {
                return MapResult(context, historyResult);
            }

            var history = historyResult.Value ?? [];
            if (history.Count == 0)
            {
                return NotFound(context, "该记录没有打印留痕，无法原样补打");
            }

            var items = history
                .Select((item, index) => ToHistoryDto(provider.DomainKey, businessId, item, index))
                .ToList();

            return Results.Json(new ReprintOriginalResponse(
                provider.DomainKey,
                businessId,
                "original",
                "original",
                BuildOriginalUrl(provider.DomainKey, businessId, 0),
                items));
        }

        if (source != "template")
        {
            return InvalidRequest(context, "source 只能是 original / template");
        }

        var printDenied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PrintPermissionFor(provider.DomainKey), ct);
        if (printDenied is not null) return printDenied;

        var templateIds = request.TemplateIds ?? [];
        if (templateIds.Count == 0)
        {
            return InvalidRequest(context, "templateIds 不能为空");
        }

        // 任务只依赖 BusinessId；申请人文号由 Provider.BuildPrintDataAsync 装配。
        var record = new ReprintRecordItem(
            provider.DomainKey, businessId, string.Empty, string.Empty,
            string.Empty, string.Empty, DateTime.MinValue, string.Empty, string.Empty, string.Empty);

        var result = await jobs.CreateSingleJobAsync(
            provider,
            record,
            templateIds,
            request.Mode ?? "print",
            request.Copies ?? 1,
            request.IsDuplex ?? false,
            request.PrinterBindingId,
            request.PrinterName,
            ReprintJobService.NewBatchNo(),
            user,
            ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>
    /// POST /api/reprint/batch
    /// body: { domain, year, month, templateIds, mode:"print|save", copies, isDuplex, printerBindingId?, printerName? }
    /// 一记录一任务、共享 batch_no；返回 { batchNo, total, jobs, errors }。
    /// </summary>
    private static async Task<IResult> CreateBatchAsync(
        ReprintBatchRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ReprintProviderRegistry registry,
        ReprintJobService jobs,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequireAnyPermissionAsync(context, permissions, EntryPermissions, ct);
        if (denied is not null) return denied;

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var provider = registry.Resolve(request.Domain);
        if (provider is null)
        {
            return InvalidRequest(context, "domain 不受支持");
        }

        var batchDenied = await EndpointSecurity.RequireAnyPermissionAsync(
            context, permissions,
            [PermissionCodes.ARCHIVE_MANAGE, PrintPermissionFor(provider.DomainKey)],
            ct);
        if (batchDenied is not null) return batchDenied;

        var year = request.Year ?? DateTime.Now.Year;
        var month = request.Month ?? 0;
        if (year is not (>= 2000 and <= 2100) || month is < 0 or > 12)
        {
            return InvalidRequest(context, "year/month 无效");
        }

        var templateIds = request.TemplateIds ?? [];
        // templateIds 为空 = 「整份档案」逐户打全部适用模板（Web 批量页默认模式）。

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        // month=0/缺省 = 全部月份：按关键词取最近 500 条（与旧版批量「全部月份」口径一致）。
        var searchResult = month <= 0
            ? await provider.SearchByPersonAsync(request.Keyword?.Trim(), 500, ct)
            : await provider.SearchByMonthAsync(year, month, 500, ct);
        if (searchResult.IsFailure)
        {
            return MapResult(context, searchResult);
        }

        var records = searchResult.Value ?? [];
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            var keyword = request.Keyword.Trim();
            records = records
                .Where(record => record.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                                 || record.IdCard.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (records.Count == 0)
        {
            return InvalidRequest(context, "当前月份没有可补打的记录");
        }

        var result = await jobs.CreateBatchAsync(
            provider,
            records,
            templateIds,
            request.Mode ?? "print",
            request.Copies ?? 1,
            request.IsDuplex ?? false,
            request.PrinterBindingId,
            request.PrinterName,
            user,
            ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>GET /api/reprint/batches/{batchNo} —— 批次进度（总/完成/失败/待处理）。</summary>
    private static async Task<IResult> GetBatchProgressAsync(
        string batchNo,
        HttpContext context,
        INewPermissionService permissions,
        ReprintJobService jobs,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequireAnyPermissionAsync(context, permissions, EntryPermissions, ct);
        if (denied is not null) return denied;

        var result = await jobs.GetBatchProgressAsync(batchNo, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    // ───────────────────────────── Agent 回传 ─────────────────────────────

    /// <summary>
    /// POST /api/agent/jobs/{id}/records
    /// body: { records:[{templateName,fileName,fileType,pdfBase64?,sourceBase64?,sourceType?,pdfSize?,sourceSize?}] }
    /// 仅允许 Agent 设备认证；按 job 的业务域/业务 ID/申请人写 nc_biz_print_records。
    /// </summary>
    private static async Task<IResult> SaveAgentRecordsAsync(
        string id,
        AgentPrintRecordsRequest? request,
        HttpContext context,
        ArchivePrintQueueService queue,
        ReprintPrintRecordService printRecords,
        CancellationToken ct)
    {
        var (device, error) = await AuthorizeDeviceAsync(context, queue, ct);
        if (error is not null) return error;

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

        if (job.Status is not (PrintJobConstants.StatusProcessing or PrintJobConstants.StatusCompleted))
        {
            return Conflict(context, "任务未处于打印中/已完成状态");
        }

        if (!string.IsNullOrWhiteSpace(job.AgentMachine)
            && !string.Equals(job.AgentMachine, device!.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return Forbidden(context, "任务不属于当前设备");
        }

        var records = request?.Records;
        if (records is not { Count: > 0 })
        {
            return InvalidRequest(context, "records 不能为空");
        }

        if (records.Count > 200)
        {
            return InvalidRequest(context, "单次最多回传 200 条留痕");
        }

        var result = await printRecords.SaveAgentRecordsAsync(job, records, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new { written = result.Value });
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

    private static IReadOnlyList<IReprintProvider>? ResolveProviders(
        ReprintProviderRegistry registry,
        string? domain,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(domain))
        {
            return registry.All;
        }

        var provider = registry.Resolve(domain);
        if (provider is null)
        {
            error = "domain 不受支持";
            return null;
        }

        return [provider];
    }

    private static bool TryValidateMonth(int? year, int? month, out string? error)
    {
        error = null;

        // month 缺省/0 = 全部月份（前端「全部」下拉会只带 year 或带 month=0）。
        if (month is null || month <= 0) return true;

        if (year is null || year <= 0)
        {
            error = "month 必须与 year 同时提供";
            return false;
        }

        if (year is not (>= 2000 and <= 2100) || month is not (>= 1 and <= 12))
        {
            error = "year/month 无效";
            return false;
        }

        return true;
    }

    private static void Collect(
        Result<List<ReprintRecordItem>> result,
        List<ReprintRecordItem> records,
        ref Result<List<ReprintRecordItem>>? firstFailure,
        string displayName,
        ILoggerService logger)
    {
        if (result.IsSuccess)
        {
            records.AddRange(result.Value ?? []);
            return;
        }

        firstFailure ??= result;
        logger.Warn($"补打检索失败（{displayName}）: {result.ErrorCode} {result.Message}");
    }

    private static bool MatchesPerson(
        ReprintRecordItem item,
        string key,
        string keyword,
        bool isNameKey)
        => isNameKey
            ? string.Equals(item.Name, keyword, StringComparison.OrdinalIgnoreCase)
            : string.Equals(item.IdCard, keyword, StringComparison.OrdinalIgnoreCase)
              || string.Equals(item.IdCard, key, StringComparison.OrdinalIgnoreCase);

    private static string KeyOf(ReprintRecordItem item)
        => string.IsNullOrWhiteSpace(item.IdCard)
            ? "name:" + item.Name
            : item.IdCard.Trim();

    private static DateTime? LatestTimeOf(IEnumerable<ReprintRecordItem> items)
    {
        var times = items
            .Where(item => item.BusinessTime != DateTime.MinValue)
            .Select(item => (DateTime?)item.BusinessTime)
            .ToList();
        return times.Count == 0 ? null : times.Max();
    }

    private static ReprintRecordDto ToRecordDto(ReprintRecordItem item) => new(
        item.DomainKey,
        item.BusinessId,
        item.Name,
        item.IdCard,
        DataMasker.MaskIdCard(item.IdCard),
        item.No,
        item.Status,
        item.BusinessTime,
        item.Extra,
        item.Classification,
        item.ChainType);

    private static ReprintTemplateDto ToTemplateDto(ReprintTemplateOption option) => new(
        option.TemplateId,
        option.Name,
        option.DocType,
        option.FileType,
        option.Categories,
        option.IsApplicable,
        option.IsDefaultSelected);

    private static ReprintHistoryDto ToHistoryDto(
        string domain,
        long businessId,
        ReprintHistoryItem item,
        int index) => new(
        item.Id,
        item.TemplateName,
        item.PrinterName,
        item.CreatedAt,
        DataMasker.MaskName(item.OperatorName),
        item.Status,
        item.Remark,
        item.BatchNo,
        item.HasPdf,
        item.PdfSize,
        BuildOriginalUrl(domain, businessId, index));

    private static string BuildOriginalUrl(string domain, long businessId, int index)
        => $"{Prefix}/records/{Uri.EscapeDataString(domain)}/{businessId}/original?index={index}";

    private static string PrintPermissionFor(string domain) => domain switch
    {
        ReprintDomains.FamilyApplication => PermissionCodes.PRINT_FAMILYAPPLICATION,
        ReprintDomains.TempRelief => PermissionCodes.PRINT_TEMPRELIEF,
        ReprintDomains.ElderlyBenefits => PermissionCodes.PRINT_ELDERLYBENEFITS,
        ReprintDomains.AssetVerification => PermissionCodes.PRINT_ASSETVERIFICATION,
        ReprintDomains.DynamicManagementRecord => PermissionCodes.PRINT_ECONOMICREVIEW,
        _ => PermissionCodes.ARCHIVE_MANAGE
    };

    private static bool TryParseId(string value, out long id)
        => long.TryParse(value, out id) && id > 0;

    private static IResult MapResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.APPLICATION_NOT_FOUND
            or ErrorCodes.RECORD_NOT_FOUND
            or ErrorCodes.NOT_FOUND
            or ErrorCodes.TEMPLATE_NOT_FOUND)
        {
            return NotFound(context, result.Message ?? "请求的资源不存在");
        }

        if (result.ErrorCode is ErrorCodes.VALIDATION_FAILED
            or ErrorCodes.INVALID_TRANSITION)
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
            "DEVICE_UNAUTHORIZED", " Agent 设备认证失败");

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound, message);

    private static IResult Conflict(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status409Conflict,
            ApiErrorCodes.ValidationError, message);

    private static IResult Forbidden(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status403Forbidden,
            ApiErrorCodes.Forbidden, message);
}
