using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Services;

/// <summary>
/// 高龄年度认证只读查询（批次、统计、名单、任务详情、凭证、凭证图片、停发预览）。
/// 认证域数据范围以户籍乡镇/村落到任务表 t.hukou_town_id / t.hukou_village_id / 地址文本，
/// 因此统计与分页全部使用 Server 侧 scoped SQL（Core 的统计/分页不接受数据范围条件）。
/// SQL 统一 $1..$n 参数、显式列、LIMIT/OFFSET；越权记录统一按 NOT_FOUND 返回。
/// </summary>
public sealed class ElderlyCertificationQueryService(
    IDatabaseService database,
    DataScopeFilterBuilder scopeBuilder,
    IDataScopeService dataScope,
    IElderlyCertificationService certificationService,
    ILoggerService logger)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private const string TaskTable = "nc_biz_elderly_cert_tasks";
    private const string EvidenceTable = "nc_biz_elderly_cert_evidence";

    /// <summary>与 Core ElderlyCertificationService.TaskSelectColumns 对齐，供详情/停发预览映射实体。</summary>
    private const string TaskColumns = @"
        t.id, t.campaign_id, t.id_card, t.name, t.application_id, t.application_no, t.source_kind,
        t.gender, t.birth_date, t.age_snapshot, t.category, t.identity_flag, t.issue_amount,
        t.hukou_city_id, t.hukou_county_id, t.hukou_town_id, t.hukou_village_id, t.hukou_village, t.hukou_address,
        t.family_city_id, t.family_county_id, t.family_town_id, t.family_village_id, t.family_address,
        t.phone, t.bank_name, t.bank_account, t.agent_name, t.agent_relation,
        t.snapshot_at, t.cert_status, t.verify_method, t.verified_at, t.verified_by, t.verifier_org, t.verifier_phone,
        t.result_note, t.death_date, t.expected_evidence_count, t.evidence_count, t.evidence_status,
        t.stop_decision, t.stop_reason, t.stop_note, t.follow_up_date, t.due_stop_date, t.actual_stop_date,
        t.stop_application_id, t.stopped_at, t.source_batch_no, t.source_file, t.source_row, t.source_submitted_at,
        t.last_imported_at, t.created_at, t.updated_at";

    private static readonly string[] StopCandidateStatuses =
    {
        ElderlyCertificationConstants.CertStatusPending,
        ElderlyCertificationConstants.CertStatusRefused,
        ElderlyCertificationConstants.CertStatusFailed,
        ElderlyCertificationConstants.CertStatusDeceased,
        ElderlyCertificationConstants.CertStatusMovedOut,
        ElderlyCertificationConstants.CertStatusNotLocated
    };

    // ───────────────────────────── 批次 ─────────────────────────────

    /// <summary>
    /// 批次列表：Core 返回全量批次；这里按任务户籍范围过滤。
    /// 可见规则：全部数据范围 / 批次内存在本范围任务 / 本人创建（便于新建后尚未生成名单时继续操作）/
    /// 批次尚无任何任务（空批次不包含任何人的 PII）。
    /// </summary>
    public async Task<Result<IReadOnlyList<ElderlyCertCampaignDto>>> GetCampaignsAsync(
        int userId,
        CancellationToken ct = default)
    {
        var campaignsResult = await certificationService.GetCampaignsAsync(ct);
        if (campaignsResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ElderlyCertCampaignDto>>(
                campaignsResult.ErrorCode!, campaignsResult.Message!);
        }

        var campaigns = campaignsResult.Value ?? [];

        var scope = await BuildTaskScopeAsync(
            userId, "t", 1, ct);

        IReadOnlySet<long> scopedCampaignIds;
        IReadOnlyDictionary<long, int> taskCounts;
        string? username;

        if (scope.IsEmpty)
        {
            return Result.Success<IReadOnlyList<ElderlyCertCampaignDto>>(
                campaigns.Select(MapCampaign).ToList());
        }

        var scopedResult = await database.QueryAsync<ElderlyCertCampaignIdRow>(
            $"SELECT DISTINCT t.campaign_id AS campaign_id FROM {TaskTable} t " +
            $"WHERE t.deleted_at IS NULL AND ({scope.Sql})",
            ct,
            scope.Parameters.ToArray());
        if (scopedResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ElderlyCertCampaignDto>>(
                scopedResult.ErrorCode!, scopedResult.Message!);
        }

        scopedCampaignIds = (scopedResult.Value ?? [])
            .Select(x => x.CampaignId)
            .ToHashSet();

        var countsResult = await database.QueryAsync<ElderlyCertCampaignTaskCountRow>(
            $"SELECT campaign_id, COUNT(*)::int AS task_count FROM {TaskTable} " +
            "WHERE deleted_at IS NULL GROUP BY campaign_id",
            ct);
        if (countsResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ElderlyCertCampaignDto>>(
                countsResult.ErrorCode!, countsResult.Message!);
        }

        taskCounts = (countsResult.Value ?? [])
            .GroupBy(x => x.CampaignId)
            .ToDictionary(g => g.Key, g => g.First().TaskCount);

        username = await GetUsernameAsync(userId, ct);

        var visible = campaigns
            .Where(campaign =>
                scopedCampaignIds.Contains(campaign.Id)
                || (!string.IsNullOrWhiteSpace(username)
                    && string.Equals(campaign.CreatedBy, username, StringComparison.OrdinalIgnoreCase))
                || !taskCounts.TryGetValue(campaign.Id, out var count)
                || count == 0)
            .Select(MapCampaign)
            .ToList();

        return Result.Success<IReadOnlyList<ElderlyCertCampaignDto>>(visible);
    }

    /// <summary>
    /// 取当前用户可见批次；不存在或不在数据范围统一返回 NOT_FOUND（不暴露记录是否存在）。
    /// 写服务复用本方法做批次级越权校验。
    /// </summary>
    public async Task<Result<ElderlyCertCampaign>> GetVisibleCampaignAsync(
        int userId,
        long campaignId,
        CancellationToken ct = default)
    {
        var campaignResult = await certificationService.GetCampaignAsync(campaignId, ct);
        if (campaignResult.IsFailure)
        {
            return Result.Failure<ElderlyCertCampaign>(
                campaignResult.ErrorCode!, campaignResult.Message!);
        }

        var campaign = campaignResult.Value;
        if (campaign is null)
        {
            return Result.Failure<ElderlyCertCampaign>(
                ApiErrorCodes.NotFound, "认证批次不存在或无权查看");
        }

        var scope = await BuildTaskScopeAsync(
            userId, "t", 2, ct);
        if (scope.IsEmpty)
        {
            return Result.Success(campaign);
        }

        var scopedParams = new List<object?> { campaignId };
        scopedParams.AddRange(scope.Parameters);

        var scopedCountResult = await database.ExecuteScalarAsync<long?>(
            $"SELECT COUNT(*) FROM {TaskTable} t " +
            $"WHERE t.campaign_id = $1 AND t.deleted_at IS NULL AND ({scope.Sql})",
            ct,
            scopedParams.ToArray());
        if (scopedCountResult.IsFailure)
        {
            return Result.Failure<ElderlyCertCampaign>(
                scopedCountResult.ErrorCode!, scopedCountResult.Message!);
        }

        if ((scopedCountResult.Value ?? 0) > 0)
        {
            return Result.Success(campaign);
        }

        var username = await GetUsernameAsync(userId, ct);
        if (!string.IsNullOrWhiteSpace(username)
            && string.Equals(campaign.CreatedBy, username, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Success(campaign);
        }

        var totalCountResult = await database.ExecuteScalarAsync<long?>(
            $"SELECT COUNT(*) FROM {TaskTable} WHERE campaign_id = $1 AND deleted_at IS NULL",
            ct,
            campaignId);
        if (totalCountResult.IsFailure)
        {
            return Result.Failure<ElderlyCertCampaign>(
                totalCountResult.ErrorCode!, totalCountResult.Message!);
        }

        // 空批次（尚未生成名单）不含 PII，允许有编辑权限的账号继续操作。
        if ((totalCountResult.Value ?? 0) == 0)
        {
            return Result.Success(campaign);
        }

        return Result.Failure<ElderlyCertCampaign>(
            ApiErrorCodes.NotFound, "认证批次不存在或无权查看");
    }

    // ───────────────────────────── 统计 ─────────────────────────────

    /// <summary>批次统计：口径与 Core GetStatsAsync 一致，但追加当前用户数据范围。</summary>
    public async Task<Result<ElderlyCertStatsDto>> GetStatsAsync(
        int userId,
        long campaignId,
        CancellationToken ct = default)
    {
        var visible = await GetVisibleCampaignAsync(userId, campaignId, ct);
        if (visible.IsFailure)
        {
            return Result.Failure<ElderlyCertStatsDto>(
                visible.ErrorCode!, visible.Message!);
        }

        var parameters = new List<object?>
        {
            campaignId,
            ElderlyCertificationConstants.CertStatusPending,
            ElderlyCertificationConstants.CertStatusVerified,
            ElderlyCertificationConstants.CertStatusRefused,
            ElderlyCertificationConstants.CertStatusFailed,
            ElderlyCertificationConstants.CertStatusDeceased,
            ElderlyCertificationConstants.CertStatusMovedOut,
            ElderlyCertificationConstants.CertStatusNotLocated,
            ElderlyCertificationConstants.CertStatusExempt,
            ElderlyCertificationConstants.CertStatusStopped,
            ElderlyCertificationConstants.CertStatusInvalid,
            ElderlyCertificationConstants.EvidenceStatusMissing,
            ElderlyCertificationConstants.EvidenceStatusPartial,
            ElderlyCertificationConstants.EvidenceStatusAmbiguous
        };

        var scope = await BuildTaskScopeAsync(
            userId, "t", parameters.Count + 1, ct);

        var conditions = new List<string> { "t.campaign_id = $1", "t.deleted_at IS NULL" };
        if (!scope.IsEmpty)
        {
            conditions.Add(scope.Sql);
            parameters.AddRange(scope.Parameters);
        }

        var sql =
            $@"SELECT COUNT(*)::int AS total,
                      COUNT(*) FILTER (WHERE t.cert_status = $2)::int AS pending,
                      COUNT(*) FILTER (WHERE t.cert_status = $3)::int AS verified,
                      COUNT(*) FILTER (WHERE t.cert_status = $4)::int AS refused,
                      COUNT(*) FILTER (WHERE t.cert_status = $5)::int AS failed,
                      COUNT(*) FILTER (WHERE t.cert_status = $6)::int AS deceased,
                      COUNT(*) FILTER (WHERE t.cert_status = $7)::int AS moved_out,
                      COUNT(*) FILTER (WHERE t.cert_status = $8)::int AS not_located,
                      COUNT(*) FILTER (WHERE t.cert_status = $9)::int AS exempt,
                      COUNT(*) FILTER (WHERE t.cert_status = $10)::int AS stopped,
                      COUNT(*) FILTER (WHERE t.cert_status = $11)::int AS invalid,
                      COUNT(*) FILTER (WHERE t.evidence_status IN ($12, $13, $14))::int AS evidence_issue,
                      COUNT(*) FILTER (WHERE t.cert_status IN ($2, $4, $5, $6, $7, $8))::int AS stop_candidate
               FROM {TaskTable} t
               WHERE {string.Join(" AND ", conditions)}";

        var statsResult = await database.QueryAsync<ElderlyCertStats>(
            sql, ct, parameters.ToArray());
        if (statsResult.IsFailure)
        {
            return Result.Failure<ElderlyCertStatsDto>(
                statsResult.ErrorCode!, statsResult.Message!);
        }

        var stats = statsResult.Value?.FirstOrDefault() ?? new ElderlyCertStats();
        return Result.Success(new ElderlyCertStatsDto(
            stats.Total,
            stats.Pending,
            stats.Verified,
            stats.Refused,
            stats.Failed,
            stats.Deceased,
            stats.MovedOut,
            stats.NotLocated,
            stats.Exempt,
            stats.Stopped,
            stats.Invalid,
            stats.EvidenceIssue,
            stats.StopCandidate,
            stats.CompletionRate));
    }

    // ───────────────────────────── 名单分页 / 详情 ─────────────────────────────

    public async Task<Result<ElderlyCertTaskListResponse>> GetTasksPagedAsync(
        int userId,
        long campaignId,
        int page,
        int pageSize,
        string? keyword,
        string? certStatus,
        string? evidenceStatus,
        CancellationToken ct = default)
    {
        var pageIndex = Math.Max(page, 1);
        var effectivePageSize = Math.Clamp(
            pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);

        var visible = await GetVisibleCampaignAsync(userId, campaignId, ct);
        if (visible.IsFailure)
        {
            return Result.Failure<ElderlyCertTaskListResponse>(
                visible.ErrorCode!, visible.Message!);
        }

        var conditions = new List<string> { "t.campaign_id = $1", "t.deleted_at IS NULL" };
        var parameters = new List<object?> { campaignId };

        var normalizedKeyword = Normalize(keyword);
        if (normalizedKeyword is not null)
        {
            parameters.Add(ToLikePattern(normalizedKeyword));
            var keywordIndex = parameters.Count;
            conditions.Add($"(t.name ILIKE ${keywordIndex} OR t.id_card ILIKE ${keywordIndex})");
        }

        var normalizedCertStatus = Normalize(certStatus);
        if (normalizedCertStatus is not null)
        {
            parameters.Add(normalizedCertStatus);
            conditions.Add($"t.cert_status = ${parameters.Count}");
        }

        var normalizedEvidenceStatus = Normalize(evidenceStatus);
        if (normalizedEvidenceStatus is not null)
        {
            parameters.Add(normalizedEvidenceStatus);
            conditions.Add($"t.evidence_status = ${parameters.Count}");
        }

        var scope = await BuildTaskScopeAsync(
            userId, "t", parameters.Count + 1, ct);
        if (!scope.IsEmpty)
        {
            conditions.Add(scope.Sql);
            parameters.AddRange(scope.Parameters);
        }

        var where = " WHERE " + string.Join(" AND ", conditions);

        var countResult = await database.ExecuteScalarAsync<long?>(
            $"SELECT COUNT(*) FROM {TaskTable} t{where}",
            ct,
            parameters.ToArray());
        if (countResult.IsFailure)
        {
            return Result.Failure<ElderlyCertTaskListResponse>(
                countResult.ErrorCode!, countResult.Message!);
        }

        var totalCount = countResult.Value ?? 0;
        if (totalCount == 0)
        {
            return Result.Success(new ElderlyCertTaskListResponse(
                Array.Empty<ElderlyCertTaskListItemDto>(),
                pageIndex,
                effectivePageSize,
                0,
                0));
        }

        var listParameters = new List<object?>(parameters)
        {
            effectivePageSize,
            (long)(pageIndex - 1) * effectivePageSize
        };
        var limitIndex = parameters.Count + 1;
        var offsetIndex = parameters.Count + 2;

        var listSql =
            $@"SELECT {TaskColumns}
               FROM {TaskTable} t{where}
               ORDER BY CASE t.cert_status
                            WHEN '{ElderlyCertificationConstants.CertStatusPending}' THEN 0
                            ELSE 1
                        END,
                        t.hukou_town_id NULLS LAST, t.hukou_village_id NULLS LAST, t.name, t.id_card
               LIMIT ${limitIndex} OFFSET ${offsetIndex}";

        var rowsResult = await database.QueryAsync<ElderlyCertTask>(
            listSql, ct, listParameters.ToArray());
        if (rowsResult.IsFailure)
        {
            return Result.Failure<ElderlyCertTaskListResponse>(
                rowsResult.ErrorCode!, rowsResult.Message!);
        }

        var items = (rowsResult.Value ?? [])
            .Select(MapTaskListItem)
            .ToList();

        var totalPages = (int)((totalCount + effectivePageSize - 1) / effectivePageSize);
        return Result.Success(new ElderlyCertTaskListResponse(
            items, pageIndex, effectivePageSize, totalCount, totalPages));
    }

    /// <summary>任务详情：按数据范围过滤，越权/不存在统一 NOT_FOUND。</summary>
    public async Task<Result<ElderlyCertTaskDetailDto>> GetTaskDetailAsync(
        int userId,
        long taskId,
        CancellationToken ct = default)
    {
        var scoped = await GetScopedTaskAsync(userId, taskId, ct);
        if (scoped.IsFailure)
        {
            return Result.Failure<ElderlyCertTaskDetailDto>(
                scoped.ErrorCode!, scoped.Message!);
        }

        return Result.Success(MapTaskDetail(scoped.Value));
    }

    /// <summary>
    /// 取当前用户数据范围内的任务；不存在/越权统一 NOT_FOUND。
    /// 写服务（改结果/停发决定/逐人停发）与凭证接口复用本方法。
    /// </summary>
    public async Task<Result<ElderlyCertTask>> GetScopedTaskAsync(
        int userId,
        long taskId,
        CancellationToken ct = default)
    {
        var parameters = new List<object?> { taskId };
        var scope = await BuildTaskScopeAsync(
            userId, "t", parameters.Count + 1, ct);

        var conditions = new List<string> { "t.id = $1", "t.deleted_at IS NULL" };
        if (!scope.IsEmpty)
        {
            conditions.Add(scope.Sql);
            parameters.AddRange(scope.Parameters);
        }

        var sql =
            $"SELECT {TaskColumns} FROM {TaskTable} t " +
            "WHERE " + string.Join(" AND ", conditions);

        var result = await database.QuerySingleAsync<ElderlyCertTask>(
            sql, ct, parameters.ToArray());
        if (result.IsFailure)
        {
            return Result.Failure<ElderlyCertTask>(result.ErrorCode!, result.Message!);
        }

        if (result.Value is null)
        {
            return Result.Failure<ElderlyCertTask>(
                ApiErrorCodes.NotFound, "认证任务不存在或无权查看");
        }

        return Result.Success(result.Value);
    }

    // ───────────────────────────── 凭证 ─────────────────────────────

    /// <summary>凭证列表：先做任务级数据范围校验，再调 Core（不含图片二进制）。</summary>
    public async Task<Result<IReadOnlyList<ElderlyCertEvidenceDto>>> GetEvidenceAsync(
        int userId,
        long taskId,
        CancellationToken ct = default)
    {
        var scoped = await GetScopedTaskAsync(userId, taskId, ct);
        if (scoped.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ElderlyCertEvidenceDto>>(
                scoped.ErrorCode!, scoped.Message!);
        }

        var evidenceResult = await certificationService.GetEvidenceAsync(taskId, false, ct);
        if (evidenceResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<ElderlyCertEvidenceDto>>(
                evidenceResult.ErrorCode!, evidenceResult.Message!);
        }

        var items = (evidenceResult.Value ?? [])
            .Select(evidence => new ElderlyCertEvidenceDto(
                evidence.Id,
                evidence.TaskId,
                evidence.Seq,
                evidence.EvidenceType ?? string.Empty,
                evidence.EvidenceTypeName,
                evidence.OriginalName ?? string.Empty,
                evidence.Display,
                evidence.ImageFormat ?? string.Empty,
                evidence.FileSize,
                evidence.Sha256 ?? string.Empty,
                evidence.MatchStatus ?? string.Empty,
                evidence.MatchMethod ?? string.Empty))
            .ToList();

        return Result.Success<IReadOnlyList<ElderlyCertEvidenceDto>>(items);
    }

    /// <summary>
    /// 凭证图片内容：evidence JOIN tasks，按 tasks 户籍范围授权；未挂接（task_id 为空）的图片
    /// 不属于任何个人，查看接口不返回（由后续人工挂接流程处理）。
    /// </summary>
    public async Task<Result<ElderlyCertEvidenceContentDto>> GetEvidenceContentAsync(
        int userId,
        long evidenceId,
        CancellationToken ct = default)
    {
        var parameters = new List<object?> { evidenceId };
        var scope = await BuildTaskScopeAsync(
            userId, "t", parameters.Count + 1, ct);

        var conditions = new List<string>
        {
            "e.id = $1",
            "e.deleted_at IS NULL",
            "e.task_id IS NOT NULL",
            "t.deleted_at IS NULL"
        };
        if (!scope.IsEmpty)
        {
            conditions.Add(scope.Sql);
            parameters.AddRange(scope.Parameters);
        }

        var sql =
            $@"SELECT e.photo_data, e.image_format, e.display_name, e.original_name
               FROM {EvidenceTable} e
               JOIN {TaskTable} t ON t.id = e.task_id
               WHERE {string.Join(" AND ", conditions)}
               LIMIT 1";

        var result = await database.QuerySingleAsync<ElderlyCertEvidenceContentRow>(
            sql, ct, parameters.ToArray());
        if (result.IsFailure)
        {
            return Result.Failure<ElderlyCertEvidenceContentDto>(
                result.ErrorCode!, result.Message!);
        }

        var row = result.Value;
        if (row?.PhotoData is not { Length: > 0 })
        {
            return Result.Failure<ElderlyCertEvidenceContentDto>(
                ApiErrorCodes.NotFound, "凭证图片不存在或无权查看");
        }

        var format = NormalizeImageFormat(row.ImageFormat);
        var fileName = !string.IsNullOrWhiteSpace(row.DisplayName)
            ? row.DisplayName!.Trim()
            : !string.IsNullOrWhiteSpace(row.OriginalName)
                ? Path.GetFileName(row.OriginalName)!
                : $"evidence-{evidenceId}.{format}";

        return Result.Success(new ElderlyCertEvidenceContentDto(
            row.PhotoData, ToImageContentType(format), fileName));
    }

    // ───────────────────────────── 停发预览 ─────────────────────────────

    /// <summary>
    /// 停发预览分页。Core GetStopPreviewPagedAsync 不接受数据范围，若先调 Core 再内存过滤会把
    /// 其他乡镇的人带进总数/分页，因此这里按 Core 同一口径（候选状态、排序）重写 scoped SQL。
    /// </summary>
    public async Task<Result<ElderlyCertStopPreviewResponse>> GetStopPreviewPagedAsync(
        int userId,
        long campaignId,
        int page,
        int pageSize,
        string? keyword,
        string? certStatus,
        CancellationToken ct = default)
    {
        var pageIndex = Math.Max(page, 1);
        var effectivePageSize = Math.Clamp(
            pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);

        var visible = await GetVisibleCampaignAsync(userId, campaignId, ct);
        if (visible.IsFailure)
        {
            return Result.Failure<ElderlyCertStopPreviewResponse>(
                visible.ErrorCode!, visible.Message!);
        }

        var campaign = visible.Value;
        var conditions = new List<string>
        {
            "t.campaign_id = $1",
            "t.deleted_at IS NULL",
            "t.cert_status = ANY($2)"
        };
        var parameters = new List<object?> { campaignId, StopCandidateStatuses };

        var normalizedKeyword = Normalize(keyword);
        if (normalizedKeyword is not null)
        {
            parameters.Add(ToLikePattern(normalizedKeyword));
            var keywordIndex = parameters.Count;
            conditions.Add($"(t.name ILIKE ${keywordIndex} OR t.id_card ILIKE ${keywordIndex})");
        }

        var normalizedStatus = Normalize(certStatus);
        if (normalizedStatus is not null)
        {
            if (!StopCandidateStatuses.Contains(normalizedStatus, StringComparer.Ordinal))
            {
                return Result.Failure<ElderlyCertStopPreviewResponse>(
                    ErrorCodes.VALIDATION_FAILED, "认证状态不在停发预览范围内");
            }

            parameters.Add(normalizedStatus);
            conditions.Add($"t.cert_status = ${parameters.Count}");
        }

        var scope = await BuildTaskScopeAsync(
            userId, "t", parameters.Count + 1, ct);
        if (!scope.IsEmpty)
        {
            conditions.Add(scope.Sql);
            parameters.AddRange(scope.Parameters);
        }

        var where = " WHERE " + string.Join(" AND ", conditions);

        var countResult = await database.ExecuteScalarAsync<long?>(
            $"SELECT COUNT(*) FROM {TaskTable} t{where}",
            ct,
            parameters.ToArray());
        if (countResult.IsFailure)
        {
            return Result.Failure<ElderlyCertStopPreviewResponse>(
                countResult.ErrorCode!, countResult.Message!);
        }

        var totalCount = countResult.Value ?? 0;
        if (totalCount == 0)
        {
            return Result.Success(new ElderlyCertStopPreviewResponse(
                Array.Empty<ElderlyCertStopPreviewItemDto>(),
                pageIndex,
                effectivePageSize,
                0,
                0));
        }

        var listParameters = new List<object?>(parameters)
        {
            effectivePageSize,
            (long)(pageIndex - 1) * effectivePageSize
        };
        var limitIndex = parameters.Count + 1;
        var offsetIndex = parameters.Count + 2;

        var listSql =
            $@"SELECT {TaskColumns}
               FROM {TaskTable} t{where}
               ORDER BY t.hukou_village NULLS LAST, t.hukou_town_id NULLS LAST, t.name, t.id_card
               LIMIT ${limitIndex} OFFSET ${offsetIndex}";

        var rowsResult = await database.QueryAsync<ElderlyCertTask>(
            listSql, ct, listParameters.ToArray());
        if (rowsResult.IsFailure)
        {
            return Result.Failure<ElderlyCertStopPreviewResponse>(
                rowsResult.ErrorCode!, rowsResult.Message!);
        }

        var items = (rowsResult.Value ?? [])
            .Select(row => MapStopPreviewItem(
                row, campaign.PeriodEnd, campaign.DeadlineStopOffset))
            .ToList();

        var totalPages = (int)((totalCount + effectivePageSize - 1) / effectivePageSize);
        return Result.Success(new ElderlyCertStopPreviewResponse(
            items, pageIndex, effectivePageSize, totalCount, totalPages));
    }

    // ───────────────────────────── 映射 ─────────────────────────────

    private static ElderlyCertCampaignDto MapCampaign(ElderlyCertCampaign campaign)
        => new(
            campaign.Id,
            campaign.CampaignNo ?? string.Empty,
            campaign.Year,
            campaign.Round,
            campaign.Name ?? string.Empty,
            campaign.DisplayName,
            DateOnly.FromDateTime(campaign.PeriodStart),
            DateOnly.FromDateTime(campaign.PeriodEnd),
            ToDateOnly(campaign.ValidUntil),
            campaign.Status ?? string.Empty,
            campaign.StatusName,
            campaign.RequireEvidence,
            campaign.DeadlineStopOffset,
            campaign.RosterGeneratedAt,
            campaign.ClosedAt,
            campaign.Remark ?? string.Empty);

    private static ElderlyCertTaskListItemDto MapTaskListItem(ElderlyCertTask task)
        => new(
            task.Id,
            task.CampaignId,
            task.IdCard ?? string.Empty,
            task.Name ?? string.Empty,
            task.Gender ?? string.Empty,
            ToDateOnly(task.BirthDate),
            task.AgeSnapshot,
            task.Category ?? string.Empty,
            ElderlyBenefitConstants.GetCategoryName(task.Category),
            task.IssueAmount,
            task.ApplicationId,
            task.ApplicationNo ?? string.Empty,
            task.SourceKind ?? string.Empty,
            task.HukouVillage ?? string.Empty,
            task.HukouAddress ?? string.Empty,
            task.FamilyAddress ?? string.Empty,
            DataMasker.MaskPhone(task.Phone ?? string.Empty),
            task.CertStatus ?? string.Empty,
            task.CertStatusName,
            task.VerifyMethod ?? string.Empty,
            task.VerifyMethodName,
            ToDateOnly(task.VerifiedAt),
            task.VerifiedBy ?? string.Empty,
            task.ResultNote ?? string.Empty,
            ToDateOnly(task.DeathDate),
            task.ExpectedEvidenceCount,
            task.EvidenceCount,
            task.EvidenceStatus ?? string.Empty,
            task.EvidenceStatusName,
            task.StopDecision ?? string.Empty,
            task.StopDecisionName,
            ToDateOnly(task.FollowUpDate));

    private static ElderlyCertTaskDetailDto MapTaskDetail(ElderlyCertTask task)
        => new(
            task.Id,
            task.CampaignId,
            task.IdCard ?? string.Empty,
            task.Name ?? string.Empty,
            task.Gender ?? string.Empty,
            ToDateOnly(task.BirthDate),
            task.AgeSnapshot,
            task.Category ?? string.Empty,
            ElderlyBenefitConstants.GetCategoryName(task.Category),
            task.IdentityFlag ?? string.Empty,
            task.IssueAmount,
            task.ApplicationId,
            task.ApplicationNo ?? string.Empty,
            task.SourceKind ?? string.Empty,
            task.HukouCityId,
            task.HukouCountyId,
            task.HukouTownId,
            task.HukouVillageId,
            task.HukouVillage ?? string.Empty,
            task.HukouAddress ?? string.Empty,
            task.FamilyCityId,
            task.FamilyCountyId,
            task.FamilyTownId,
            task.FamilyVillageId,
            task.FamilyAddress ?? string.Empty,
            task.Phone ?? string.Empty,
            task.BankName ?? string.Empty,
            task.BankAccount ?? string.Empty,
            task.AgentName ?? string.Empty,
            task.AgentRelation ?? string.Empty,
            task.SnapshotAt,
            task.CertStatus ?? string.Empty,
            task.CertStatusName,
            task.VerifyMethod ?? string.Empty,
            task.VerifyMethodName,
            ToDateOnly(task.VerifiedAt),
            task.VerifiedBy ?? string.Empty,
            task.VerifierOrg ?? string.Empty,
            task.VerifierPhone ?? string.Empty,
            task.ResultNote ?? string.Empty,
            ToDateOnly(task.DeathDate),
            task.ExpectedEvidenceCount,
            task.EvidenceCount,
            task.EvidenceStatus ?? string.Empty,
            task.EvidenceStatusName,
            task.StopDecision ?? string.Empty,
            task.StopDecisionName,
            task.StopReason ?? string.Empty,
            ElderlyBenefitConstants.GetStopReasonName(task.StopReason),
            task.StopNote ?? string.Empty,
            ToDateOnly(task.FollowUpDate),
            ToDateOnly(task.DueStopDate),
            ToDateOnly(task.ActualStopDate),
            task.StopApplicationId,
            task.StoppedAt,
            task.SourceBatchNo ?? string.Empty,
            task.SourceFile ?? string.Empty,
            task.SourceRow,
            task.SourceSubmittedAt,
            task.LastImportedAt,
            task.CreatedAt,
            task.UpdatedAt);

    private static ElderlyCertStopPreviewItemDto MapStopPreviewItem(
        ElderlyCertTask task,
        DateTime periodEnd,
        int deadlineStopOffset)
    {
        // 与桌面端共用同一套原因/应停日期/备注口径：
        // NotLocated 也归到 CERTIFICATION_FAIL（现有取消备案表五项里没有“查找不到本人”），
        // 备注写明“无法联系/查找不到本人”，避免 Web 与桌面出现两套停发原因。
        var reason = ElderlyCertificationStopPreviewHelper.GetSuggestedStopReason(task.CertStatus)
                     ?? ElderlyBenefitConstants.StopReasonCertificationFail;
        var note = ElderlyCertificationStopPreviewHelper.BuildPresetNote(task.CertStatus) ?? string.Empty;
        var suggestedDue = task.DueStopDate
            ?? ElderlyCertificationStopPreviewHelper.ComputeSuggestedDueStopDate(
                task.CertStatus, task.DeathDate, periodEnd, deadlineStopOffset);

        return new ElderlyCertStopPreviewItemDto(
            task.Id,
            task.CampaignId,
            task.IdCard ?? string.Empty,
            task.Name ?? string.Empty,
            task.Gender ?? string.Empty,
            ToDateOnly(task.BirthDate),
            task.AgeSnapshot,
            task.Category ?? string.Empty,
            ElderlyBenefitConstants.GetCategoryName(task.Category),
            task.IssueAmount,
            task.SourceKind ?? string.Empty,
            task.HukouVillage ?? string.Empty,
            task.HukouAddress ?? string.Empty,
            task.FamilyAddress ?? string.Empty,
            DataMasker.MaskPhone(task.Phone ?? string.Empty),
            task.CertStatus ?? string.Empty,
            task.CertStatusName,
            task.VerifyMethod ?? string.Empty,
            task.VerifyMethodName,
            ToDateOnly(task.VerifiedAt),
            task.VerifiedBy ?? string.Empty,
            task.ResultNote ?? string.Empty,
            ToDateOnly(task.DeathDate),
            task.ExpectedEvidenceCount,
            task.EvidenceCount,
            task.EvidenceStatus ?? string.Empty,
            task.EvidenceStatusName,
            reason,
            ElderlyBenefitConstants.GetStopReasonName(reason),
            note,
            DateOnly.FromDateTime(suggestedDue),
            task.StopDecision ?? string.Empty,
            task.StopDecisionName,
            ToDateOnly(task.FollowUpDate),
            task.ApplicationId,
            task.ApplicationNo ?? string.Empty);
    }

    // ───────────────────────────── 通用 ─────────────────────────────

    /// <summary>
    /// 认证任务表没有 created_by 列，DataScopeFilterBuilder 在 SELF 口径下会生成 t.created_by，
    /// 直接使用会导致 SQL 报错。SELF 是默认最小范围，认证域按拒绝处理（本人创建的空批次仍可见）。
    /// </summary>
    public async Task<DataScopeClause> BuildTaskScopeAsync(
        int userId,
        string alias,
        int startIndex,
        CancellationToken ct)
    {
        var effective = await dataScope.GetEffectiveScopeAsync(userId, ct);
        if (string.Equals(effective.Scope, DataScopeConstants.SELF, StringComparison.OrdinalIgnoreCase))
        {
            return new DataScopeClause("1 = 0", Array.Empty<object?>());
        }

        return await scopeBuilder.BuildAsync(
            userId, DataScopeTarget.ElderlyApplications, alias, startIndex, ct);
    }

    private async Task<string?> GetUsernameAsync(int userId, CancellationToken ct)
    {
        var result = await database.ExecuteScalarAsync<string?>(
            "SELECT username FROM nc_sys_users WHERE id = $1 LIMIT 1",
            ct,
            userId);
        if (result.IsFailure)
        {
            // 用户名只用于“本人创建批次”的兜底可见性；失败时按不可见处理，不阻断列表。
            logger.Error($"读取高龄认证批次可见性用户名失败: UserId={userId} {result.ErrorCode}");
            return null;
        }

        return result.Value?.Trim();
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>转义 LIKE 通配符，关键字按字面量匹配（PostgreSQL 默认转义字符为反斜杠）。</summary>
    private static string ToLikePattern(string keyword)
    {
        var escaped = keyword
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static DateOnly? ToDateOnly(DateTime? value)
        => value is { Year: >= 1900 } date ? DateOnly.FromDateTime(date) : null;

    private static string NormalizeImageFormat(string? format)
    {
        var value = (format ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        return value switch
        {
            "jpeg" => "jpg",
            "png" or "webp" or "bmp" or "gif" or "jpg" => value,
            _ => "jpg"
        };
    }

    private static string ToImageContentType(string format) => format switch
    {
        "png" => "image/png",
        "webp" => "image/webp",
        "bmp" => "image/bmp",
        "gif" => "image/gif",
        _ => "image/jpeg"
    };
}

/// <summary>Core 反射映射需要 public 可写属性与公共无参构造函数。</summary>
public sealed class ElderlyCertCampaignIdRow
{
    public long CampaignId { get; set; }
}

public sealed class ElderlyCertCampaignTaskCountRow
{
    public long CampaignId { get; set; }
    public int TaskCount { get; set; }
}

public sealed class ElderlyCertEvidenceContentRow
{
    public byte[]? PhotoData { get; set; }
    public string? ImageFormat { get; set; }
    public string? DisplayName { get; set; }
    public string? OriginalName { get; set; }
}
