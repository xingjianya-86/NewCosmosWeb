using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Server.Services.Reprint;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

/// <summary>
/// 低收入人口域补打 Provider（对齐旧版 <c>SocialAssistanceReprintProvider</c>）：
/// 全状态档案搜索（含在保/停保）+ 档案打印字段装配（复用 <see cref="ArchivePrintDataService"/>）。
/// </summary>
public sealed class SocialAssistanceReprintProvider(
    IApplicationService applications,
    ArchivePrintDataService printData,
    IDatabaseService database,
    IBusinessTimelineService timeline,
    ILoggerService logger)
    : ReprintProviderBase(database, timeline, logger)
{
    public override string DomainKey => ReprintDomains.FamilyApplication;

    public override string DisplayName => "低收入人口";

    public override string Mode => "ArchiveSet";

    public override ReprintMonthWindow MonthWindow => ReprintMonthWindow.BusinessProcess;

    public override async Task<Result<List<ReprintRecordItem>>> SearchByPersonAsync(
        string? keyword,
        int limit,
        CancellationToken ct = default)
    {
        if (limit <= 0) limit = 20;

        var result = await applications.GetPagedAsync(1, limit, null!, keyword?.Trim()!, ct);
        return result.IsFailure
            ? Result.Failure<List<ReprintRecordItem>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "档案搜索失败")
            : Result.Success((result.Value?.Items ?? []).Select(ToItem).ToList());
    }

    protected override async Task<Result<List<ReprintRecordItem>>> SearchByNaturalMonthAsync(
        int year,
        int month,
        int limit,
        CancellationToken ct)
    {
        var result = await applications.GetByMonthPagedAsync(year, month, 1, limit, ct);
        return result.IsFailure
            ? Result.Failure<List<ReprintRecordItem>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "按月查询失败")
            : Result.Success((result.Value?.Items ?? []).Select(ToItem).ToList());
    }

    public override async Task<Result<ReprintRecordDetail>> GetDetailAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        var appResult = await applications.GetByIdAsync(id, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<ReprintRecordDetail>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "申请不存在");
        }

        var dataResult = await BuildPrintDataAsync(id, userId, ct);
        if (dataResult.IsFailure || dataResult.Value is null)
        {
            return Result.Failure<ReprintRecordDetail>(
                dataResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                dataResult.Message ?? "加载打印数据失败");
        }

        var app = appResult.Value;
        var data = dataResult.Value;
        return Result.Success(new ReprintRecordDetail(
            DomainKey,
            id,
            app.ApplicantName ?? string.Empty,
            app.ApplicantIdCard ?? string.Empty,
            app.ApplicationNo ?? string.Empty,
            app.Status ?? string.Empty,
            app.ClassificationResult ?? string.Empty,
            app.FirstApprovedAt ?? app.UpdatedAt,
            ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? string.Empty),
            Mode,
            data.Fields,
            data.TableRows));
    }

    public override async Task<Result<List<ReprintTemplateOption>>> GetTemplatesAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        var appResult = await applications.GetByIdAsync(id, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<List<ReprintTemplateOption>>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "申请不存在");
        }

        return await BuildTemplatesAsync(appResult.Value.ClassificationResult, null, ct);
    }

    public override async Task<Result<ReprintPrintData>> BuildPrintDataAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        var dataResult = await printData.BuildAsync(id, userId, ct);
        if (dataResult.IsFailure || dataResult.Value is null)
        {
            return Result.Failure<ReprintPrintData>(
                dataResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                dataResult.Message ?? "申请数据装配失败");
        }

        var data = dataResult.Value;
        return Result.Success(new ReprintPrintData(
            DomainKey,
            id,
            data.ApplicantName,
            data.ApplicantIdCard,
            data.Classification,
            data.Classification ?? DomainKey,
            null,
            data.Fields,
            data.TableRows));
    }

    private static ReprintRecordItem ToItem(ApplicationEntity app) => new(
        ReprintDomains.FamilyApplication,
        app.Id,
        app.ApplicantName ?? string.Empty,
        app.ApplicantIdCard ?? string.Empty,
        app.ApplicationNo ?? string.Empty,
        app.Status ?? string.Empty,
        app.FirstApprovedAt ?? app.UpdatedAt,
        ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? string.Empty),
        app.ClassificationResult ?? string.Empty,
        app.ChainType ?? string.Empty);
}
