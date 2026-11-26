using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Server.Services.Reprint;

/// <summary>
/// 资产核查域补打 Provider（对齐旧版 <c>AssetVerificationReprintProvider</c>）：
/// 成员记录归位户主、按月创建时间检索；字段装配复用 <see cref="AssetCheckPrintDataService"/>。
/// </summary>
public sealed class AssetVerificationReprintProvider(
    IAssetVerificationService verifications,
    AssetCheckPrintDataService printData,
    IDatabaseService database,
    IBusinessTimelineService timeline,
    ILoggerService logger)
    : ReprintProviderBase(database, timeline, logger)
{
    public override string DomainKey => ReprintDomains.AssetVerification;

    public override string DisplayName => "资产核查";

    public override string Mode => "AssetVerification";

    public override ReprintMonthWindow MonthWindow => ReprintMonthWindow.EconomicReview;

    public override async Task<Result<List<ReprintRecordItem>>> SearchByPersonAsync(
        string? keyword,
        int limit,
        CancellationToken ct = default)
    {
        if (limit <= 0) limit = 20;

        var result = await verifications.GetTasksPagedAsync(
            1, limit, status: null, keyword: keyword?.Trim()!, dateFrom: null, dateTo: null, ct: ct);
        return result.IsFailure
            ? Result.Failure<List<ReprintRecordItem>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "核查记录搜索失败")
            : Result.Success(NormalizeToHeadEntries(result.Value?.Items ?? []).Take(limit).ToList());
    }

    protected override async Task<Result<List<ReprintRecordItem>>> SearchByNaturalMonthAsync(
        int year,
        int month,
        int limit,
        CancellationToken ct)
    {
        var dateFrom = new DateTime(year, month, 1);
        var dateTo = dateFrom.AddMonths(1);

        var result = await verifications.GetTasksPagedAsync(
            1, limit, status: null, keyword: null, dateFrom: dateFrom, dateTo: dateTo, ct: ct);
        return result.IsFailure
            ? Result.Failure<List<ReprintRecordItem>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "核查记录按月查询失败")
            : Result.Success(NormalizeToHeadEntries(result.Value?.Items ?? []).Take(limit).ToList());
    }

    public override async Task<Result<ReprintRecordDetail>> GetDetailAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        var recordResult = await verifications.GetTaskByIdAsync(id, ct);
        if (recordResult.IsFailure || recordResult.Value is null)
        {
            return Result.Failure<ReprintRecordDetail>(
                recordResult.ErrorCode ?? ErrorCodes.RECORD_NOT_FOUND,
                recordResult.Message ?? "核查记录不存在");
        }

        var payloadResult = await printData.BuildAsync(id, ct);
        if (payloadResult.IsFailure || payloadResult.Value is null)
        {
            return Result.Failure<ReprintRecordDetail>(
                payloadResult.ErrorCode ?? ErrorCodes.RECORD_NOT_FOUND,
                payloadResult.Message ?? "核查打印数据装配失败");
        }

        var record = recordResult.Value;
        var payload = payloadResult.Value;
        return Result.Success(new ReprintRecordDetail(
            DomainKey,
            id,
            record.ArchiveName ?? string.Empty,
            record.ArchiveIdCard ?? string.Empty,
            record.BatchId ?? string.Empty,
            record.Status ?? string.Empty,
            ClassificationConstants.AssetVerification,
            record.CreatedAt,
            BuildExtra(record, 0),
            Mode,
            payload.Fields,
            payload.TableRows));
    }

    public override async Task<Result<List<ReprintTemplateOption>>> GetTemplatesAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        var recordResult = await verifications.GetTaskByIdAsync(id, ct);
        if (recordResult.IsFailure || recordResult.Value is null)
        {
            return Result.Failure<List<ReprintTemplateOption>>(
                recordResult.ErrorCode ?? ErrorCodes.RECORD_NOT_FOUND,
                recordResult.Message ?? "核查记录不存在");
        }

        return await BuildTemplatesAsync(ClassificationConstants.AssetVerification, null, ct);
    }

    public override async Task<Result<ReprintPrintData>> BuildPrintDataAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        var payloadResult = await printData.BuildAsync(id, ct);
        if (payloadResult.IsFailure || payloadResult.Value is null)
        {
            return Result.Failure<ReprintPrintData>(
                payloadResult.ErrorCode ?? ErrorCodes.RECORD_NOT_FOUND,
                payloadResult.Message ?? "核查打印数据装配失败");
        }

        var payload = payloadResult.Value;
        var fields = payload.Fields;
        return Result.Success(new ReprintPrintData(
            DomainKey,
            id,
            fields.GetValueOrDefault(FieldKeys.APPLICANT_NAME) ?? string.Empty,
            fields.GetValueOrDefault(FieldKeys.APPLICANT_ID_CARD) ?? string.Empty,
            ClassificationConstants.AssetVerification,
            ClassificationConstants.AssetVerification,
            null,
            fields,
            payload.TableRows));
    }

    /// <summary>
    /// 成员 → 户主归位：核查表按人一条记录，名单每户只出一条。
    /// 优先取 is_head=true，户主不在集内时取组内最新兜底（与旧版一致）。
    /// </summary>
    private static List<ReprintRecordItem> NormalizeToHeadEntries(IEnumerable<AssetVerificationTask> tasks)
    {
        var ordered = tasks.OrderByDescending(task => task.CreatedAt).ToList();
        return ordered
            .GroupBy(task => string.IsNullOrWhiteSpace(task.HeadIdCard)
                ? "self:" + task.ArchiveIdCard
                : task.HeadIdCard)
            .Select(group =>
            {
                var head = group.FirstOrDefault(task => task.IsHead) ?? group.First();
                return new ReprintRecordItem(
                    ReprintDomains.AssetVerification,
                    head.Id,
                    head.ArchiveName ?? string.Empty,
                    head.ArchiveIdCard ?? string.Empty,
                    head.BatchId ?? string.Empty,
                    AssetCheckStatusConstants.GetDescription(head.Status),
                    head.CreatedAt,
                    BuildExtra(head, group.Count()),
                    ClassificationConstants.AssetVerification,
                    string.Empty);
            })
            .OrderByDescending(item => item.BusinessTime)
            .ToList();
    }

    private static string BuildExtra(AssetVerificationTask record, int familyCount)
    {
        var status = AssetCheckStatusConstants.GetDescription(record.Status);
        var suffix = familyCount > 0 ? $" · 该户{familyCount}人核查" : string.Empty;
        return $"{record.VerificationYear}年{record.VerificationMonth}月 · {status}{suffix}";
    }
}
