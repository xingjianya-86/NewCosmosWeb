using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Server.Services.Reprint;

/// <summary>
/// 补打域公共能力：按月业务窗口解析、模板分类查询与适用性标记。
/// <para>
/// 月份窗口与旧版统一补打中心一致：各域在「自然月查询」之上，再按自身业务时间轴
/// （B 线 / A 线 / C 线）过滤。域 Provider 只实现 <see cref="SearchByNaturalMonthAsync"/>。
/// </para>
/// </summary>
public abstract class ReprintProviderBase(
    IDatabaseService database,
    IBusinessTimelineService timeline,
    ILoggerService logger) : IReprintProvider
{
    protected IDatabaseService Database { get; } = database;

    protected IBusinessTimelineService Timeline { get; } = timeline;

    protected ILoggerService Logger { get; } = logger;

    public abstract string DomainKey { get; }

    public abstract string DisplayName { get; }

    public abstract string Mode { get; }

    public abstract ReprintMonthWindow MonthWindow { get; }

    public abstract Task<Result<List<ReprintRecordItem>>> SearchByPersonAsync(
        string? keyword,
        int limit,
        CancellationToken ct = default);

    public abstract Task<Result<ReprintRecordDetail>> GetDetailAsync(
        long id,
        int userId,
        CancellationToken ct = default);

    public abstract Task<Result<List<ReprintTemplateOption>>> GetTemplatesAsync(
        long id,
        int userId,
        CancellationToken ct = default);

    public abstract Task<Result<ReprintPrintData>> BuildPrintDataAsync(
        long id,
        int userId,
        CancellationToken ct = default);

    /// <summary>自然月查询（各域既有服务单月口径）。</summary>
    protected abstract Task<Result<List<ReprintRecordItem>>> SearchByNaturalMonthAsync(
        int year,
        int month,
        int limit,
        CancellationToken ct);

    public async Task<Result<List<ReprintRecordItem>>> SearchByMonthAsync(
        int year,
        int month,
        int limit,
        CancellationToken ct = default)
    {
        if (limit <= 0) limit = 500;

        var (from, to) = await ResolveWindowAsync(year, month);
        var all = new List<ReprintRecordItem>();
        Result<List<ReprintRecordItem>>? lastFailure = null;

        foreach (var (y, m) in SpannedMonths(from, to))
        {
            var result = await SearchByNaturalMonthAsync(y, m, limit, ct);
            if (result.IsFailure)
            {
                lastFailure = result;
                continue;
            }

            all.AddRange(result.Value ?? []);
        }

        if (all.Count == 0 && lastFailure is not null)
        {
            return Result.Failure<List<ReprintRecordItem>>(
                lastFailure.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                lastFailure.Message ?? "按月查询失败");
        }

        var hiExclusive = to.Date.AddDays(1);
        var filtered = all
            .Where(x => x.BusinessTime == DateTime.MinValue
                        || (x.BusinessTime.Date >= from.Date && x.BusinessTime.Date < hiExclusive))
            .GroupBy(x => x.BusinessId)
            .Select(g => g.First())
            .OrderByDescending(x => x.BusinessTime)
            .Take(limit)
            .ToList();

        return Result.Success(filtered);
    }

    /// <summary>月份窗口区间（含端点）。</summary>
    protected async Task<(DateTime From, DateTime To)> ResolveWindowAsync(int year, int month)
    {
        var monthStart = new DateTime(year, month, 1);

        switch (MonthWindow)
        {
            case ReprintMonthWindow.BusinessProcess:
            {
                var result = await Timeline.CalculateTimelineAsync(year, month, TimelineType.BusinessProcess);
                return (result.CycleStartDate.Date, result.CycleEndDate.Date);
            }

            case ReprintMonthWindow.EconomicReview:
            {
                var result = await Timeline.CalculateTimelineAsync(year, month, TimelineType.EconomicReview);
                return (result.CycleStartDate.Date, result.CycleEndDate.Date);
            }

            case ReprintMonthWindow.TempReliefFull:
            {
                // C 线批次窗口 = (上月验收日, 本月验收日]
                var current = await Timeline.CalculateTempReliefAsync(year, month);
                var prevMonth = month == 1 ? 12 : month - 1;
                var prevYear = month == 1 ? year - 1 : year;
                var prev = await Timeline.CalculateTempReliefAsync(prevYear, prevMonth);
                return (prev.AuditDate.Date.AddDays(1), current.AuditDate.Date);
            }

            default:
                return (monthStart, monthStart.AddMonths(1).AddDays(-1));
        }
    }

    /// <summary>窗口跨越的自然月。</summary>
    protected static IEnumerable<(int Year, int Month)> SpannedMonths(DateTime from, DateTime to)
    {
        var current = new DateTime(from.Year, from.Month, 1);
        var last = new DateTime(to.Year, to.Month, 1);
        while (current <= last)
        {
            yield return (current.Year, current.Month);
            current = current.AddMonths(1);
        }
    }

    /// <summary>
    /// 域模板列表：分类取 <see cref="ArchiveCategoryResolver.GetReprintDomainCategories"/>；
    /// 记录有分类时再按 <see cref="ArchiveCategoryResolver.GetRecordCategories"/> 标记适用性。
    /// </summary>
    protected async Task<Result<List<ReprintTemplateOption>>> BuildTemplatesAsync(
        string? classification,
        string? fixedTemplateName,
        CancellationToken ct)
    {
        var scope = ArchiveCategoryResolver.GetReprintDomainCategories(DomainKey);

        string sql;
        object?[] parameters;
        if (!string.IsNullOrWhiteSpace(fixedTemplateName))
        {
            sql = @"SELECT id, name, doc_type, file_type, sort_order, categories
                    FROM nc_biz_templates
                    WHERE name = $1
                    ORDER BY sort_order ASC, name ASC";
            parameters = [fixedTemplateName.Trim()];
        }
        else if (scope is { Length: > 0 })
        {
            sql = @"SELECT id, name, doc_type, file_type, sort_order, categories
                    FROM nc_biz_templates
                    WHERE categories && string_to_array($1, ',')
                    ORDER BY sort_order ASC, name ASC";
            parameters = [string.Join(",", scope)];
        }
        else
        {
            sql = @"SELECT id, name, doc_type, file_type, sort_order, categories
                    FROM nc_biz_templates
                    ORDER BY sort_order ASC, name ASC";
            parameters = [];
        }

        var rowsResult = await Database.QueryAsync<ReprintTemplateRow>(sql, ct, parameters);
        if (rowsResult.IsFailure)
        {
            return Result.Failure<List<ReprintTemplateOption>>(
                rowsResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                rowsResult.Message ?? "加载模板失败");
        }

        var recordCategories = string.IsNullOrWhiteSpace(classification)
            ? null
            : ArchiveCategoryResolver.GetRecordCategories(DomainKey, classification);

        var options = (rowsResult.Value ?? [])
            .Select(row =>
            {
                var categories = row.Categories ?? [];
                var applicable = !string.IsNullOrWhiteSpace(fixedTemplateName)
                                 || recordCategories is null
                                 || categories.Length == 0
                                 || categories.Intersect(recordCategories, StringComparer.Ordinal).Any();

                return new ReprintTemplateOption(
                    row.Id,
                    row.Name ?? string.Empty,
                    row.DocType ?? string.Empty,
                    NormalizeFileType(row.FileType),
                    categories,
                    applicable,
                    applicable);
            })
            .ToList();

        if (options.Count == 0)
        {
            Logger.Warn($"补打模板为空: Domain={DomainKey}, Fixed={fixedTemplateName ?? "-"}");
        }

        return Result.Success(options);
    }

    /// <summary>关键词转 SQL LIKE 模式；空关键词返回 null。</summary>
    protected static string? ToLikePattern(string? keyword)
        => string.IsNullOrWhiteSpace(keyword) ? null : $"%{keyword.Trim()}%";

    protected static string NormalizeFileType(string? fileType)
    {
        try
        {
            return TemplateFileTypes.Normalize(fileType);
        }
        catch (NotSupportedException)
        {
            return string.IsNullOrWhiteSpace(fileType) ? "docx" : fileType.Trim().ToLowerInvariant();
        }
    }

    /// <summary>模板行（含 categories，用于适用性判断）。</summary>
    protected sealed class ReprintTemplateRow
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public string? DocType { get; set; }
        public string? FileType { get; set; }
        public int SortOrder { get; set; }
        public string[]? Categories { get; set; }
    }
}
