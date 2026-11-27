using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ElderlyBenefits;

namespace NewCosmos.Server.Services;

/// <summary>
/// 高龄年度认证报表：Server 侧按数据范围取源行，再复用 Core 的列/行拼装与 Excel 导出器。
/// 不重写列定义，不在 Server 里重抄月度/认证业务口径。
/// </summary>
public sealed class ElderlyCertificationReportQueryService(
    IDatabaseService database,
    ElderlyCertificationQueryService queryService,
    ILoggerService logger)
{
    private static readonly string[] UnverifiedStatuses =
    {
        ElderlyCertificationConstants.CertStatusPending,
        ElderlyCertificationConstants.CertStatusRefused,
        ElderlyCertificationConstants.CertStatusFailed,
        ElderlyCertificationConstants.CertStatusNotLocated
    };

    public Task<Result<ElderlyCertificationReportFileDto>> ExportExcelAsync(
        int userId,
        long campaignId,
        string formKey,
        CancellationToken ct = default)
        => ExportFileAsync(userId, campaignId, formKey, isPdf: false, ct);

    public Task<Result<ElderlyCertificationReportFileDto>> ExportPdfAsync(
        int userId,
        long campaignId,
        string formKey,
        CancellationToken ct = default)
        => ExportFileAsync(userId, campaignId, formKey, isPdf: true, ct);

    private async Task<Result<ElderlyCertificationReportFileDto>> ExportFileAsync(
        int userId,
        long campaignId,
        string formKey,
        bool isPdf,
        CancellationToken ct)
    {
        if (!ElderlyCertificationReportFormKeys.IsValid(formKey))
        {
            return Result.Failure<ElderlyCertificationReportFileDto>(
                ErrorCodes.VALIDATION_FAILED, $"未知的认证报表类型：{formKey}");
        }

        var visible = await queryService.GetVisibleCampaignAsync(userId, campaignId, ct);
        if (visible.IsFailure)
        {
            return Result.Failure<ElderlyCertificationReportFileDto>(
                visible.ErrorCode!, visible.Message!);
        }

        var campaign = visible.Value;
        var title = $"{campaign.DisplayName} {ElderlyCertificationReportFormKeys.GetName(formKey)}";

        try
        {
            var reportResult = formKey switch
            {
                ElderlyCertificationReportFormKeys.Roster => await BuildRosterAsync(
                    userId, campaign, title, ct),
                ElderlyCertificationReportFormKeys.Summary => await BuildSummaryAsync(
                    userId, campaign, title, ct),
                ElderlyCertificationReportFormKeys.Unverified => await BuildUnverifiedAsync(
                    userId, campaign, title, ct),
                _ => Result.Failure<ElderlyCertificationReport>(
                    ErrorCodes.VALIDATION_FAILED, $"未知的认证报表类型：{formKey}")
            };

            if (reportResult.IsFailure)
            {
                return Result.Failure<ElderlyCertificationReportFileDto>(
                    reportResult.ErrorCode!, reportResult.Message!);
            }

            var export = isPdf
                ? ElderlyCertificationPdfRenderer.Render(reportResult.Value)
                : ElderlyCertificationExcelExporter.Export(reportResult.Value);
            if (export.IsFailure)
            {
                return Result.Failure<ElderlyCertificationReportFileDto>(
                    export.ErrorCode!, export.Message!);
            }

            var extension = isPdf ? "pdf" : "xlsx";
            var fileName = $"{SafePart(campaign.CampaignNo)}_{formKey}_{DateTime.Now:yyyyMMddHHmmss}.{extension}";
            return Result.Success(new ElderlyCertificationReportFileDto(export.Value, fileName));
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                $"导出高龄认证报表失败: CampaignId={campaignId}, FormKey={formKey}, Pdf={isPdf}");
            return Result.Failure<ElderlyCertificationReportFileDto>(
                ErrorCodes.UNKNOWN_ERROR, $"导出报表失败：{ex.Message}");
        }
    }

    private async Task<Result<ElderlyCertificationReport>> BuildRosterAsync(
        int userId,
        ElderlyCertCampaign campaign,
        string title,
        CancellationToken ct)
    {
        var parameters = new List<object?> { campaign.Id };
        var scope = await queryService.BuildTaskScopeAsync(userId, "t", 2, ct);
        var conditions = new List<string> { "t.campaign_id = $1", "t.deleted_at IS NULL" };
        AppendScope(conditions, parameters, scope);

        var limitIndex = parameters.Count + 1;
        parameters.Add(ElderlyCertificationReportService.ReportRowLimit);

        var sql = $@"
            SELECT tw.town_name AS town_name,
                   COALESCE(NULLIF(t.hukou_village, ''), v.village_name) AS village_name,
                   t.id_card, t.name, t.gender, t.birth_date, t.age_snapshot,
                   t.category, t.issue_amount, t.hukou_address, t.family_address, t.phone,
                   t.verify_method, t.verified_at, t.cert_status, t.evidence_status,
                   t.stop_decision, t.result_note
            FROM nc_biz_elderly_cert_tasks t
            LEFT JOIN nc_regions_towns tw ON tw.id = t.hukou_town_id
            LEFT JOIN nc_regions_villages v ON v.id = t.hukou_village_id
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY tw.town_name NULLS LAST, village_name NULLS LAST, t.name, t.id_card
            LIMIT ${limitIndex}";

        var query = await database.QueryAsync<ElderlyCertificationRosterSourceRow>(
            sql, ct, parameters.ToArray());
        if (query.IsFailure)
        {
            return Result.Failure<ElderlyCertificationReport>(
                query.ErrorCode!, query.Message!);
        }

        var rows = ElderlyCertificationReportService.BuildRosterRows(
            query.Value ?? new List<ElderlyCertificationRosterSourceRow>());
        return Result.Success(new ElderlyCertificationReport(
            ElderlyCertificationReportFormKeys.Roster,
            title,
            ElderlyCertificationReportService.RosterColumns,
            rows));
    }

    private async Task<Result<ElderlyCertificationReport>> BuildSummaryAsync(
        int userId,
        ElderlyCertCampaign campaign,
        string title,
        CancellationToken ct)
    {
        var parameters = new List<object?>
        {
            campaign.Id,
            ElderlyCertificationConstants.CertStatusVerified,
            ElderlyCertificationConstants.CertStatusExempt,
            ElderlyCertificationConstants.CertStatusPending,
            ElderlyCertificationConstants.CertStatusRefused,
            ElderlyCertificationConstants.CertStatusFailed,
            ElderlyCertificationConstants.CertStatusDeceased,
            ElderlyCertificationConstants.CertStatusMovedOut,
            ElderlyCertificationConstants.CertStatusNotLocated,
            ElderlyCertificationConstants.CertStatusStopped,
            ElderlyCertificationConstants.EvidenceStatusMissing,
            ElderlyCertificationConstants.EvidenceStatusPartial,
            ElderlyCertificationConstants.EvidenceStatusAmbiguous
        };

        var scope = await queryService.BuildTaskScopeAsync(userId, "t", parameters.Count + 1, ct);
        var conditions = new List<string> { "t.campaign_id = $1", "t.deleted_at IS NULL" };
        AppendScope(conditions, parameters, scope);

        var limitIndex = parameters.Count + 1;
        parameters.Add(ElderlyCertificationReportService.ReportRowLimit);

        var sql = $@"
            SELECT COALESCE(tw.town_name, '') AS town_name,
                   COALESCE(NULLIF(t.hukou_village, ''), v.village_name, '') AS village_name,
                   COUNT(*)::int AS total,
                   COUNT(*) FILTER (WHERE t.cert_status = $2)::int AS verified,
                   COUNT(*) FILTER (WHERE t.cert_status = $3)::int AS exempt,
                   COUNT(*) FILTER (WHERE t.cert_status = $4)::int AS pending,
                   COUNT(*) FILTER (WHERE t.cert_status = $5)::int AS refused,
                   COUNT(*) FILTER (WHERE t.cert_status = $6)::int AS failed,
                   COUNT(*) FILTER (WHERE t.cert_status = $7)::int AS deceased,
                   COUNT(*) FILTER (WHERE t.cert_status = $8)::int AS moved_out,
                   COUNT(*) FILTER (WHERE t.cert_status = $9)::int AS not_located,
                   COUNT(*) FILTER (WHERE t.cert_status = $10)::int AS stopped,
                   COUNT(*) FILTER (WHERE t.evidence_status IN ($11, $12, $13))::int AS evidence_issue
            FROM nc_biz_elderly_cert_tasks t
            LEFT JOIN nc_regions_towns tw ON tw.id = t.hukou_town_id
            LEFT JOIN nc_regions_villages v ON v.id = t.hukou_village_id
            WHERE {string.Join(" AND ", conditions)}
            GROUP BY town_name, village_name
            ORDER BY town_name, village_name
            LIMIT ${limitIndex}";

        var query = await database.QueryAsync<ElderlyCertificationSummarySourceRow>(
            sql, ct, parameters.ToArray());
        if (query.IsFailure)
        {
            return Result.Failure<ElderlyCertificationReport>(
                query.ErrorCode!, query.Message!);
        }

        var rows = ElderlyCertificationReportService.BuildSummaryRows(
            query.Value ?? new List<ElderlyCertificationSummarySourceRow>());
        return Result.Success(new ElderlyCertificationReport(
            ElderlyCertificationReportFormKeys.Summary,
            title,
            ElderlyCertificationReportService.SummaryColumns,
            rows));
    }

    private async Task<Result<ElderlyCertificationReport>> BuildUnverifiedAsync(
        int userId,
        ElderlyCertCampaign campaign,
        string title,
        CancellationToken ct)
    {
        var parameters = new List<object?>
        {
            campaign.Id,
            UnverifiedStatuses,
            ElderlyCertificationConstants.CertStatusStopped,
            ElderlyCertificationConstants.StopDecisionApproved
        };

        var scope = await queryService.BuildTaskScopeAsync(userId, "t", parameters.Count + 1, ct);
        var conditions = new List<string>
        {
            "t.campaign_id = $1",
            "t.deleted_at IS NULL",
            "t.cert_status = ANY($2)",
            "t.cert_status <> $3",
            "t.stop_decision <> $4"
        };
        AppendScope(conditions, parameters, scope);

        var limitIndex = parameters.Count + 1;
        parameters.Add(ElderlyCertificationReportService.ReportRowLimit);

        var sql = $@"
            SELECT tw.town_name AS town_name,
                   COALESCE(NULLIF(t.hukou_village, ''), v.village_name) AS village_name,
                   t.id_card, t.name, t.gender, t.birth_date, t.age_snapshot, t.phone,
                   t.cert_status, t.evidence_status, t.due_stop_date, t.death_date,
                   t.stop_decision, t.follow_up_date,
                   COALESCE(NULLIF(t.stop_note, ''), t.result_note) AS stop_note,
                   t.result_note
            FROM nc_biz_elderly_cert_tasks t
            LEFT JOIN nc_regions_towns tw ON tw.id = t.hukou_town_id
            LEFT JOIN nc_regions_villages v ON v.id = t.hukou_village_id
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY tw.town_name NULLS LAST, village_name NULLS LAST, t.name, t.id_card
            LIMIT ${limitIndex}";

        var query = await database.QueryAsync<ElderlyCertificationUnverifiedSourceRow>(
            sql, ct, parameters.ToArray());
        if (query.IsFailure)
        {
            return Result.Failure<ElderlyCertificationReport>(
                query.ErrorCode!, query.Message!);
        }

        var rows = ElderlyCertificationReportService.BuildUnverifiedRows(
            query.Value ?? new List<ElderlyCertificationUnverifiedSourceRow>(),
            campaign);
        return Result.Success(new ElderlyCertificationReport(
            ElderlyCertificationReportFormKeys.Unverified,
            title,
            ElderlyCertificationReportService.UnverifiedColumns,
            rows));
    }

    private static void AppendScope(
        List<string> conditions,
        List<object?> parameters,
        DataScopeClause scope)
    {
        if (scope.IsEmpty) return;
        conditions.Add(scope.Sql);
        parameters.AddRange(scope.Parameters);
    }

    private static string SafePart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "campaign";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var text = new string(chars).Trim();
        return text.Length == 0 ? "campaign" : text;
    }
}
