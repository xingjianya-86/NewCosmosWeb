using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Server.Services.Reprint;

/// <summary>
/// 临时救助域补打 Provider（对齐旧版 <c>TempReliefReprintProvider</c>）：
/// 已归档记录搜索 + 疾病/灾害/教育/成员子表加载 + <see cref="TempReliefPrintDataBuilder"/> 字段装配。
/// </summary>
public sealed class TempReliefReprintProvider(
    ITempReliefService tempRelief,
    IDatabaseService database,
    IBusinessTimelineService timeline,
    ILoggerService logger)
    : ReprintProviderBase(database, timeline, logger)
{
    public override string DomainKey => ReprintDomains.TempRelief;

    public override string DisplayName => "临时救助";

    public override string Mode => "ArchiveSet";

    public override ReprintMonthWindow MonthWindow => ReprintMonthWindow.TempReliefFull;

    public override async Task<Result<List<ReprintRecordItem>>> SearchByPersonAsync(
        string? keyword,
        int limit,
        CancellationToken ct = default)
    {
        if (limit <= 0) limit = 20;

        var result = await tempRelief.GetArchivedPagedAsync(keyword?.Trim(), 1, limit, ct);
        return result.IsFailure
            ? Result.Failure<List<ReprintRecordItem>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "临时救助搜索失败")
            : Result.Success((result.Value?.Items ?? []).Select(ToItem).ToList());
    }

    protected override async Task<Result<List<ReprintRecordItem>>> SearchByNaturalMonthAsync(
        int year,
        int month,
        int limit,
        CancellationToken ct)
    {
        var result = await tempRelief.GetByMonthPagedAsync(year, month, 1, limit, ct);
        return result.IsFailure
            ? Result.Failure<List<ReprintRecordItem>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "临时救助按月查询失败")
            : Result.Success((result.Value?.Items ?? []).Select(ToItem).ToList());
    }

    public override async Task<Result<ReprintRecordDetail>> GetDetailAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        var loaded = await LoadAsync(id, ct);
        if (loaded.IsFailure || loaded.Value.App is null)
        {
            return Result.Failure<ReprintRecordDetail>(
                loaded.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                loaded.Message ?? "临时救助记录不存在");
        }

        var app = loaded.Value.App;
        return Result.Success(new ReprintRecordDetail(
            DomainKey,
            id,
            app.ApplicantName ?? string.Empty,
            app.ApplicantIdCard ?? string.Empty,
            app.ApplicationNo ?? string.Empty,
            app.Status ?? string.Empty,
            app.ReliefType ?? string.Empty,
            BusinessTimeOf(app),
            TempReliefConstants.GetReliefTypeName(app.ReliefType),
            Mode,
            loaded.Value.Fields,
            loaded.Value.Rows));
    }

    public override async Task<Result<List<ReprintTemplateOption>>> GetTemplatesAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        var appResult = await tempRelief.GetByIdAsync(id, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<List<ReprintTemplateOption>>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "临时救助申请不存在");
        }

        return await BuildTemplatesAsync(appResult.Value.ReliefType, null, ct);
    }

    public override async Task<Result<ReprintPrintData>> BuildPrintDataAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        var loaded = await LoadAsync(id, ct);
        if (loaded.IsFailure || loaded.Value.App is null)
        {
            return Result.Failure<ReprintPrintData>(
                loaded.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                loaded.Message ?? "临时救助记录不存在");
        }

        var app = loaded.Value.App;
        return Result.Success(new ReprintPrintData(
            DomainKey,
            id,
            app.ApplicantName ?? string.Empty,
            app.ApplicantIdCard ?? string.Empty,
            app.ReliefType,
            app.ReliefType ?? DomainKey,
            app.Status,
            loaded.Value.Fields,
            loaded.Value.Rows));
    }

    /// <summary>
    /// 加载主表 + 四类子表并装配字段。补打正式文书：任一子表读取失败都中止，
    /// 避免打印缺明细的版本（与旧版 Provider 一致）。
    /// </summary>
    private async Task<Result<TempReliefPrintData>> LoadAsync(long id, CancellationToken ct)
    {
        var appResult = await tempRelief.GetByIdAsync(id, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<TempReliefPrintData>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "临时救助申请不存在");
        }

        var app = appResult.Value;

        var membersResult = await tempRelief.GetMembersByApplicationIdAsync(id, ct);
        if (membersResult.IsFailure)
        {
            return Result.Failure<TempReliefPrintData>(
                membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"成员明细加载失败: {membersResult.Message}");
        }

        var diseasesResult = await tempRelief.GetDiseasesByApplicationIdAsync(id, ct);
        if (diseasesResult.IsFailure)
        {
            return Result.Failure<TempReliefPrintData>(
                diseasesResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"疾病明细加载失败: {diseasesResult.Message}");
        }

        var accidentsResult = await tempRelief.GetAccidentsByApplicationIdAsync(id, ct);
        if (accidentsResult.IsFailure)
        {
            return Result.Failure<TempReliefPrintData>(
                accidentsResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"灾害明细加载失败: {accidentsResult.Message}");
        }

        var educationsResult = await tempRelief.GetEducationsByApplicationIdAsync(id, ct);
        if (educationsResult.IsFailure)
        {
            return Result.Failure<TempReliefPrintData>(
                educationsResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                $"教育明细加载失败: {educationsResult.Message}");
        }

        app.Diseases = diseasesResult.Value ?? [];
        app.Accidents = accidentsResult.Value ?? [];
        app.Educations = educationsResult.Value ?? [];

        var members = membersResult.Value ?? [];
        var (acceptanceDate, investigationDate) = await TempReliefPrintDataBuilder.ResolveScheduleAsync(Timeline, app);

        var phoneResult = await tempRelief.GetReportUnitPhoneAsync(app.ReportUnit, ct);
        if (phoneResult.IsFailure)
        {
            Logger.Warn($"临时救助补打：报告单位电话读取失败（字段留空）: {phoneResult.Message}");
        }

        var fields = TempReliefPrintDataBuilder.BuildSingleFields(
            app, members, phoneResult.IsSuccess ? phoneResult.Value ?? string.Empty : string.Empty,
            acceptanceDate, investigationDate);

        var rows = members
            .Where(member => !string.IsNullOrWhiteSpace(member.MemberName))
            .Select(member =>
            {
                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                Put(row, FieldKeys.FAMILY_MEMBER_NAME, member.MemberName);
                Put(row, FieldKeys.FAMILY_MEMBER_ID_CARD, member.IdCard);
                Put(row, FieldKeys.FAMILY_MEMBER_RELATION, member.Relation);
                return row;
            })
            .Where(row => row.Count > 0)
            .ToList();

        return Result.Success(new TempReliefPrintData(app, fields, rows));
    }

    private static ReprintRecordItem ToItem(TempReliefApplication app) => new(
        ReprintDomains.TempRelief,
        app.Id,
        app.ApplicantName ?? string.Empty,
        app.ApplicantIdCard ?? string.Empty,
        app.ApplicationNo ?? string.Empty,
        app.Status ?? string.Empty,
        BusinessTimeOf(app),
        TempReliefConstants.GetReliefTypeName(app.ReliefType),
        app.ReliefType ?? string.Empty,
        string.Empty);

    private static DateTime BusinessTimeOf(TempReliefApplication app)
        => app.ApplyDate ?? app.ReportTime ?? app.CreatedAt ?? DateTime.MinValue;

    private static void Put(Dictionary<string, string> row, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) row[key] = value;
    }

    private sealed record TempReliefPrintData(
        TempReliefApplication App,
        Dictionary<string, string> Fields,
        List<Dictionary<string, string>> Rows);
}
