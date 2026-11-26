using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Server.Services.Reprint;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

/// <summary>
/// 动态管理档案域补打 Provider。
/// <para>
/// 现状：legacy 的 <c>IDynamicManagementRecordService</c>（档案链归一、变更事实装配、重新生成）
/// 尚未下沉到 Core，因此本期先基于 <c>nc_biz_change_records</c> + 现役 <c>nc_biz_applications</c>
/// 做只读检索/详情；打印字段复用低收入人口的
/// <see cref="ArchivePrintDataService"/>（即现役档案字段集）。
/// </para>
/// <para>
/// TODO(动态档案下沉)：待 <c>IDynamicManagementRecordService</c> 下沉后，切换为
/// SearchChangedArchivesRawAsync / NormalizeToLatestApplicationIdAsync / BuildFieldsAsync，
/// 以补齐历史档案版本、变更文书（渐退审批表等）字段与“重新生成”能力。
/// </para>
/// </summary>
public sealed class DynamicRecordReprintProvider(
    IApplicationService applications,
    ArchivePrintDataService printData,
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
        var pattern = ToLikePattern(keyword);

        var sql = ListSelect + "\n" + """
            WHERE a.deleted_at IS NULL
              AND ($1::text IS NULL
                   OR a.applicant_name ILIKE $1
                   OR a.applicant_id_card ILIKE $1
                   OR EXISTS (
                       SELECT 1 FROM nc_biz_family_members fm
                       WHERE fm.application_id = a.id
                         AND fm.deleted_at IS NULL
                         AND (fm.name ILIKE $1 OR fm.id_card ILIKE $1)))
            GROUP BY a.id
            ORDER BY COALESCE(MAX(c.changed_at), a.updated_at) DESC
            LIMIT $2
            """;

        var result = await Database.QueryAsync<DynamicChangeRow>(sql, ct, pattern, limit);
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
        var appResult = await applications.GetByIdAsync(id, ct);
        if (appResult.IsFailure || appResult.Value is null)
        {
            return Result.Failure<ReprintRecordDetail>(
                appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                appResult.Message ?? "动态管理档案不存在");
        }

        var dataResult = await BuildPrintDataAsync(id, userId, ct);
        if (dataResult.IsFailure || dataResult.Value is null)
        {
            return Result.Failure<ReprintRecordDetail>(
                dataResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                dataResult.Message ?? "动态管理档案字段装配失败");
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
            BusinessTimeOf(app),
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
        // TODO(动态档案下沉)：现役档案字段集的可用部分；变更事实/历史档案版本字段待 legacy 服务下沉后补齐。
        var dataResult = await printData.BuildAsync(id, userId, ct);
        if (dataResult.IsFailure || dataResult.Value is null)
        {
            return Result.Failure<ReprintPrintData>(
                dataResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                dataResult.Message ?? "动态管理档案字段装配失败");
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

    private static ReprintRecordItem ToItem(DynamicChangeRow row) => new(
        ReprintDomains.DynamicManagementRecord,
        row.ApplicationId,
        row.ApplicantName ?? string.Empty,
        row.ApplicantIdCard ?? string.Empty,
        row.ApplicationNo ?? string.Empty,
        row.Status ?? string.Empty,
        row.LastChangeAt,
        $"{ClassificationConstants.ConvertToFullName(row.ClassificationResult ?? string.Empty)} · 变更{row.ChangeCount}次",
        row.ClassificationResult ?? string.Empty,
        row.ChainType ?? string.Empty);

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
