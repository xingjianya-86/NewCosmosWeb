using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services;

/// <summary>
/// 「业务申请工作流」只读查询：五个阶段 Tab 的计数与分页列表。
/// 口径对齐 legacy ApplicationWorkflowViewModel：
/// - 0/1 资产核查只取户主（is_head=true）；
/// - 2 草稿 = Draft 且 current_step &lt; PENDING_ARCHIVE；
/// - 3 已建档未提交 = Draft 且 current_step = PENDING_ARCHIVE；
/// - 4 已完结 = current_step = ARCHIVED 且 status 属于已归档状态集合。
/// 所有查询叠加 DataScopeFilterBuilder。
/// </summary>
public sealed class ApplicationWorkflowQueryService(
    IDatabaseService database,
    DataScopeFilterBuilder scopeBuilder,
    ILoggerService logger)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private static readonly string[] ArchivedStatuses =
    [
        ApplicationStatusCodes.APPROVED,
        ApplicationStatusCodes.COMPLETED,
        ApplicationStatusCodes.STOPPED
    ];

    public async Task<WorkflowSummaryDto> GetSummaryAsync(
        int userId,
        bool includeAssetCounts,
        CancellationToken ct = default)
    {
        var assetSubmittedTask = includeAssetCounts
            ? CountAssetAsync(userId, AssetCheckStatusConstants.SUBMITTED, ct)
            : Task.FromResult<int?>(null);
        var assetReportTask = includeAssetCounts
            ? CountAssetAsync(userId, AssetCheckStatusConstants.VERIFIED, ct)
            : Task.FromResult<int?>(null);
        var draftsTask = CountDraftAsync(userId, ct);
        var builtTask = CountArchiveBuiltAsync(userId, ct);
        var archivedTask = CountArchivedAsync(userId, ct);

        await Task.WhenAll(assetSubmittedTask, assetReportTask, draftsTask, builtTask, archivedTask);

        return new WorkflowSummaryDto(
            includeAssetCounts,
            await assetSubmittedTask,
            await assetReportTask,
            await draftsTask,
            await builtTask,
            await archivedTask);
    }

    public async Task<Result<WorkflowListResponse>> ListAsync(
        int userId,
        int tab,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken ct = default)
    {
        if (tab is < 0 or > 4)
        {
            return Result.Failure<WorkflowListResponse>(
                ErrorCodes.VALIDATION_FAILED, "工作流阶段无效");
        }

        var pageIndex = Math.Max(page, 1);
        var effectivePageSize = Math.Clamp(
            pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);

        return tab switch
        {
            0 => await ListAssetAsync(userId, AssetCheckStatusConstants.SUBMITTED, tab,
                pageIndex, effectivePageSize, keyword, ct),
            1 => await ListAssetAsync(userId, AssetCheckStatusConstants.VERIFIED, tab,
                pageIndex, effectivePageSize, keyword, ct),
            _ => await ListApplicationAsync(userId, tab, pageIndex, effectivePageSize, keyword, ct)
        };
    }

    private async Task<int?> CountAssetAsync(int userId, string status, CancellationToken ct)
    {
        try
        {
            var parameters = new List<object?> { status };
            var scope = await scopeBuilder.BuildAsync(
                userId, DataScopeTarget.AssetChecks, "v", parameters.Count + 1, ct);
            var sql =
                "SELECT COUNT(*) FROM nc_biz_asset_checks v " +
                "WHERE v.deleted_at IS NULL AND v.is_head = TRUE AND v.status = $1";
            if (!scope.IsEmpty)
            {
                sql += " AND " + scope.Sql;
                parameters.AddRange(scope.Parameters);
            }

            var result = await database.ExecuteScalarAsync<long?>(sql, ct, parameters.ToArray());
            return result.IsSuccess ? (int)(result.Value ?? 0) : null;
        }
        catch (Exception ex)
        {
            logger.Warn($"业务申请工作流-资产核查计数失败: {ex.Message}");
            return null;
        }
    }

    private async Task<int?> CountDraftAsync(int userId, CancellationToken ct)
    {
        try
        {
            var parameters = new List<object?>
            {
                ApplicationStatusCodes.DRAFT,
                WorkflowSteps.PENDING_ARCHIVE
            };
            var scope = await scopeBuilder.BuildAsync(
                userId, DataScopeTarget.Applications, "a", parameters.Count + 1, ct);
            var sql =
                "SELECT COUNT(*) FROM nc_biz_applications a " +
                "WHERE a.deleted_at IS NULL AND a.application_type IS DISTINCT FROM '" + ApplicationTypeConstants.REMOTE_ASSISTANCE + "' AND a.status = $1 AND a.current_step < $2";
            if (!scope.IsEmpty)
            {
                sql += " AND " + scope.Sql;
                parameters.AddRange(scope.Parameters);
            }

            var result = await database.ExecuteScalarAsync<long?>(sql, ct, parameters.ToArray());
            return result.IsSuccess ? (int)(result.Value ?? 0) : null;
        }
        catch (Exception ex)
        {
            logger.Warn($"业务申请工作流-草稿计数失败: {ex.Message}");
            return null;
        }
    }

    private async Task<int?> CountArchiveBuiltAsync(int userId, CancellationToken ct)
    {
        try
        {
            var parameters = new List<object?>
            {
                ApplicationStatusCodes.DRAFT,
                WorkflowSteps.PENDING_ARCHIVE
            };
            var scope = await scopeBuilder.BuildAsync(
                userId, DataScopeTarget.Applications, "a", parameters.Count + 1, ct);
            var sql =
                "SELECT COUNT(*) FROM nc_biz_applications a " +
                "WHERE a.deleted_at IS NULL AND a.application_type IS DISTINCT FROM '" + ApplicationTypeConstants.REMOTE_ASSISTANCE + "' AND a.status = $1 AND a.current_step = $2";
            if (!scope.IsEmpty)
            {
                sql += " AND " + scope.Sql;
                parameters.AddRange(scope.Parameters);
            }

            var result = await database.ExecuteScalarAsync<long?>(sql, ct, parameters.ToArray());
            return result.IsSuccess ? (int)(result.Value ?? 0) : null;
        }
        catch (Exception ex)
        {
            logger.Warn($"业务申请工作流-已建档未提交计数失败: {ex.Message}");
            return null;
        }
    }

    private async Task<int?> CountArchivedAsync(int userId, CancellationToken ct)
    {
        try
        {
            var parameters = new List<object?>
            {
                WorkflowSteps.ARCHIVED,
                ArchivedStatuses
            };
            var scope = await scopeBuilder.BuildAsync(
                userId, DataScopeTarget.Applications, "a", parameters.Count + 1, ct);
            var sql =
                "SELECT COUNT(*) FROM nc_biz_applications a " +
                "WHERE a.deleted_at IS NULL AND a.application_type IS DISTINCT FROM '" + ApplicationTypeConstants.REMOTE_ASSISTANCE + "' AND a.current_step = $1 AND a.status = ANY($2)";
            if (!scope.IsEmpty)
            {
                sql += " AND " + scope.Sql;
                parameters.AddRange(scope.Parameters);
            }

            var result = await database.ExecuteScalarAsync<long?>(sql, ct, parameters.ToArray());
            return result.IsSuccess ? (int)(result.Value ?? 0) : null;
        }
        catch (Exception ex)
        {
            logger.Warn($"业务申请工作流-已完结计数失败: {ex.Message}");
            return null;
        }
    }

    private async Task<Result<WorkflowListResponse>> ListAssetAsync(
        int userId,
        string status,
        int tab,
        int pageIndex,
        int pageSize,
        string? keyword,
        CancellationToken ct)
    {
        var conditions = new List<string>
        {
            "v.deleted_at IS NULL",
            "v.is_head = TRUE",
            "v.status = $1"
        };
        var parameters = new List<object?> { status };

        var normalizedKeyword = Normalize(keyword);
        if (normalizedKeyword is not null)
        {
            parameters.Add(ToLikePattern(normalizedKeyword));
            var index = parameters.Count;
            conditions.Add(
                $"(v.applicant_name ILIKE ${index} OR v.applicant_id_card ILIKE ${index} " +
                $"OR v.head_id_card ILIKE ${index})");
        }

        var scope = await scopeBuilder.BuildAsync(
            userId, DataScopeTarget.AssetChecks, "v", parameters.Count + 1, ct);
        if (!scope.IsEmpty)
        {
            conditions.Add(scope.Sql);
            parameters.AddRange(scope.Parameters);
        }

        var where = " WHERE " + string.Join(" AND ", conditions);
        var countResult = await database.ExecuteScalarAsync<long?>(
            "SELECT COUNT(*) FROM nc_biz_asset_checks v" + where,
            ct,
            parameters.ToArray());
        if (countResult.IsFailure)
        {
            return Result.Failure<WorkflowListResponse>(
                countResult.ErrorCode!, countResult.Message!);
        }

        var totalCount = countResult.Value ?? 0;
        if (totalCount == 0)
        {
            return Result.Success(new WorkflowListResponse(
                [], tab, pageIndex, pageSize, 0, 0));
        }

        var listParameters = new List<object?>(parameters)
        {
            pageSize,
            (long)(pageIndex - 1) * pageSize
        };
        var limitIndex = parameters.Count + 1;
        var offsetIndex = parameters.Count + 2;

        var sql =
            $@"SELECT v.id, v.applicant_name, v.applicant_id_card, v.relationship,
                      COALESCE(rv.village_name, v.community) AS community,
                      v.family_address, v.application_date, v.created_at, v.status
               FROM nc_biz_asset_checks v
               LEFT JOIN nc_regions_villages rv ON rv.village_code = v.community
               {where}
               ORDER BY COALESCE(v.created_at, v.application_date::timestamp) DESC, v.id DESC
               LIMIT ${limitIndex} OFFSET ${offsetIndex}";

        var rowsResult = await database.QueryAsync<WorkflowAssetRow>(
            sql, ct, listParameters.ToArray());
        if (rowsResult.IsFailure)
        {
            return Result.Failure<WorkflowListResponse>(
                rowsResult.ErrorCode!, rowsResult.Message!);
        }

        var items = new List<WorkflowItemDto>();
        foreach (var row in rowsResult.Value ?? [])
        {
            var isSubmitted = status == AssetCheckStatusConstants.SUBMITTED;
            items.Add(new WorkflowItemDto(
                $"asset-{row.Id}",
                "asset",
                row.Id,
                row.ApplicantName ?? string.Empty,
                DataMasker.MaskIdCard(row.ApplicantIdCard ?? string.Empty),
                row.Status ?? status,
                isSubmitted ? "已提交" : "有报告未建档",
                isSubmitted ? "info" : "warning",
                RelationshipText(row.Relationship),
                string.Empty,
                row.FamilyAddress ?? string.Empty,
                row.Community ?? string.Empty,
                row.CreatedAt ?? row.ApplicationDate,
                false,
                false,
                null,
                false,
                null,
                string.Empty,
                string.Empty,
                row.Status ?? string.Empty,
                ApplicationQueryService.ToStatusText(row.Status),
                null));
        }

        var totalPages = (int)((totalCount + pageSize - 1) / pageSize);
        return Result.Success(new WorkflowListResponse(
            items, tab, pageIndex, pageSize, totalCount, totalPages));
    }

    private async Task<Result<WorkflowListResponse>> ListApplicationAsync(
        int userId,
        int tab,
        int pageIndex,
        int pageSize,
        string? keyword,
        CancellationToken ct)
    {
        var conditions = new List<string>
        {
            "a.deleted_at IS NULL",
            $"a.application_type IS DISTINCT FROM '{ApplicationTypeConstants.REMOTE_ASSISTANCE}'"
        };
        var parameters = new List<object?>();

        switch (tab)
        {
            case 2:
                parameters.Add(ApplicationStatusCodes.DRAFT);
                parameters.Add(WorkflowSteps.PENDING_ARCHIVE);
                conditions.Add("a.status = $1");
                conditions.Add("a.current_step < $2");
                break;
            case 3:
                parameters.Add(ApplicationStatusCodes.DRAFT);
                parameters.Add(WorkflowSteps.PENDING_ARCHIVE);
                conditions.Add("a.status = $1");
                conditions.Add("a.current_step = $2");
                break;
            case 4:
                parameters.Add(WorkflowSteps.ARCHIVED);
                parameters.Add(ArchivedStatuses);
                conditions.Add("a.current_step = $1");
                conditions.Add("a.status = ANY($2)");
                break;
            default:
                return Result.Failure<WorkflowListResponse>(
                    ErrorCodes.VALIDATION_FAILED, "工作流阶段无效");
        }

        var normalizedKeyword = Normalize(keyword);
        if (normalizedKeyword is not null)
        {
            if (normalizedKeyword.Length == 18)
            {
                parameters.Add(ToLikePattern(normalizedKeyword));
                var nameIndex = parameters.Count;
                parameters.Add(normalizedKeyword);
                var cardIndex = parameters.Count;
                conditions.Add(
                    $"(a.applicant_name ILIKE ${nameIndex} OR a.applicant_id_card = ${cardIndex})");
            }
            else
            {
                parameters.Add(ToLikePattern(normalizedKeyword));
                var index = parameters.Count;
                conditions.Add(
                    $"(a.applicant_name ILIKE ${index} OR a.applicant_id_card ILIKE ${index} " +
                    $"OR a.application_no ILIKE ${index})");
            }
        }

        var scope = await scopeBuilder.BuildAsync(
            userId, DataScopeTarget.Applications, "a", parameters.Count + 1, ct);
        if (!scope.IsEmpty)
        {
            conditions.Add(scope.Sql);
            parameters.AddRange(scope.Parameters);
        }

        var where = " WHERE " + string.Join(" AND ", conditions);
        var countResult = await database.ExecuteScalarAsync<long?>(
            "SELECT COUNT(*) FROM nc_biz_applications a" + where,
            ct,
            parameters.ToArray());
        if (countResult.IsFailure)
        {
            return Result.Failure<WorkflowListResponse>(
                countResult.ErrorCode!, countResult.Message!);
        }

        var totalCount = countResult.Value ?? 0;
        if (totalCount == 0)
        {
            return Result.Success(new WorkflowListResponse(
                [], tab, pageIndex, pageSize, 0, 0));
        }

        var listParameters = new List<object?>(parameters)
        {
            pageSize,
            (long)(pageIndex - 1) * pageSize
        };
        var limitIndex = parameters.Count + 1;
        var offsetIndex = parameters.Count + 2;

        var sql =
            $@"SELECT a.id, a.application_no, a.applicant_name, a.applicant_id_card,
                      a.is_single_rescue, a.status, a.created_at, a.updated_at,
                      a.classification_result, a.household_monthly_guarantee_amount
               FROM nc_biz_applications a
               {where}
               ORDER BY COALESCE(a.updated_at, a.created_at) DESC, a.id DESC
               LIMIT ${limitIndex} OFFSET ${offsetIndex}";

        var rowsResult = await database.QueryAsync<WorkflowApplicationRow>(
            sql, ct, listParameters.ToArray());
        if (rowsResult.IsFailure)
        {
            return Result.Failure<WorkflowListResponse>(
                rowsResult.ErrorCode!, rowsResult.Message!);
        }

        var rows = rowsResult.Value ?? [];
        var graceMap = await LoadGraceMapAsync(rows.Select(x => x.Id).ToArray(), ct);
        var statusText = tab switch
        {
            2 => "草稿",
            3 => "已建档",
            _ => "已完结"
        };
        var statusTone = tab switch
        {
            2 => "danger",
            3 => "warning",
            _ => "success"
        };

        var items = new List<WorkflowItemDto>(rows.Count);
        foreach (var row in rows)
        {
            graceMap.TryGetValue(row.Id, out var graceEnd);
            items.Add(new WorkflowItemDto(
                $"application-{row.Id}",
                "application",
                row.Id,
                row.ApplicantName ?? string.Empty,
                DataMasker.MaskIdCard(row.ApplicantIdCard ?? string.Empty),
                row.Status ?? string.Empty,
                statusText,
                statusTone,
                string.Empty,
                row.ApplicationNo ?? string.Empty,
                string.Empty,
                string.Empty,
                row.CreatedAt,
                row.IsSingleRescue,
                graceEnd != default,
                graceEnd == default ? null : $"渐退期至 {graceEnd:yyyy-MM-dd}",
                false,
                null,
                row.ClassificationResult ?? string.Empty,
                string.IsNullOrEmpty(row.ClassificationResult)
                    ? string.Empty
                    : ClassificationConstants.ConvertFromCode(row.ClassificationResult),
                row.Status ?? string.Empty,
                ApplicationQueryService.ToStatusText(row.Status),
                row.HouseholdMonthlyGuaranteeAmount));
        }

        var totalPages = (int)((totalCount + pageSize - 1) / pageSize);
        return Result.Success(new WorkflowListResponse(
            items, tab, pageIndex, pageSize, totalCount, totalPages));
    }

    private async Task<Dictionary<long, DateTime>> LoadGraceMapAsync(
        long[] applicationIds,
        CancellationToken ct)
    {
        if (applicationIds.Length == 0)
        {
            return [];
        }

        try
        {
            var result = await database.QueryAsync<WorkflowGraceRow>(
                @"SELECT gp.application_id, gp.end_date
                  FROM nc_biz_grace_periods gp
                  WHERE gp.application_id = ANY($1)
                    AND gp.is_active = TRUE
                    AND gp.deleted_at IS NULL
                    AND gp.end_date IS NOT NULL",
                ct,
                applicationIds);
            if (result.IsFailure)
            {
                logger.Warn($"业务申请工作流-渐退期角标查询失败: {result.ErrorCode} {result.Message}");
                return [];
            }

            return (result.Value ?? [])
                .GroupBy(x => x.ApplicationId)
                .ToDictionary(x => x.Key, x => x.First().EndDate);
        }
        catch (Exception ex)
        {
            logger.Warn($"业务申请工作流-渐退期角标查询异常: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// 资产核查关系列存英文代码，旧版通过字典转中文；Web 端补齐常见值，
    /// 未知值回退 Core 字典显示。
    /// </summary>
    private static string RelationshipText(string? code) => code switch
    {
        "Head" => "本人/户主",
        "Spouse" => "配偶",
        "Son" => "子",
        "Daughter" => "女",
        "Parent" => "父母",
        "Father" => "父",
        "Mother" => "母",
        _ => DictDisplayHelper.GetFamilyRelationshipDisplay(code)
    };

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ToLikePattern(string keyword)
    {
        var escaped = keyword
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}

public sealed class WorkflowAssetRow
{
    public long Id { get; set; }
    public string? ApplicantName { get; set; }
    public string? ApplicantIdCard { get; set; }
    public string? Relationship { get; set; }
    public string? Community { get; set; }
    public string? FamilyAddress { get; set; }
    public DateTime? ApplicationDate { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? Status { get; set; }
}

public sealed class WorkflowApplicationRow
{
    public long Id { get; set; }
    public string? ApplicationNo { get; set; }
    public string? ApplicantName { get; set; }
    public string? ApplicantIdCard { get; set; }
    public bool IsSingleRescue { get; set; }
    public string? Status { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? ClassificationResult { get; set; }
    public decimal? HouseholdMonthlyGuaranteeAmount { get; set; }
}

public sealed class WorkflowGraceRow
{
    public long ApplicationId { get; set; }
    public DateTime EndDate { get; set; }
}
