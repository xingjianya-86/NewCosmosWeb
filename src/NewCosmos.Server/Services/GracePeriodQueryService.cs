using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services;

/// <summary>
/// 渐退期列表只读查询（进行中 + 已到期）。
/// 口径与 Core <c>IGracePeriodService.GetExpiringPagedAsync</c> 一致，另补乡镇/村与数据范围过滤；
/// 列表显式列取、LIMIT 分页、全部值走 $1..$n 参数，身份证统一脱敏。
/// 采用静态查询器而非 DI 服务，避免改动共享的 Program.cs 注册。
/// </summary>
public static class GracePeriodQueryService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private const string StatusExpired = "Expired";

    private const string FromWhere =
        @" FROM nc_biz_grace_periods gp
           JOIN nc_biz_applications a ON gp.application_id = a.id AND a.deleted_at IS NULL
           WHERE gp.is_active = TRUE
             AND gp.deleted_at IS NULL
             AND gp.end_date IS NOT NULL
             AND a.status IN ($2, $3, $4)
             AND ($1 = '' OR (a.applicant_name ILIKE '%' || $1 || '%'
                              OR a.applicant_id_card ILIKE '%' || $1 || '%'))";

    private const string SelectColumns =
        @"SELECT a.id AS application_id,
                 a.application_no,
                 a.applicant_name,
                 a.applicant_id_card,
                 a.town,
                 a.community AS village,
                 gp.start_date AS grace_period_start_date,
                 gp.end_date AS grace_period_end_date,
                 gp.grace_period_months,
                 gp.original_classification,
                 (gp.end_date >= CURRENT_DATE) AS is_active_period,
                 (gp.end_date - CURRENT_DATE) AS days_left";

    public static async Task<Result<GracePeriodListResponse>> ListAsync(
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        ILoggerService logger,
        int userId,
        int page,
        int pageSize,
        string? keyword,
        CancellationToken ct = default)
    {
        try
        {
            var pageIndex = Math.Max(page, 1);
            var effectivePageSize = Math.Clamp(
                pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);
            var keywordValue = string.IsNullOrWhiteSpace(keyword) ? string.Empty : keyword.Trim();

            // $1 关键字、$2..$4 有效状态，数据范围参数从 $5 开始。
            var scope = await dataScope.BuildAsync(
                userId, DataScopeTarget.Applications, "a", 5, ct);

            var filter = FromWhere;
            if (!scope.IsEmpty)
            {
                filter += " AND " + scope.Sql;
            }

            var parameters = new List<object?>
            {
                keywordValue,
                ApplicationStatusCodes.APPROVED,
                ApplicationStatusCodes.COMPLETED,
                ApplicationStatusCodes.DRAFT
            };
            parameters.AddRange(scope.Parameters);

            var countSql = "SELECT COUNT(*)" + filter;
            var countResult = await database.ExecuteScalarAsync<long?>(
                countSql, ct, parameters.ToArray());
            if (countResult.IsFailure)
            {
                logger.Warn($"渐退期列表计数失败: {countResult.ErrorCode} {countResult.Message}");
                return Result.Failure<GracePeriodListResponse>(
                    countResult.ErrorCode!, countResult.Message!);
            }

            var totalCount = countResult.Value ?? 0;
            if (totalCount == 0)
            {
                return Result.Success(new GracePeriodListResponse(
                    [], pageIndex, effectivePageSize, 0, 0));
            }

            var dataParameters = new List<object?>(parameters)
            {
                effectivePageSize,
                (long)(pageIndex - 1) * effectivePageSize
            };
            var limitIndex = parameters.Count + 1;
            var offsetIndex = parameters.Count + 2;

            var dataSql = $"{SelectColumns}{filter} " +
                          $"ORDER BY gp.end_date ASC, a.id ASC LIMIT ${limitIndex} OFFSET ${offsetIndex}";

            var rowsResult = await database.QueryAsync<GracePeriodListRow>(
                dataSql, ct, dataParameters.ToArray());
            if (rowsResult.IsFailure)
            {
                logger.Warn($"渐退期列表查询失败: {rowsResult.ErrorCode} {rowsResult.Message}");
                return Result.Failure<GracePeriodListResponse>(
                    rowsResult.ErrorCode!, rowsResult.Message!);
            }

            var rows = rowsResult.Value ?? [];
            var items = new List<GracePeriodListItemDto>(rows.Count);
            foreach (var row in rows)
            {
                var isActive = row.IsActivePeriod;
                items.Add(new GracePeriodListItemDto(
                    row.ApplicationId,
                    row.ApplicationNo ?? string.Empty,
                    row.ApplicantName ?? string.Empty,
                    DataMasker.MaskIdCard(row.ApplicantIdCard ?? string.Empty),
                    row.Town ?? string.Empty,
                    row.Village ?? string.Empty,
                    row.GracePeriodStartDate,
                    row.GracePeriodEndDate,
                    row.DaysLeft,
                    isActive ? ApplicationStatusCodes.ACTIVE : StatusExpired,
                    isActive ? "进行中" : "已到期",
                    row.GracePeriodMonths,
                    isActive
                        ? $"剩 {Math.Abs(row.DaysLeft)} 天"
                        : $"逾期 {Math.Abs(row.DaysLeft)} 天",
                    row.OriginalClassification ?? string.Empty));
            }

            var totalPages = (int)((totalCount + effectivePageSize - 1) / effectivePageSize);
            logger.Info($"渐退期列表: 总{totalCount}条, 第{pageIndex}页");
            return Result.Success(new GracePeriodListResponse(
                items, pageIndex, effectivePageSize, totalCount, totalPages));
        }
        catch (Exception ex)
        {
            logger.Error($"查询渐退期列表异常: {ex.Message}");
            return Result.Failure<GracePeriodListResponse>(ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    /// <summary>
    /// 校验申请档案是否在当前用户的数据范围内（成员明细接口的越权闸门）。
    /// 口径与列表筛选一致：nc_biz_applications + DataScopeTarget.Applications，未命中按 404 处理。
    /// </summary>
    public static async Task<Result<bool>> IsApplicationInScopeAsync(
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        ILoggerService logger,
        int userId,
        long applicationId,
        CancellationToken ct = default)
    {
        try
        {
            if (applicationId <= 0)
            {
                return Result.Failure<bool>(ErrorCodes.VALIDATION_FAILED, "申请 id 无效");
            }

            // $1 申请 id，数据范围参数从 $2 开始。
            var scope = await dataScope.BuildAsync(
                userId, DataScopeTarget.Applications, "a", 2, ct);

            var sql = "SELECT COUNT(*) FROM nc_biz_applications a " +
                      "WHERE a.id = $1 AND a.deleted_at IS NULL";
            if (!scope.IsEmpty)
            {
                sql += " AND " + scope.Sql;
            }

            var parameters = new List<object?> { applicationId };
            parameters.AddRange(scope.Parameters);

            var countResult = await database.ExecuteScalarAsync<long>(
                sql, ct, parameters.ToArray());
            if (countResult.IsFailure)
            {
                logger.Warn(
                    $"渐退期成员范围校验失败: {countResult.ErrorCode} {countResult.Message}");
                return Result.Failure<bool>(countResult.ErrorCode!, countResult.Message!);
            }

            return Result.Success(countResult.Value > 0);
        }
        catch (Exception ex)
        {
            logger.Error($"渐退期成员范围校验异常: {ex.Message}");
            return Result.Failure<bool>(ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }
}

/// <summary>Core 反射映射需要 public 可写属性与公共无参构造函数。</summary>
public sealed class GracePeriodListRow
{
    public long ApplicationId { get; set; }
    public string? ApplicationNo { get; set; }
    public string? ApplicantName { get; set; }
    public string? ApplicantIdCard { get; set; }
    public string? Town { get; set; }
    public string? Village { get; set; }
    public DateTime? GracePeriodStartDate { get; set; }
    public DateTime? GracePeriodEndDate { get; set; }
    public int? GracePeriodMonths { get; set; }
    public string? OriginalClassification { get; set; }
    public bool IsActivePeriod { get; set; }
    public int DaysLeft { get; set; }
}
