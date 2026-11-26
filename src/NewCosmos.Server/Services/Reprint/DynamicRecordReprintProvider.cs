using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Server.Services.Reprint;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

/// <summary>
/// 动态管理档案域补打 Provider。
/// <para>
/// 字段装配走已下沉到 Core 的 <see cref="IDynamicManagementRecordService.BuildFieldsAsync"/>，
/// 与 PC 动态管理记录使用同一口径（年值收入、货币财产、赡养人、经济明细、近亲属、入户调查结论）；
/// 表格行继续复用 <see cref="ArchivePrintDataService"/>（成员表）。
/// </para>
/// <para>
/// 检索使用 <see cref="IDynamicManagementRecordService.SearchChangedArchivesRawAsync"/>
/// 返回历史与现役全部版本；详情/打印前用
/// <see cref="IDynamicManagementRecordService.NormalizeToLatestApplicationIdAsync"/> 归一到链尾现役档案。
/// </para>
/// </summary>
public sealed class DynamicRecordReprintProvider(
    IApplicationService applications,
    ArchivePrintDataService printData,
    IDynamicManagementRecordService dynamicRecords,
    IDatabaseService database,
    IBusinessTimelineService timeline,
    ILoggerService logger)
    : ReprintProviderBase(database, timeline, logger)
{
    /// <summary>动态管理固定模板名（唯一，模板 248 挂「变更档案」分类）。</summary>
    public const string FixedTemplateName = "模板_社会救助对象动态管理记录";

    private const string ListSelect = """
        SELECT a.id                    AS ApplicationId,
               a.application_no        AS ApplicationNo,
               a.applicant_name        AS ApplicantName,
               a.applicant_id_card     AS ApplicantIdCard,
               a.status                AS Status,
               a.classification_result AS ClassificationResult,
               a.chain_type            AS ChainType,
               COALESCE(MAX(c.changed_at), a.updated_at) AS LastChangeAt,
               COUNT(c.id)::int        AS ChangeCount
        FROM nc_biz_applications a
        JOIN nc_biz_change_records c
          ON c.application_id = a.id AND c.deleted_at IS NULL
        """;

    public override string DomainKey => ReprintDomains.DynamicManagementRecord;

    public override string DisplayName => "动态管理档案";

    public override string Mode => "DynamicRecord";

    public override ReprintMonthWindow MonthWindow => ReprintMonthWindow.BusinessProcess;

    public override async Task<Result<List<ReprintRecordItem>>> SearchByPersonAsync(
        string? keyword,
        int limit,
        CancellationToken ct = default)
    {
        if (limit <= 0) limit = 20;

        // 与 PC 同一检索口径：不归一，历史旧档案与现役档案分别返回，供补打中心选择版本。
        var result = await dynamicRecords.SearchChangedArchivesRawAsync(keyword, limit, ct);
        return result.IsFailure
            ? Result.Failure<List<ReprintRecordItem>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "变更档案搜索失败")
            : Result.Success((result.Value ?? []).Select(ToItem).ToList());
    }

    protected override async Task<Result<List<ReprintRecordItem>>> SearchByNaturalMonthAsync(
        int year,
        int month,
        int limit,
        CancellationToken ct)
    {
        var from = new DateTime(year, month, 1);
        var to = from.AddMonths(1);

        var sql = ListSelect + "\n" + """
            WHERE a.deleted_at IS NULL
              AND COALESCE(c.change_date, c.changed_at::date) >= $1::date
              AND COALESCE(c.change_date, c.changed_at::date) <  $2::date
            GROUP BY a.id
            ORDER BY COALESCE(MAX(c.changed_at), a.updated_at) DESC
            LIMIT $3
            """;

        var result = await Database.QueryAsync<DynamicChangeRow>(sql, ct, from, to, limit);
        return result.IsFailure
            ? Result.Failure<List<ReprintRecordItem>>(
                result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "变更档案按月查询失败")
            : Result.Success((result.Value ?? []).Select(ToItem).ToList());
    }

    public override async Task<Result<ReprintRecordDetail>> GetDetailAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        // 详情按链尾现役档案展示与装配（历史版本在列表中以状态/链类型标识）。
        var normalizeResult = await dynamicRecords.NormalizeToLatestApplicationIdAsync(id, ct);
        if (normalizeResult.IsFailure)
        {
            return Result.Failure<ReprintRecordDetail>(
                normalizeResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                normalizeResult.Message ?? "动态管理档案不存在");
        }

        var latestId = normalizeResult.Value;
        var appResult = await applications.GetByIdAsync(latestId, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<ReprintRecordDetail>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "动态管理档案不存在");
        }

        var dataResult = await BuildPrintDataAsync(latestId, userId, ct);
        if (dataResult.IsFailure || dataResult.Value is null)
        {
            return Result.Failure<ReprintRecordDetail>(
                dataResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                dataResult.Message ?? "动态管理档案字段装配失败");
        }

        var app = appResult.Value;
        var data = dataResult.Value;
        var classification = app.ClassificationResult ?? string.Empty;
        return Result.Success(new ReprintRecordDetail(
            DomainKey,
            latestId,
            app.ApplicantName ?? string.Empty,
            app.ApplicantIdCard ?? string.Empty,
            app.ApplicationNo ?? string.Empty,
            app.Status ?? string.Empty,
            classification,
            BusinessTimeOf(app),
            $"{ClassificationConstants.ConvertToFullName(classification)} · {VersionLabel(app.Status)}",
            Mode,
            data.Fields,
            data.TableRows));
    }

    public override async Task<Result<List<ReprintTemplateOption>>> GetTemplatesAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        var normalizeResult = await dynamicRecords.NormalizeToLatestApplicationIdAsync(id, ct);
        if (normalizeResult.IsFailure)
        {
            return Result.Failure<List<ReprintTemplateOption>>(
                normalizeResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                normalizeResult.Message ?? "动态管理档案不存在");
        }

        var appResult = await applications.GetByIdAsync(normalizeResult.Value, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<List<ReprintTemplateOption>>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "动态管理档案不存在");
        }

        // 动态管理域固定单模板（与旧版方案 B 一致，不再按名称模糊匹配全库）。
        return await BuildTemplatesAsync(appResult.Value.ClassificationResult, FixedTemplateName, ct);
    }

    public override async Task<Result<ReprintPrintData>> BuildPrintDataAsync(
        long id,
        int userId,
        CancellationToken ct = default)
    {
        // 打印前归一到链尾现役档案：变更记录常挂在停旧建新的旧档案上。
        var normalizeResult = await dynamicRecords.NormalizeToLatestApplicationIdAsync(id, ct);
        if (normalizeResult.IsFailure)
        {
            return Result.Failure<ReprintPrintData>(
                normalizeResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                normalizeResult.Message ?? "动态管理档案不存在");
        }

        var latestId = normalizeResult.Value;

        // 主字段走 PC 同口径动态管理记录装配（年值收入/货币财产/赡养人/经济明细/近亲属/入户结论）。
        var fieldsResult = await dynamicRecords.BuildFieldsAsync(latestId, ct);
        if (fieldsResult.IsFailure || fieldsResult.Value is null)
        {
            return Result.Failure<ReprintPrintData>(
                fieldsResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                fieldsResult.Message ?? "动态管理档案字段装配失败");
        }

        var appResult = await applications.GetByIdAsync(latestId, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<ReprintPrintData>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "动态管理档案不存在");
        }

        var app = appResult.Value;
        var fields = new Dictionary<string, string>(fieldsResult.Value, StringComparer.Ordinal);
        var tableRows = new List<Dictionary<string, string>>();

        // 表格行沿用档案字段服务（成员表）；其余非冲突字段作为兜底补空。
        var archiveResult = await printData.BuildAsync(latestId, userId, ct);
        if (archiveResult.IsSuccess && archiveResult.Value is not null)
        {
            foreach (var pair in archiveResult.Value.Fields)
            {
                fields.TryAdd(pair.Key, pair.Value);
            }

            tableRows.AddRange(archiveResult.Value.TableRows);
        }
        else
        {
            Logger.Warn($"动态管理档案表格行装配失败，按主字段继续: ApplicationId={latestId}, {archiveResult.Message}");
        }

        var classification = app.ClassificationResult ?? string.Empty;
        return Result.Success(new ReprintPrintData(
            DomainKey,
            latestId,
            app.ApplicantName ?? string.Empty,
            app.ApplicantIdCard ?? string.Empty,
            classification,
            string.IsNullOrWhiteSpace(classification) ? DomainKey : classification,
            app.Status,
            fields,
            tableRows));
    }

    private static ReprintRecordItem ToItem(ChangedArchiveSummary row) => new(
        ReprintDomains.DynamicManagementRecord,
        row.ApplicationId,
        row.HeadName ?? string.Empty,
        row.IdCard ?? string.Empty,
        row.ApplicationNo ?? string.Empty,
        row.Status ?? string.Empty,
        row.LastChangeDate,
        $"{ClassificationConstants.ConvertToFullName(row.Classification ?? string.Empty)} · {VersionLabel(row.Status)}",
        row.Classification ?? string.Empty,
        row.ChainType ?? string.Empty);

    private static ReprintRecordItem ToItem(DynamicChangeRow row) => new(
        ReprintDomains.DynamicManagementRecord,
        row.ApplicationId,
        row.ApplicantName ?? string.Empty,
        row.ApplicantIdCard ?? string.Empty,
        row.ApplicationNo ?? string.Empty,
        row.Status ?? string.Empty,
        row.LastChangeAt,
        $"{ClassificationConstants.ConvertToFullName(row.ClassificationResult ?? string.Empty)} · {VersionLabel(row.Status)} · 变更{row.ChangeCount}次",
        row.ClassificationResult ?? string.Empty,
        row.ChainType ?? string.Empty);

    /// <summary>补打列表的历史版本标识（Stopped = 停旧建新的旧档案）。</summary>
    private static string VersionLabel(string? status)
        => string.Equals(status, ApplicationStatusCodes.STOPPED, StringComparison.Ordinal)
            ? "历史版本"
            : "现役版本";

    private static DateTime BusinessTimeOf(ApplicationEntity app)
        => app.FirstApprovedAt ?? app.UpdatedAt;

    /// <summary>动态变更人群聚合行。</summary>
    private sealed class DynamicChangeRow
    {
        public long ApplicationId { get; set; }
        public string? ApplicationNo { get; set; }
        public string? ApplicantName { get; set; }
        public string? ApplicantIdCard { get; set; }
        public string? Status { get; set; }
        public string? ClassificationResult { get; set; }
        public string? ChainType { get; set; }
        public DateTime LastChangeAt { get; set; }
        public int ChangeCount { get; set; }
    }
}
