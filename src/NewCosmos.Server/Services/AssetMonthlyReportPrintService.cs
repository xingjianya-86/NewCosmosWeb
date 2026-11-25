using System.Globalization;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services;

/// <summary>
/// 核对月报表 / 周报 的预览与打印任务。
/// <para>
/// 与导出共用 <see cref="AssetMonthlyReportExportService.QueryAsync"/>，保证预览、打印、导出源文件
/// 使用完全相同的筛选/整户展开/排序数据；目标固定为当前账号默认绑定打印机（前端不再选择）。
/// </para>
/// </summary>
public sealed class AssetMonthlyReportPrintService(
    IDatabaseService database,
    DataScopeFilterBuilder dataScope,
    ArchivePrintQueueService queue,
    PrinterBindingService printers)
{
    private const string TemplateCategory = "AssetVerificationMonthlyReport";

    public async Task<Result<PrintJob>> CreateAsync(
        int userId,
        string operatorName,
        MonthlyReportOutputRequest request,
        CancellationToken ct = default)
    {
        var mode = (request.Mode ?? "preview").Trim().ToLowerInvariant();
        if (mode is not ("preview" or "print"))
        {
            return Result.Failure<PrintJob>(
                ErrorCodes.VALIDATION_FAILED, "mode 只能是 preview 或 print");
        }

        var exportRequest = request.ToExportRequest();
        var exportService = new AssetMonthlyReportExportService(database, dataScope);
        var snapshotResult = await exportService.QueryAsync(userId, exportRequest, ct);
        if (snapshotResult.IsFailure)
        {
            return Result.Failure<PrintJob>(
                snapshotResult.ErrorCode!, snapshotResult.Message!);
        }

        var snapshot = snapshotResult.Value;
        if (snapshot.Rows.Count == 0)
        {
            return Result.Failure<PrintJob>(
                ErrorCodes.VALIDATION_FAILED, "所选周期没有可输出的核查人员");
        }

        var templateResult = await LoadTemplateAsync(ct);
        if (templateResult.IsFailure)
        {
            return Result.Failure<PrintJob>(
                templateResult.ErrorCode!, templateResult.Message!);
        }

        var template = templateResult.Value;
        if (template is null)
        {
            return Result.Failure<PrintJob>(
                ErrorCodes.NOT_FOUND,
                "未找到资产核查月报模板（AssetVerificationMonthlyReport）");
        }

        // 固定默认打印机：不接收前端打印机选择，统一取当前账号默认绑定。
        var targetResult = await printers.ResolveTargetAsync(userId, null, null, ct);
        if (targetResult.IsFailure)
        {
            return Result.Failure<PrintJob>(
                targetResult.ErrorCode!, targetResult.Message!);
        }

        var target = targetResult.Value;
        if (target is null || target.DeviceId <= 0)
        {
            return Result.Failure<PrintJob>(
                ErrorCodes.VALIDATION_FAILED,
                "当前账号未绑定打印机，请先在打印机绑定页生成绑定码并在本机 Agent 完成绑定");
        }

        if (mode == "print" && string.IsNullOrWhiteSpace(target.PrinterName))
        {
            return Result.Failure<PrintJob>(
                ErrorCodes.VALIDATION_FAILED, "默认绑定缺少打印机名称，请重新绑定");
        }

        var fields = AssetMonthlyReportExportService.BuildReportContextValues(
            exportRequest, snapshot.Period);

        var tableRows = snapshot.Rows
            .Select(row => new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldKeys.FAMILY_MEMBER_NAME] = row.ApplicantName ?? string.Empty,
                [FieldKeys.FAMILY_MEMBER_ID_CARD] = row.ApplicantIdCard ?? string.Empty,
                [FieldKeys.FAMILY_MEMBER_ADDRESS] =
                    AssetMonthlyReportExportService.BuildTemplateAddress(row),
                [FieldKeys.FAMILY_MEMBER_RELATION] = row.RelationshipDisplay ?? string.Empty,
                [FieldKeys.HEAD_ID_CARD] = row.HeadIdCard ?? string.Empty,
            })
            .ToList();

        var jobRequest = new PrintJobRequest
        {
            BusinessType = PrintBusinessTypes.AssetVerificationMonthlyReport,
            BusinessId = BuildBusinessId(request),
            Classification = ClassificationConstants.AssetVerification,
            TemplateId = template.Id,
            TemplateName = template.Name,
            ApplicantName = fields.GetValueOrDefault("REPORT_PERIOD") ?? "核对月报表",
            ApplicantIdCard = "-",
            PrinterName = target.PrinterName,
            TargetUserId = target.UserId,
            TargetDeviceId = target.DeviceId,
            TargetPrinter = target.PrinterName,
            Copies = Math.Clamp(request.Copies ?? 1, 1, 99),
            IsDuplex = request.IsDuplex,
            Fields = fields,
            TableRows = tableRows,
            RequestedBy = userId,
            RequestedByName = operatorName,
            JobKind = mode == "preview"
                ? PrintJobConstants.JobKindPreview
                : PrintJobConstants.JobKindTemplate,
        };

        return await queue.EnqueueAsync(jobRequest, ct);
    }

    private async Task<Result<MonthlyReportPrintTemplate?>> LoadTemplateAsync(
        CancellationToken ct)
    {
        const string sql = @"
            SELECT id, name, file_type, config_json::text AS config_json
            FROM nc_biz_templates
            WHERE file_type = 'xlsx'
              AND $1 = ANY(categories)
            ORDER BY sort_order, id
            LIMIT 1";

        var result = await database.QuerySingleAsync<MonthlyReportPrintTemplateRow>(
            sql, ct, TemplateCategory);
        if (result.IsFailure)
        {
            return Result.Failure<MonthlyReportPrintTemplate?>(
                result.ErrorCode!, result.Message!);
        }

        var row = result.Value;
        if (row is null || row.Id <= 0)
        {
            return Result.Success<MonthlyReportPrintTemplate?>(null);
        }

        return Result.Success<MonthlyReportPrintTemplate?>(
            new MonthlyReportPrintTemplate(row.Id, row.Name ?? "通用_经济核查汇总表"));
    }

    private static long? BuildBusinessId(MonthlyReportOutputRequest request)
    {
        if (request.AllMonths)
        {
            return null;
        }

        if (request.WeekStart is { } weekStart)
        {
            return long.Parse(
                $"{ISOWeek.GetYear(weekStart)}{ISOWeek.GetWeekOfYear(weekStart):D2}",
                CultureInfo.InvariantCulture);
        }

        var year = request.Year ?? DateTime.Now.Year;
        var month = request.Month ?? DateTime.Now.Month;
        return long.Parse($"{year}{month:D2}", CultureInfo.InvariantCulture);
    }
}

/// <summary>月报/周报预览或打印请求。</summary>
public sealed class MonthlyReportOutputRequest
{
    /// <summary>preview | print。</summary>
    public string? Mode { get; init; }

    public int? Year { get; init; }

    public int? Month { get; init; }

    public DateTime? WeekStart { get; init; }

    public DateTime? WeekEnd { get; init; }

    public bool AllMonths { get; init; }

    public string? Status { get; init; }

    /// <summary>true = 仅输出状态命中人员；false = 展开同户全部成员。</summary>
    public bool OnlyCurrent { get; init; }

    public int? Copies { get; init; }

    public bool IsDuplex { get; init; }

    public AssetMonthlyReportExportRequest ToExportRequest()
        => new()
        {
            Year = Year,
            Month = Month,
            WeekStart = WeekStart,
            WeekEnd = WeekEnd,
            Status = Status,
            OnlyCurrent = OnlyCurrent,
            AllMonths = AllMonths,
        };
}

internal sealed class MonthlyReportPrintTemplateRow
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? FileType { get; set; }
    public string? ConfigJson { get; set; }
}

internal sealed record MonthlyReportPrintTemplate(long Id, string Name);
