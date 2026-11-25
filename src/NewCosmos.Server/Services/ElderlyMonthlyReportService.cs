using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ElderlyBenefits;

namespace NewCosmos.Server.Services;

/// <summary>
/// 高龄津贴月报表：自然月统计与批量打印任务。
/// <para>
/// 统计口径与旧版月报完全一致：新增 = <c>GetByMonthAsync(..., excludeImported: true)</c>，
/// 停止 = <c>GetByMonthAsync(..., excludeImported: false)</c>，满 90 周岁 = <c>GetAge90ReportRowsAsync</c>。
/// 批量任务把每个明细表页/逐人页装配为 <see cref="PrintBatchDefinition"/> 入队，
/// Server 不渲染文档（模板字节与字段由 Agent 领取后执行）。
/// </para>
/// </summary>
public sealed class ElderlyMonthlyReportService(
    IElderlyApplicationService applications,
    ArchivePrintQueueService queue,
    PrinterBindingService printers,
    IDatabaseService database,
    ILoggerService logger)
{
    /// <summary>批量任务分类（PrintBatchDefinition.Category）。</summary>
    public const string BatchCategory = "高龄月报";

    /// <summary>新增/停止明细表每页人数（与旧版一致）。</summary>
    private const int DetailRowsPerPage = 13;

    private const string NewDetailTemplateName = "普惠高龄新增明细表";
    private const string StopDetailTemplateName = "普惠高龄停止明细表";
    private const string Age90TemplateName = "普惠高龄津贴调整备案表";

    private static readonly string[] Modes = ["preview", "print", "save", "open-folder"];

    // ───────────────────────────── 统计 ─────────────────────────────

    /// <summary>
    /// 自然月统计：逐项返回 9 个明细表（4 类新增 + 4 类停止 + 满90周岁）的人数。
    /// 任一查询失败立即上抛（端点返回 500），绝不静默按 0 处理。
    /// </summary>
    public async Task<Result<ElderlyMonthlyReportStatisticsDto>> GetStatisticsAsync(
        int year,
        int month,
        CancellationToken ct = default)
    {
        var forms = new List<ElderlyMonthlyReportFormDto>();

        foreach (var (key, label) in ElderlyBenefitConstants.GetMonthlyDetailFormOptions())
        {
            int count;
            if (ElderlyBenefitConstants.IsAge90Form(key))
            {
                var rowsResult = await applications.GetAge90ReportRowsAsync(year, month, ct);
                if (rowsResult.IsFailure)
                {
                    return Result.Failure<ElderlyMonthlyReportStatisticsDto>(
                        rowsResult.ErrorCode!, rowsResult.Message!);
                }

                count = rowsResult.Value?.Count ?? 0;
            }
            else
            {
                var isStop = ElderlyBenefitConstants.IsStopForm(key);
                var category = ElderlyBenefitConstants.GetCategoryCodeFromFormKey(key);
                var recordsResult = await applications.GetByMonthAsync(
                    year, month, isStop, category, excludeImported: !isStop, ct);
                if (recordsResult.IsFailure)
                {
                    return Result.Failure<ElderlyMonthlyReportStatisticsDto>(
                        recordsResult.ErrorCode!, recordsResult.Message!);
                }

                count = recordsResult.Value?.Count ?? 0;
            }

            forms.Add(new ElderlyMonthlyReportFormDto(key, label, count, count > 0));
        }

        // 合计口径与旧版一致：新增/停止按「全部类别」各查一次（不受 form 勾选影响）。
        var newTotalResult = await applications.GetByMonthAsync(
            year, month, false, categoryCode: null, excludeImported: true, ct);
        if (newTotalResult.IsFailure)
        {
            return Result.Failure<ElderlyMonthlyReportStatisticsDto>(
                newTotalResult.ErrorCode!, newTotalResult.Message!);
        }

        var stopTotalResult = await applications.GetByMonthAsync(
            year, month, true, categoryCode: null, excludeImported: false, ct);
        if (stopTotalResult.IsFailure)
        {
            return Result.Failure<ElderlyMonthlyReportStatisticsDto>(
                stopTotalResult.ErrorCode!, stopTotalResult.Message!);
        }

        var newCount = newTotalResult.Value?.Count ?? 0;
        var stopCount = stopTotalResult.Value?.Count ?? 0;

        var days = DateTime.DaysInMonth(year, month);
        return Result.Success(new ElderlyMonthlyReportStatisticsDto(
            year,
            month,
            $"{year:D4}-{month:D2}-01 ~ {year:D4}-{month:D2}-{days:D2}",
            $"{year}年{month}月普惠高龄月度明细（自然月）",
            newCount,
            stopCount,
            $"新增 {newCount} 人，停止 {stopCount} 人",
            forms));
    }

    // ───────────────────────────── 批量任务 ─────────────────────────────

    /// <summary>
    /// 构造并写入批量打印任务。目标固定为当前账号默认绑定打印机。
    /// </summary>
    public async Task<Result<PrintJob>> CreateBatchAsync(
        int userId,
        string operatorName,
        int? organizationId,
        ElderlyMonthlyReportJobRequest request,
        CancellationToken ct = default)
    {
        if (request.Year is not (>= 1900 and <= 9999))
        {
            return Result.Failure<PrintJob>(ErrorCodes.VALIDATION_FAILED, "年份参数无效");
        }

        if (request.Month is not (>= 1 and <= 12))
        {
            return Result.Failure<PrintJob>(ErrorCodes.VALIDATION_FAILED, "月份参数应为 1-12");
        }

        var year = request.Year.Value;
        var month = request.Month.Value;

        var mode = (request.Mode ?? "preview").Trim().ToLowerInvariant();
        if (!Modes.Contains(mode, StringComparer.Ordinal))
        {
            return Result.Failure<PrintJob>(
                ErrorCodes.VALIDATION_FAILED, "mode 只能是 preview / print / save / open-folder");
        }

        var isOpenFolder = string.Equals(mode, "open-folder", StringComparison.Ordinal);
        var formKeys = NormalizeFormKeys(request.FormKeys);
        if (!isOpenFolder)
        {
            if (formKeys.Count == 0)
            {
                return Result.Failure<PrintJob>(
                    ErrorCodes.VALIDATION_FAILED, "请至少选择一个明细表");
            }

            var validKeys = ElderlyBenefitConstants.GetMonthlyDetailFormOptions()
                .Select(option => option.Key)
                .ToHashSet(StringComparer.Ordinal);
            var unknown = formKeys.Where(key => !validKeys.Contains(key)).ToList();
            if (unknown.Count > 0)
            {
                return Result.Failure<PrintJob>(
                    ErrorCodes.VALIDATION_FAILED, $"未知明细表：{string.Join("、", unknown)}");
            }
        }

        // 目标固定当前账号默认绑定（前端不再选择打印机）。
        var targetResult = await printers.ResolveTargetAsync(userId, null, null, ct);
        if (targetResult.IsFailure)
        {
            return Result.Failure<PrintJob>(targetResult.ErrorCode!, targetResult.Message!);
        }

        var target = targetResult.Value;
        if (target is null || target.DeviceId <= 0)
        {
            return Result.Failure<PrintJob>(
                ErrorCodes.VALIDATION_FAILED,
                "当前账号未绑定打印机，请先在打印机绑定页生成绑定码并在本机 Agent 完成绑定");
        }

        if (string.IsNullOrWhiteSpace(target.PrinterName))
        {
            return Result.Failure<PrintJob>(
                ErrorCodes.VALIDATION_FAILED, "默认绑定缺少打印机名称，请重新绑定");
        }

        var period = $"{year}{month:D2}";
        var batch = new PrintBatchDefinition
        {
            Mode = mode,
            Category = BatchCategory,
            Period = period,
        };

        if (!isOpenFolder)
        {
            var buildResult = await BuildPartsAsync(
                batch, year, month, formKeys, organizationId, ct);
            if (buildResult.IsFailure)
            {
                return Result.Failure<PrintJob>(buildResult.ErrorCode!, buildResult.Message!);
            }

            if (batch.Parts.Count == 0)
            {
                return Result.Failure<PrintJob>(
                    ErrorCodes.VALIDATION_FAILED, "所选明细表本月份均无数据");
            }
        }

        var jobRequest = new PrintJobRequest
        {
            BusinessType = PrintBusinessTypes.ElderlyBenefits,
            BusinessId = year * 100L + month,
            Classification = BatchCategory,
            TemplateId = null,
            TemplateName = $"{BatchCategory} {year}年{month}月",
            PrinterName = target.PrinterName,
            TargetUserId = target.UserId,
            TargetDeviceId = target.DeviceId,
            TargetPrinter = target.PrinterName,
            Copies = Math.Clamp(request.Copies ?? 1, 1, 99),
            IsDuplex = request.IsDuplex ?? false,
            RequestedBy = userId,
            RequestedByName = operatorName,
            JobKind = PrintJobConstants.JobKindTemplateBatch,
            Batch = batch,
        };

        return await queue.EnqueueAsync(jobRequest, ct);
    }

    /// <summary>
    /// 按旧版 BuildElderlyMonthlyPagesAsync / BuildAge90FormDataAsync 口径装配逐页 part。
    /// 空数据的明细表直接跳过；全部为空由调用方统一报 400。
    /// </summary>
    private async Task<Result> BuildPartsAsync(
        PrintBatchDefinition batch,
        int year,
        int month,
        IReadOnlyList<string> formKeys,
        int? organizationId,
        CancellationToken ct)
    {
        var fillUnit = await ResolveReportUnitAsync(organizationId, ct);
        var templateCache = new Dictionary<string, ReportTemplateRow>(StringComparer.Ordinal);

        foreach (var formKey in formKeys)
        {
            if (ElderlyBenefitConstants.IsAge90Form(formKey))
            {
                var rowsResult = await applications.GetAge90ReportRowsAsync(year, month, ct);
                if (rowsResult.IsFailure)
                {
                    return Result.Failure(rowsResult.ErrorCode!, rowsResult.Message!);
                }

                var rows = rowsResult.Value ?? [];
                if (rows.Count == 0) continue;

                var age90TemplateResult = await ResolveTemplateAsync(
                    Age90TemplateName, templateCache, ct);
                if (age90TemplateResult.IsFailure)
                {
                    return age90TemplateResult;
                }

                var age90Template = age90TemplateResult.Value;
                var monthText = $"{year}年{month}月";
                var acceptDate = DateTime.Today.ToString("yyyy年M月d日");
                for (var index = 0; index < rows.Count; index++)
                {
                    batch.Parts.Add(new PrintBatchPart
                    {
                        TemplateId = age90Template.Id,
                        TemplateName = age90Template.Name,
                        FileName = $"{year}{month:D2}_满90周岁调整备案表_{index + 1}.xlsx",
                        Fields = ElderlyPrintDataBuilder.BuildAge90AdjustFields(
                            rows[index], acceptDate, monthText),
                    });
                }

                continue;
            }

            var isStop = ElderlyBenefitConstants.IsStopForm(formKey);
            var category = ElderlyBenefitConstants.GetCategoryCodeFromFormKey(formKey);
            var recordsResult = await applications.GetByMonthAsync(
                year, month, isStop, category, excludeImported: !isStop, ct);
            if (recordsResult.IsFailure)
            {
                return Result.Failure(recordsResult.ErrorCode!, recordsResult.Message!);
            }

            var records = recordsResult.Value ?? [];
            if (records.Count == 0) continue;

            // 停止明细表：实际发放列读取历史档案发放金额；失败降级为空（回退计发金额）但留警告。
            Dictionary<string, decimal>? historyAmounts = null;
            if (isStop)
            {
                var historyResult = await applications.GetHistorySubsidyAmountsAsync(
                    records.Select(record => record.IdCard), ct);
                if (historyResult.IsSuccess)
                {
                    historyAmounts = historyResult.Value;
                }
                else
                {
                    logger.Warn(
                        $"高龄月报停止明细表历史发放金额读取失败，实际发放列回退计发金额: {historyResult.ErrorCode} {historyResult.Message}");
                }
            }

            var templateName = isStop ? StopDetailTemplateName : NewDetailTemplateName;
            var templateResult = await ResolveTemplateAsync(templateName, templateCache, ct);
            if (templateResult.IsFailure)
            {
                return templateResult;
            }

            var template = templateResult.Value;
            // 标题月份 = 周期结束日的次月（与月报口径一致；12 月跨年）。
            var titleYear = month == 12 ? year + 1 : year;
            var titleMonth = month == 12 ? 1 : month + 1;
            var title =
                $"{titleYear}年{titleMonth}月普惠高龄{ElderlyBenefitConstants.GetCategoryName(category)}" +
                $"{(isStop ? "停止" : "新增")}人员明细表";

            var pageCount = (records.Count + DetailRowsPerPage - 1) / DetailRowsPerPage;
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var pageRecords = records
                    .Skip(pageIndex * DetailRowsPerPage)
                    .Take(DetailRowsPerPage)
                    .ToList();

                var fields = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [FieldKeys.REPORT_TITLE] = title,
                    [FieldKeys.REPORT_UNIT] = fillUnit,
                    [FieldKeys.REPORT_DATE] = $"{year}年{month}月",
                };
                foreach (var (key, value) in ElderlyPrintDataBuilder.BuildDetailFields(
                             pageRecords, isStop, DetailRowsPerPage, historyAmounts))
                {
                    fields[key] = value;
                }

                var pageSuffix = pageCount > 1 ? $"_{pageIndex + 1}" : string.Empty;
                batch.Parts.Add(new PrintBatchPart
                {
                    TemplateId = template.Id,
                    TemplateName = template.Name,
                    FileName =
                        $"{year}{month:D2}_{category}{(isStop ? "停止" : "新增")}明细表{pageSuffix}.xlsx",
                    Fields = fields,
                });
            }
        }

        return Result.Success();
    }

    /// <summary>按模板名称解析（同一批任务内缓存，避免逐页重复查询）。</summary>
    private async Task<Result<ReportTemplateRow>> ResolveTemplateAsync(
        string name,
        Dictionary<string, ReportTemplateRow> cache,
        CancellationToken ct)
    {
        if (cache.TryGetValue(name, out var cached))
        {
            return Result.Success(cached);
        }

        var result = await database.QuerySingleAsync<ReportTemplateRow?>(
            @"SELECT id, name, file_type
              FROM nc_biz_templates
              WHERE name = $1
              ORDER BY id
              LIMIT 1",
            ct, name);
        if (result.IsFailure)
        {
            return Result.Failure<ReportTemplateRow>(result.ErrorCode!, result.Message!);
        }

        if (result.Value is not { Id: > 0 } template)
        {
            return Result.Failure<ReportTemplateRow>(
                ErrorCodes.NOT_FOUND, $"模板不存在：{name}");
        }

        cache[name] = template;
        return Result.Success(template);
    }

    /// <summary>填报单位 = 当前用户组织名；组织缺失/查询失败回退「全部」。</summary>
    private async Task<string> ResolveReportUnitAsync(int? organizationId, CancellationToken ct)
    {
        if (organizationId is not > 0)
        {
            return "全部";
        }

        var result = await database.QuerySingleAsync<string?>(
            "SELECT name FROM nc_sys_organizations WHERE id = $1",
            ct, organizationId.Value);
        if (result.IsFailure)
        {
            logger.Warn(
                $"高龄月报填报单位查询失败，回退「全部」: OrgId={organizationId} {result.ErrorCode} {result.Message}");
            return "全部";
        }

        return string.IsNullOrWhiteSpace(result.Value) ? "全部" : result.Value.Trim();
    }

    private static List<string> NormalizeFormKeys(IReadOnlyList<string>? formKeys)
        => (formKeys ?? [])
            .Select(key => key?.Trim() ?? string.Empty)
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private sealed class ReportTemplateRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
    }
}
