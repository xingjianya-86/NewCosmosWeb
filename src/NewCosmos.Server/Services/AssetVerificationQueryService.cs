using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.Reporting;

namespace NewCosmos.Server.Services;

/// <summary>
/// 家庭经济状况核对只读查询：
/// - 首页统计复用 Core <see cref="IStatisticsService"/>，不在 Server 重抄口径；
/// - 列表/详情/月度审核全部使用 $1..$n 参数与显式列；
/// - 所有查询叠加 <see cref="DataScopeFilterBuilder"/> 的村（community）数据范围。
/// </summary>
public sealed class AssetVerificationQueryService(
    IDatabaseService database,
    IStatisticsService statistics,
    DataScopeFilterBuilder dataScope)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private const string ListColumns =
        @"v.id, v.batch_id, v.applicant_name, v.applicant_id_card, v.relationship,
          v.is_head, v.head_id_card, v.family_address, v.community, v.application_date,
          v.status, v.contact_phone";

    /// <summary>
    /// 首页统计。Core 查询失败时返回四个 null，由前端显示“—”，避免整页 500。
    /// </summary>
    public async Task<AssetVerificationModuleSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var result = await statistics.GetAssetVerificationStatsAsync(ct);
        if (!result.HasValue)
        {
            return new AssetVerificationModuleSummaryDto(null, null, null, null);
        }

        return new AssetVerificationModuleSummaryDto(
            result.Value.PendingReportCount,
            result.Value.MonthlyCompleted,
            result.Value.MonthlyNew,
            result.Value.YearTotal);
    }

    /// <summary>快速核对任务列表（可按申请日期范围筛选）。dateToExclusive 为开区间上界。</summary>
    public Task<Result<AssetVerificationTaskListResponse>> ListTasksAsync(
        int userId,
        int page,
        int pageSize,
        string? status,
        string? keyword,
        DateTime? dateFrom,
        DateTime? dateToExclusive,
        CancellationToken ct = default)
        => QueryTasksAsync(
            userId, page, pageSize, status, keyword, dateFrom, dateToExclusive, null, null, ct);

    /// <summary>月度审核列表：按 application_date 落在指定年月筛选；年月缺省用当前月。</summary>
    public Task<Result<AssetVerificationTaskListResponse>> ListMonthlyAsync(
        int userId,
        int? year,
        int? month,
        int page,
        int pageSize,
        string? status,
        string? keyword,
        CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var effectiveYear = year ?? now.Year;
        var effectiveMonth = month ?? now.Month;

        if (effectiveYear is < 1900 or > 9999 || effectiveMonth is < 1 or > 12)
        {
            return Task.FromResult(Result.Failure<AssetVerificationTaskListResponse>(
                ErrorCodes.VALIDATION_FAILED, "年月参数无效"));
        }

        var monthStart = new DateTime(effectiveYear, effectiveMonth, 1);
        return QueryTasksAsync(
            userId, page, pageSize, status, keyword, null, null,
            monthStart, monthStart.AddMonths(1), ct);
    }

    /// <summary>核对详情：详情查询本身带数据范围，越权按不存在（404）处理。</summary>
    public async Task<Result<AssetVerificationDetailDto>> GetDetailAsync(
        int userId,
        long id,
        CancellationToken ct = default)
    {
        var parameters = new List<object?> { id };
        var scope = await dataScope.BuildAsync(
            userId, DataScopeTarget.AssetChecks, "v", parameters.Count + 1, ct);

        var conditions = "v.id = $1 AND v.deleted_at IS NULL";
        if (!scope.IsEmpty)
        {
            conditions += $" AND ({scope.Sql})";
            parameters.AddRange(scope.Parameters);
        }

        var detailSql =
            $@"SELECT v.id, v.batch_id, v.applicant_name, v.applicant_id_type, v.applicant_id_card,
                      v.relationship, v.is_head, v.head_id_card, v.family_address, v.community,
                      v.application_reason, v.application_date, v.contact_phone, v.status,
                      v.created_at, v.updated_at
               FROM nc_biz_asset_checks v
               WHERE {conditions}
               LIMIT 1";

        var detailResult = await database.QuerySingleAsync<AssetVerificationDetailRow>(
            detailSql, ct, parameters.ToArray());

        if (detailResult.IsFailure)
        {
            return Result.Failure<AssetVerificationDetailDto>(
                detailResult.ErrorCode!, detailResult.Message!);
        }

        var detail = detailResult.Value;
        if (detail is null)
        {
            return Result.Failure<AssetVerificationDetailDto>(
                ErrorCodes.NOT_FOUND, "核对记录不存在");
        }

        // 同批次成员：详情已通过数据范围校验，家庭成员按 batch_id 归属授权户。
        const string memberSql =
            @"SELECT v.id, v.applicant_name, v.applicant_id_card, v.relationship, v.is_head
              FROM nc_biz_asset_checks v
              WHERE v.batch_id = $1 AND v.deleted_at IS NULL
              ORDER BY v.is_head DESC, v.id ASC
              LIMIT 200";

        var memberResult = await database.QueryAsync<AssetVerificationMemberRow>(
            memberSql, ct, detail.BatchId);

        if (memberResult.IsFailure)
        {
            return Result.Failure<AssetVerificationDetailDto>(
                memberResult.ErrorCode!, memberResult.Message!);
        }

        var members = new List<AssetVerificationMemberDto>();
        foreach (var member in memberResult.Value ?? [])
        {
            members.Add(new AssetVerificationMemberDto(
                member.Id,
                member.ApplicantName ?? string.Empty,
                member.ApplicantIdCard ?? string.Empty,
                member.Relationship ?? string.Empty,
                member.IsHead));
        }

        return Result.Success(new AssetVerificationDetailDto(
            detail.Id,
            detail.BatchId ?? string.Empty,
            detail.ApplicantName ?? string.Empty,
            detail.ApplicantIdCard ?? string.Empty,
            detail.ApplicantIdType ?? string.Empty,
            detail.Relationship ?? string.Empty,
            detail.IsHead,
            detail.HeadIdCard ?? string.Empty,
            detail.FamilyAddress ?? string.Empty,
            detail.Community ?? string.Empty,
            detail.ApplicationReason ?? string.Empty,
            detail.ApplicationDate,
            detail.ContactPhone ?? string.Empty,
            detail.Status ?? string.Empty,
            ToStatusText(detail.Status),
            detail.CreatedAt ?? DateTime.MinValue,
            detail.UpdatedAt ?? DateTime.MinValue,
            members));
    }

    private async Task<Result<AssetVerificationTaskListResponse>> QueryTasksAsync(
        int userId,
        int page,
        int pageSize,
        string? status,
        string? keyword,
        DateTime? dateFrom,
        DateTime? dateToExclusive,
        DateTime? monthStart,
        DateTime? monthEnd,
        CancellationToken ct)
    {
        var pageIndex = Math.Max(page, 1);
        var effectivePageSize = Math.Clamp(
            pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);

        var (where, parameters) = await BuildWhereAsync(
            userId, status, keyword, dateFrom, dateToExclusive, monthStart, monthEnd, ct);

        var countResult = await database.ExecuteScalarAsync<long?>(
            "SELECT COUNT(*) FROM nc_biz_asset_checks v" + where,
            ct,
            parameters.ToArray());

        if (countResult.IsFailure)
        {
            return Result.Failure<AssetVerificationTaskListResponse>(
                countResult.ErrorCode!, countResult.Message!);
        }

        var totalCount = countResult.Value ?? 0;
        if (totalCount == 0)
        {
            return Result.Success(new AssetVerificationTaskListResponse(
                [], pageIndex, effectivePageSize, 0, 0));
        }

        var listParameters = new List<object?>(parameters)
        {
            effectivePageSize,
            (long)(pageIndex - 1) * effectivePageSize
        };
        var limitIndex = parameters.Count + 1;
        var offsetIndex = parameters.Count + 2;

        var listSql =
            $@"SELECT {ListColumns}
               FROM nc_biz_asset_checks v{where}
               ORDER BY v.application_date DESC, v.id DESC
               LIMIT ${limitIndex} OFFSET ${offsetIndex}";

        var rowsResult = await database.QueryAsync<AssetVerificationListRow>(
            listSql, ct, listParameters.ToArray());

        if (rowsResult.IsFailure)
        {
            return Result.Failure<AssetVerificationTaskListResponse>(
                rowsResult.ErrorCode!, rowsResult.Message!);
        }

        var items = new List<AssetVerificationTaskItemDto>();
        foreach (var row in rowsResult.Value ?? [])
        {
            items.Add(new AssetVerificationTaskItemDto(
                row.Id,
                row.BatchId ?? string.Empty,
                row.ApplicantName ?? string.Empty,
                DataMasker.MaskIdCard(row.ApplicantIdCard ?? string.Empty),
                row.Relationship ?? string.Empty,
                row.IsHead,
                DataMasker.MaskIdCard(row.HeadIdCard ?? string.Empty),
                row.FamilyAddress ?? string.Empty,
                row.Community ?? string.Empty,
                row.ApplicationDate,
                row.Status ?? string.Empty,
                ToStatusText(row.Status),
                DataMasker.MaskPhone(row.ContactPhone ?? string.Empty)));
        }

        var totalPages = (int)((totalCount + effectivePageSize - 1) / effectivePageSize);
        return Result.Success(new AssetVerificationTaskListResponse(
            items, pageIndex, effectivePageSize, totalCount, totalPages));
    }

    private async Task<(string Where, List<object?> Parameters)> BuildWhereAsync(
        int userId,
        string? status,
        string? keyword,
        DateTime? dateFrom,
        DateTime? dateToExclusive,
        DateTime? monthStart,
        DateTime? monthEnd,
        CancellationToken ct)
    {
        var conditions = new List<string> { "v.deleted_at IS NULL" };
        var parameters = new List<object?>();

        var normalizedStatus = Normalize(status);
        if (normalizedStatus is not null)
        {
            parameters.Add(normalizedStatus);
            conditions.Add($"v.status = ${parameters.Count}");
        }

        var normalizedKeyword = Normalize(keyword);
        if (normalizedKeyword is not null)
        {
            parameters.Add(ToLikePattern(normalizedKeyword));
            var index = parameters.Count;
            conditions.Add(
                $"(v.applicant_name ILIKE ${index} OR v.applicant_id_card ILIKE ${index} " +
                $"OR v.head_id_card ILIKE ${index})");
        }

        if (dateFrom is { } from)
        {
            parameters.Add(from.Date);
            conditions.Add($"v.application_date >= ${parameters.Count}");
        }

        if (dateToExclusive is { } to)
        {
            parameters.Add(to.Date);
            conditions.Add($"v.application_date < ${parameters.Count}");
        }

        if (monthStart is { } start && monthEnd is { } end)
        {
            parameters.Add(start);
            conditions.Add($"v.application_date >= ${parameters.Count}");

            parameters.Add(end);
            conditions.Add($"v.application_date < ${parameters.Count}");
        }

        var scope = await dataScope.BuildAsync(
            userId, DataScopeTarget.AssetChecks, "v", parameters.Count + 1, ct);

        if (!scope.IsEmpty)
        {
            conditions.Add($"({scope.Sql})");
            parameters.AddRange(scope.Parameters);
        }

        return (" WHERE " + string.Join(" AND ", conditions), parameters);
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

    private static string ToStatusText(string? status)
        => string.IsNullOrWhiteSpace(status)
            ? string.Empty
            : AssetCheckStatusConstants.GetDescription(status);
}

/// <summary>列表行：列名经 Core 的下划线转 PascalCase 映射。</summary>
public sealed class AssetVerificationListRow
{
    public long Id { get; set; }
    public string? BatchId { get; set; }
    public string? ApplicantName { get; set; }
    public string? ApplicantIdCard { get; set; }
    public string? Relationship { get; set; }
    public bool IsHead { get; set; }
    public string? HeadIdCard { get; set; }
    public string? FamilyAddress { get; set; }
    public string? Community { get; set; }
    public DateTime ApplicationDate { get; set; }
    public string? Status { get; set; }
    public string? ContactPhone { get; set; }
}

/// <summary>详情行；created_at / updated_at 允许为空以兼容历史数据。</summary>
public sealed class AssetVerificationDetailRow
{
    public long Id { get; set; }
    public string? BatchId { get; set; }
    public string? ApplicantName { get; set; }
    public string? ApplicantIdType { get; set; }
    public string? ApplicantIdCard { get; set; }
    public string? Relationship { get; set; }
    public bool IsHead { get; set; }
    public string? HeadIdCard { get; set; }
    public string? FamilyAddress { get; set; }
    public string? Community { get; set; }
    public string? ApplicationReason { get; set; }
    public DateTime ApplicationDate { get; set; }
    public string? ContactPhone { get; set; }
    public string? Status { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class AssetVerificationMemberRow
{
    public long Id { get; set; }
    public string? ApplicantName { get; set; }
    public string? ApplicantIdCard { get; set; }
    public string? Relationship { get; set; }
    public bool IsHead { get; set; }
}
