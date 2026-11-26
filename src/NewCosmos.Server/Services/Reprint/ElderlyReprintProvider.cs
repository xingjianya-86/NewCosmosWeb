using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Server.Services.Reprint;

/// <summary>
/// 普惠高龄域补打 Provider（对齐旧版 <c>ElderlyReprintProvider</c>）：
/// 已确认 + 已停发双状态合并搜索；新增方向按月排除导入补录档，停发方向口径与停发明细一致。
/// </summary>
public sealed class ElderlyReprintProvider(
    IElderlyApplicationService elderly,
    IDatabaseService database,
    IBusinessTimelineService timeline,
    ILoggerService logger)
    : ReprintProviderBase(database, timeline, logger)
{
    public override string DomainKey => ReprintDomains.ElderlyBenefits;

    public override string DisplayName => "普惠高龄";

    public override string Mode => "ArchiveSet";

    public override ReprintMonthWindow MonthWindow => ReprintMonthWindow.NaturalMonth;

    public override async Task<Result<List<ReprintRecordItem>>> SearchByPersonAsync(
        string? keyword,
        int limit,
        CancellationToken ct = default)
    {
        if (limit <= 0) limit = 20;
        var kw = keyword?.Trim();

        var confirmed = await elderly.GetPagedAsync(kw, ElderlyBenefitConstants.StatusConfirmed, 1, limit, ct);
        var stopped = await elderly.GetPagedAsync(kw, ElderlyBenefitConstants.StatusStopped, 1, limit, ct);

        if (confirmed.IsFailure && stopped.IsFailure)
        {
            return Result.Failure<List<ReprintRecordItem>>(
                confirmed.ErrorCode ?? stopped.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                confirmed.Message ?? stopped.Message ?? "普惠高龄搜索失败");
        }

        var merged = new List<ElderlyApplication>();
        if (confirmed.HasValue) merged.AddRange(confirmed.Value.Items);
        if (stopped.HasValue) merged.AddRange(stopped.Value.Items);

        return Result.Success(merged
            .GroupBy(app => app.Id)
            .Select(group => group.First())
            .OrderByDescending(app => BusinessTimeOf(app))
            .Take(limit)
            .Select(ToItem)
            .ToList());
    }

    protected override async Task<Result<List<ReprintRecordItem>>> SearchByNaturalMonthAsync(
        int year,
        int month,
        int limit,
        CancellationToken ct)
    {
        // 新增：排除导入补录档（与月报新增明细同源）；停发：不排除（与停发明细同源）。
        var confirmed = await elderly.GetByMonthAsync(
            year, month, isStop: false, categoryCode: null, excludeImported: true, ct: ct);
        var stopped = await elderly.GetByMonthAsync(
            year, month, isStop: true, categoryCode: null, excludeImported: false, ct: ct);

        if (confirmed.IsFailure && stopped.IsFailure)
        {
            return Result.Failure<List<ReprintRecordItem>>(
                confirmed.ErrorCode ?? stopped.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                confirmed.Message ?? stopped.Message ?? "普惠高龄按月查询失败");
        }

        var merged = new List<ElderlyApplication>();
        if (confirmed.HasValue) merged.AddRange(confirmed.Value);
        if (stopped.HasValue) merged.AddRange(stopped.Value);

        return Result.Success(merged
            .GroupBy(app => app.Id)
            .Select(group => group.First())
            .OrderByDescending(app => BusinessTimeOf(app))
            .Take(limit)
            .Select(ToItem)
            .ToList());
    }

    public override async Task<Result<ReprintRecordDetail>> GetDetailAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        var appResult = await elderly.GetByIdAsync(id, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<ReprintRecordDetail>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "普惠高龄登记不存在");
        }

        var app = appResult.Value;
        var fields = ElderlyPrintDataBuilder.BuildSingleFields(app);
        return Result.Success(new ReprintRecordDetail(
            DomainKey,
            id,
            app.Name ?? string.Empty,
            app.IdCard ?? string.Empty,
            app.ApplicationNo ?? string.Empty,
            app.Status ?? string.Empty,
            ClassificationOf(app),
            BusinessTimeOf(app),
            app.Status == ElderlyBenefitConstants.StatusStopped ? "已停发" : "已确认",
            Mode,
            fields,
            []));
    }

    public override async Task<Result<List<ReprintTemplateOption>>> GetTemplatesAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        var appResult = await elderly.GetByIdAsync(id, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<List<ReprintTemplateOption>>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "普惠高龄登记不存在");
        }

        return await BuildTemplatesAsync(ClassificationOf(appResult.Value), null, ct);
    }

    public override async Task<Result<ReprintPrintData>> BuildPrintDataAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        var appResult = await elderly.GetByIdAsync(id, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<ReprintPrintData>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "普惠高龄登记不存在");
        }

        var app = appResult.Value;
        var classification = ClassificationOf(app);
        return Result.Success(new ReprintPrintData(
            DomainKey,
            id,
            app.Name ?? string.Empty,
            app.IdCard ?? string.Empty,
            classification,
            classification,
            app.Status,
            ElderlyPrintDataBuilder.BuildSingleFields(app),
            []));
    }

    private static ReprintRecordItem ToItem(ElderlyApplication app) => new(
        ReprintDomains.ElderlyBenefits,
        app.Id,
        app.Name ?? string.Empty,
        app.IdCard ?? string.Empty,
        app.ApplicationNo ?? string.Empty,
        app.Status ?? string.Empty,
        BusinessTimeOf(app),
        app.Status == ElderlyBenefitConstants.StatusStopped ? "已停发" : "已确认",
        ClassificationOf(app),
        string.Empty);

    /// <summary>补打模板分类：停发记录走 Stop，其余走 New（与旧版一致）。</summary>
    private static string ClassificationOf(ElderlyApplication app)
        => app.Status == ElderlyBenefitConstants.StatusStopped ? "Stop" : "New";

    private static DateTime BusinessTimeOf(ElderlyApplication app)
        => app.ApplyDate ?? app.ConfirmedAt ?? app.CreatedAt ?? DateTime.MinValue;
}
