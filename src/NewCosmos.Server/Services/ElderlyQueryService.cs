using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.Reporting;

namespace NewCosmos.Server.Services;

/// <summary>
/// 高龄津贴发放只读查询：列表/详情/待办/复核。
/// 列表与详情自行拼 SQL 以注入数据范围（Core GetPagedAsync 不接受范围条件）；
/// 参数统一使用 $1..$n，列表显式列 + LIMIT，禁止 SELECT *。
/// </summary>
public sealed class ElderlyQueryService(
    IDatabaseService database,
    DataScopeFilterBuilder scopeBuilder,
    IElderlyApplicationService elderlyService,
    IStatisticsService statisticsService,
    ILoggerService logger)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private const string ApplicationTable = "nc_biz_elderly_applications";

    /// <summary>概览统计：任一 Core 调用失败只把对应字段置空，不拖垮整页。</summary>
    public async Task<Result<ElderlyModuleSummaryDto>> GetSummaryAsync(CancellationToken ct = default)
    {
        var statsResult = await statisticsService.GetElderlyStatsAsync(ct);
        var countsResult = await elderlyService.GetPendingElderlyCountsAsync(ct);

        var activeCount = statsResult.HasValue ? statsResult.Value.ActiveCount : (int?)null;
        var monthlyNew = statsResult.HasValue ? statsResult.Value.MonthlyNew : (int?)null;
        var pendingNew = countsResult.HasValue ? countsResult.Value.PendingNew : (int?)null;
        var pendingTransfer = countsResult.HasValue ? countsResult.Value.PendingTransfer : (int?)null;

        if (statsResult.IsFailure)
        {
            logger.LogError(
                new InvalidOperationException("ElderlyStats"),
                $"读取高龄概览统计失败（对应字段留空）: {statsResult.ErrorCode}");
        }

        if (countsResult.IsFailure)
        {
            logger.LogError(
                new InvalidOperationException("ElderlyPendingCounts"),
                $"读取高龄待办统计失败（对应字段留空）: {countsResult.ErrorCode}");
        }

        return Result.Success(new ElderlyModuleSummaryDto(
            activeCount, monthlyNew, pendingNew, pendingTransfer));
    }

    /// <summary>登记列表分页。status 为空表示全部；keyword 匹配姓名/身份证/申请编号。</summary>
    public async Task<Result<ElderlyApplicationListResponse>> ListApplicationsAsync(
        int userId,
        int page,
        int pageSize,
        string? status,
        string? keyword,
        CancellationToken ct = default)
    {
        var pageIndex = Math.Max(page, 1);
        var effectivePageSize = Math.Clamp(
            pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);

        var conditions = new List<string> { "a.deleted_at IS NULL" };
        var parameters = new List<object?>();

        var normalizedStatus = Normalize(status);
        if (normalizedStatus is not null)
        {
            parameters.Add(normalizedStatus);
            conditions.Add($"a.status = ${parameters.Count}");
        }

        var normalizedKeyword = Normalize(keyword);
        if (normalizedKeyword is not null)
        {
            parameters.Add(ToLikePattern(normalizedKeyword));
            var keywordIndex = parameters.Count;
            conditions.Add(
                $"(a.name ILIKE ${keywordIndex} OR a.id_card ILIKE ${keywordIndex} " +
                $"OR a.application_no ILIKE ${keywordIndex})");
        }

        var scope = await scopeBuilder.BuildAsync(
            userId,
            DataScopeTarget.ElderlyApplications,
            "a",
            parameters.Count + 1,
            ct);
        if (!scope.IsEmpty)
        {
            conditions.Add(scope.Sql);
            parameters.AddRange(scope.Parameters);
        }

        var where = " WHERE " + string.Join(" AND ", conditions);

        var countResult = await database.ExecuteScalarAsync<long?>(
            $"SELECT COUNT(*) FROM {ApplicationTable} a{where}",
            ct,
            parameters.ToArray());
        if (countResult.IsFailure)
        {
            return Result.Failure<ElderlyApplicationListResponse>(
                countResult.ErrorCode!, countResult.Message!);
        }

        var totalCount = countResult.Value ?? 0;
        if (totalCount == 0)
        {
            return Result.Success(new ElderlyApplicationListResponse(
                Array.Empty<ElderlyApplicationListItemDto>(),
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
            $@"SELECT a.id, a.application_no, a.name, a.id_card, a.gender, a.birth_date,
                      a.category, a.status, a.apply_date, a.confirmed_at,
                      a.issue_start_month, a.issue_amount
               FROM {ApplicationTable} a{where}
               ORDER BY COALESCE(a.apply_date, a.created_at) DESC, a.id DESC
               LIMIT ${limitIndex} OFFSET ${offsetIndex}";

        var rowsResult = await database.QueryAsync<ElderlyApplicationListRow>(
            listSql, ct, listParameters.ToArray());
        if (rowsResult.IsFailure)
        {
            return Result.Failure<ElderlyApplicationListResponse>(
                rowsResult.ErrorCode!, rowsResult.Message!);
        }

        var items = new List<ElderlyApplicationListItemDto>();
        foreach (var row in rowsResult.Value ?? [])
        {
            var birthDate = ToDateOnly(row.BirthDate);
            items.Add(new ElderlyApplicationListItemDto(
                row.Id,
                row.ApplicationNo ?? string.Empty,
                row.Name ?? string.Empty,
                DataMasker.MaskIdCard(row.IdCard ?? string.Empty),
                row.Gender ?? string.Empty,
                birthDate,
                CalculateAge(birthDate),
                row.Category ?? string.Empty,
                ElderlyBenefitConstants.GetCategoryName(row.Category),
                row.Status ?? string.Empty,
                ElderlyBenefitConstants.GetStatusName(row.Status),
                row.ApplyDate,
                row.ConfirmedAt,
                row.IssueStartMonth ?? string.Empty,
                row.IssueAmount));
        }

        var totalPages = (int)((totalCount + effectivePageSize - 1) / effectivePageSize);
        return Result.Success(new ElderlyApplicationListResponse(
            items, pageIndex, effectivePageSize, totalCount, totalPages));
    }

    /// <summary>登记详情。数据范围不匹配时返回 NOT_FOUND（不暴露越权记录是否存在）。</summary>
    public async Task<Result<ElderlyApplicationDetailDto>> GetApplicationDetailAsync(
        int userId,
        long id,
        CancellationToken ct = default)
    {
        var parameters = new List<object?> { id };
        var scope = await scopeBuilder.BuildAsync(
            userId,
            DataScopeTarget.ElderlyApplications,
            "a",
            parameters.Count + 1,
            ct);

        var conditions = new List<string> { "a.id = $1", "a.deleted_at IS NULL" };
        if (!scope.IsEmpty)
        {
            conditions.Add(scope.Sql);
            parameters.AddRange(scope.Parameters);
        }

        var sql =
            $"SELECT {WideTableColumns.ElderlyApplicationsColumns} FROM {ApplicationTable} a" +
            " WHERE " + string.Join(" AND ", conditions);

        var detailResult = await database.QuerySingleAsync<ElderlyApplication>(
            sql, ct, parameters.ToArray());
        if (detailResult.IsFailure)
        {
            return Result.Failure<ElderlyApplicationDetailDto>(
                detailResult.ErrorCode!, detailResult.Message!);
        }

        var row = detailResult.Value;
        if (row is null)
        {
            return Result.Failure<ElderlyApplicationDetailDto>(
                ApiErrorCodes.NotFound, "高龄津贴档案不存在或无权查看");
        }

        var birthDate = ToDateOnly(row.BirthDate);
        return Result.Success(new ElderlyApplicationDetailDto(
            row.Id,
            row.ApplicationNo ?? string.Empty,
            row.Name ?? string.Empty,
            row.IdCard ?? string.Empty,
            row.Gender ?? string.Empty,
            birthDate,
            CalculateAge(birthDate),
            row.Phone ?? string.Empty,
            row.HukouAddress ?? string.Empty,
            row.HukouVillage ?? string.Empty,
            row.HukouDetailAddress ?? string.Empty,
            row.FamilyAddress ?? string.Empty,
            row.DetailAddress ?? string.Empty,
            row.Category ?? string.Empty,
            ElderlyBenefitConstants.GetCategoryName(row.Category),
            row.IsCategoryManual,
            row.IdentityFlag ?? string.Empty,
            row.IdentitySource ?? string.Empty,
            row.IssueAmount,
            row.IssueStartMonth ?? string.Empty,
            row.BankName ?? string.Empty,
            row.BankAccount ?? string.Empty,
            row.AgentName ?? string.Empty,
            row.AgentRelation ?? string.Empty,
            row.AgentReceiveName ?? string.Empty,
            row.AgentReceiveRelation ?? string.Empty,
            row.AgentReceiveBankName ?? string.Empty,
            row.AgentReceiveBankAccount ?? string.Empty,
            row.AgentReceiveReason ?? string.Empty,
            row.PaybackStartMonth ?? string.Empty,
            row.PaybackEndMonth ?? string.Empty,
            row.PaybackMonths,
            row.PaybackAmount,
            row.PaybackReason ?? string.Empty,
            row.IsSpecialCase,
            row.SpecialReason ?? string.Empty,
            row.Status ?? string.Empty,
            ElderlyBenefitConstants.GetStatusName(row.Status),
            row.ApplyDate,
            row.ConfirmedAt,
            row.ConfirmedBy ?? string.Empty,
            row.DueStopDate,
            row.ActualStopDate,
            row.StoppedAt,
            row.StoppedBy ?? string.Empty,
            row.StopReason ?? string.Empty,
            ElderlyBenefitConstants.GetStopReasonName(row.StopReason),
            row.IsRecover,
            row.RecoverStartMonth ?? string.Empty,
            row.RecoverEndMonth ?? string.Empty,
            row.RecoverAmount,
            row.Remark ?? string.Empty,
            row.CreatedAt,
            row.UpdatedAt));
    }

    /// <summary>
    /// 下月待办：人员集合与分流口径完全复用 Core；Server 只做 PII 脱敏、
    /// 类型中文映射，并批量补出当前类别（在享档 / 待复核记录的旧类别）。
    /// </summary>
    public async Task<Result<ElderlyPendingResponse>> GetPendingAsync(
        int userId,
        CancellationToken ct = default)
    {
        var listResult = await elderlyService.GetPendingElderlyListAsync(ct);
        if (listResult.IsFailure)
        {
            return Result.Failure<ElderlyPendingResponse>(
                listResult.ErrorCode!, listResult.Message!);
        }

        var sourceItems = listResult.Value ?? [];

        // Core 的待办列表是全局口径；受限用户按当前数据范围二次过滤，
        // 避免把其他乡镇/村的姓名和身份证带到待办页。
        var scope = await scopeBuilder.BuildAsync(
            userId, DataScopeTarget.ElderlyApplications, "a", 1, ct);
        var scoped = !scope.IsEmpty;
        if (scoped)
        {
            var allowedResult = await database.QueryAsync<ElderlyScopeRow>(
                $"SELECT a.id, a.id_card FROM {ApplicationTable} a " +
                $"WHERE a.deleted_at IS NULL AND ({scope.Sql})",
                ct,
                scope.Parameters.ToArray());
            if (allowedResult.IsFailure)
            {
                return Result.Failure<ElderlyPendingResponse>(
                    allowedResult.ErrorCode!, allowedResult.Message!);
            }

            var allowedIds = (allowedResult.Value ?? [])
                .Select(x => x.Id)
                .Where(x => x > 0)
                .ToHashSet();
            var allowedIdCards = (allowedResult.Value ?? [])
                .Select(x => x.IdCard)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            sourceItems = sourceItems
                .Where(item =>
                    (item.ElderlyApplicationId > 0 && allowedIds.Contains(item.ElderlyApplicationId))
                    || (!string.IsNullOrWhiteSpace(item.IdCard) && allowedIdCards.Contains(item.IdCard)))
                .ToList();
        }

        var countsResult = scoped
            ? null
            : await elderlyService.GetPendingElderlyCountsAsync(ct);

        var pendingNew = countsResult is { HasValue: true }
            ? countsResult.Value.PendingNew
            : sourceItems.Count(x => !x.IsTransfer);
        var pendingTransfer = countsResult is { HasValue: true }
            ? countsResult.Value.PendingTransfer
            : sourceItems.Count(x => x.IsTransfer);

        var reviewCategories = await LoadReviewCategoriesAsync(
            sourceItems.Where(x => x.ReviewId > 0).Select(x => x.ReviewId), ct);
        var applicationCategories = await LoadApplicationCategoriesAsync(
            sourceItems.Where(x => x.ElderlyApplicationId > 0).Select(x => x.ElderlyApplicationId), ct);

        var items = new List<ElderlyPendingItemDto>(sourceItems.Count);
        foreach (var item in sourceItems)
        {
            var category = ResolveCurrentCategory(item, applicationCategories, reviewCategories);
            var isTransfer = item.IsTransfer;
            items.Add(new ElderlyPendingItemDto(
                DataMasker.MaskIdCard(item.IdCard),
                item.Name ?? string.Empty,
                item.Age,
                item.PendingType,
                isTransfer ? "需停旧增新" : "待新增",
                category,
                ElderlyBenefitConstants.GetCategoryName(category),
                isTransfer ? "办理类别复核（停旧增新）" : "新增登记",
                item.StatusLabel,
                item.MemberRole ?? string.Empty,
                item.Gender ?? string.Empty,
                ToDateOnly(item.BirthDate),
                item.ElderlyApplicationId,
                item.ReviewId,
                item.SourceApplicationNo ?? string.Empty,
                item.ElderlyApplicationNo ?? string.Empty));
        }

        return Result.Success(new ElderlyPendingResponse(pendingNew, pendingTransfer, items));
    }

    /// <summary>
    /// 复核记录分页。Core GetReviewsPagedAsync 不带数据范围，这里改用 SQL 并按
    /// 同身份证/关联档案的户籍乡镇村过滤，保证受限用户不越权；范围为空表示全部。
    /// </summary>
    public async Task<Result<ElderlyReviewListResponse>> ListReviewsAsync(
        int userId,
        int page,
        int pageSize,
        string? status,
        string? keyword,
        CancellationToken ct = default)
    {
        var pageIndex = Math.Max(page, 1);
        var effectivePageSize = Math.Clamp(
            pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);

        var conditions = new List<string> { "r.deleted_at IS NULL" };
        var parameters = new List<object?>();

        var normalizedStatus = Normalize(status);
        if (normalizedStatus is not null)
        {
            parameters.Add(normalizedStatus);
            conditions.Add($"r.status = ${parameters.Count}");
        }

        var normalizedKeyword = Normalize(keyword);
        if (normalizedKeyword is not null)
        {
            parameters.Add(ToLikePattern(normalizedKeyword));
            var keywordIndex = parameters.Count;
            conditions.Add(
                $"(r.name ILIKE ${keywordIndex} OR r.id_card ILIKE ${keywordIndex} " +
                $"OR r.review_no ILIKE ${keywordIndex})");
        }

        var scope = await scopeBuilder.BuildAsync(
            userId,
            DataScopeTarget.ElderlyApplications,
            "a",
            parameters.Count + 1,
            ct);
        if (!scope.IsEmpty)
        {
            conditions.Add(
                "EXISTS (SELECT 1 FROM " + ApplicationTable + " a " +
                "WHERE a.deleted_at IS NULL " +
                "AND (a.id = r.old_application_id OR a.id = r.new_application_id " +
                "OR a.id_card = r.id_card) " +
                "AND (" + scope.Sql + "))");
            parameters.AddRange(scope.Parameters);
        }

        var where = " WHERE " + string.Join(" AND ", conditions);

        var countResult = await database.ExecuteScalarAsync<long?>(
            $"SELECT COUNT(*) FROM nc_biz_elderly_reviews r{where}",
            ct,
            parameters.ToArray());
        if (countResult.IsFailure)
        {
            return Result.Failure<ElderlyReviewListResponse>(
                countResult.ErrorCode!, countResult.Message!);
        }

        var totalCount = countResult.Value ?? 0;
        if (totalCount == 0)
        {
            return Result.Success(new ElderlyReviewListResponse(
                Array.Empty<ElderlyReviewListItemDto>(),
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
            $@"SELECT r.id, r.review_no, r.name, r.id_card, r.status, r.trigger_source,
                      r.trigger_reason, r.old_category, r.new_category, r.review_result,
                      r.effective_month, r.issue_start_month, r.reviewed_by, r.reviewed_at, r.created_at
               FROM nc_biz_elderly_reviews r{where}
               ORDER BY COALESCE(r.reviewed_at, r.created_at) DESC NULLS LAST, r.id DESC
               LIMIT ${limitIndex} OFFSET ${offsetIndex}";

        var rowsResult = await database.QueryAsync<ElderlyReviewListRow>(
            listSql, ct, listParameters.ToArray());
        if (rowsResult.IsFailure)
        {
            return Result.Failure<ElderlyReviewListResponse>(
                rowsResult.ErrorCode!, rowsResult.Message!);
        }

        var items = new List<ElderlyReviewListItemDto>();
        foreach (var row in rowsResult.Value ?? [])
        {
            var oldCategory = row.OldCategory ?? string.Empty;
            var newCategory = row.NewCategory ?? string.Empty;
            var categoryChange = string.IsNullOrEmpty(newCategory)
                ? ElderlyBenefitConstants.GetCategoryName(oldCategory)
                : $"{ElderlyBenefitConstants.GetCategoryName(oldCategory)} → " +
                  ElderlyBenefitConstants.GetCategoryName(newCategory);

            items.Add(new ElderlyReviewListItemDto(
                row.Id,
                row.ReviewNo ?? string.Empty,
                row.Name ?? string.Empty,
                DataMasker.MaskIdCard(row.IdCard ?? string.Empty),
                row.TriggerSource ?? string.Empty,
                ElderlyBenefitConstants.GetReviewTriggerName(row.TriggerSource),
                row.TriggerReason ?? string.Empty,
                oldCategory,
                ElderlyBenefitConstants.GetCategoryName(oldCategory),
                newCategory,
                ElderlyBenefitConstants.GetCategoryName(newCategory),
                categoryChange,
                row.ReviewResult ?? string.Empty,
                ElderlyBenefitConstants.GetReviewResultName(row.ReviewResult),
                row.Status ?? string.Empty,
                ElderlyBenefitConstants.GetReviewStatusName(row.Status),
                row.EffectiveMonth ?? string.Empty,
                row.IssueStartMonth ?? string.Empty,
                row.ReviewedBy ?? string.Empty,
                row.ReviewedAt,
                row.CreatedAt));
        }

        var totalPages = (int)((totalCount + effectivePageSize - 1) / effectivePageSize);
        return Result.Success(new ElderlyReviewListResponse(
            items, pageIndex, effectivePageSize, totalCount, totalPages));
    }

    private static string ResolveCurrentCategory(
        ElderlyPendingItem item,
        IReadOnlyDictionary<long, string> applicationCategories,
        IReadOnlyDictionary<long, string> reviewCategories)
    {
        if (item.ElderlyApplicationId > 0
            && applicationCategories.TryGetValue(item.ElderlyApplicationId, out var applicationCategory)
            && !string.IsNullOrWhiteSpace(applicationCategory))
        {
            return applicationCategory;
        }

        if (item.ReviewId > 0
            && reviewCategories.TryGetValue(item.ReviewId, out var reviewCategory)
            && !string.IsNullOrWhiteSpace(reviewCategory))
        {
            return reviewCategory;
        }

        return string.Empty;
    }

    /// <summary>补当前类别是展示增强，失败不阻断待办主数据。</summary>
    private async Task<IReadOnlyDictionary<long, string>> LoadReviewCategoriesAsync(
        IEnumerable<long> reviewIds,
        CancellationToken ct)
    {
        var ids = reviewIds.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, string>();
        }

        var result = await database.QueryAsync<ElderlyCategoryLookupRow>(
            "SELECT id, old_category AS category FROM nc_biz_elderly_reviews WHERE id = ANY($1)",
            ct,
            ids);
        if (result.IsFailure)
        {
            logger.LogError(
                new InvalidOperationException("ElderlyReviewCategory"),
                $"读取待办复核旧类别失败（仅影响展示）: {result.ErrorCode}");
            return new Dictionary<long, string>();
        }

        return (result.Value ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Category))
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First().Category!);
    }

    /// <summary>同身份证可能有多条档案，这里按 ID 精确取当前在享档类别。</summary>
    private async Task<IReadOnlyDictionary<long, string>> LoadApplicationCategoriesAsync(
        IEnumerable<long> applicationIds,
        CancellationToken ct)
    {
        var ids = applicationIds.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, string>();
        }

        var result = await database.QueryAsync<ElderlyCategoryLookupRow>(
            $"SELECT id, category FROM {ApplicationTable} WHERE id = ANY($1) AND deleted_at IS NULL",
            ct,
            ids);
        if (result.IsFailure)
        {
            logger.LogError(
                new InvalidOperationException("ElderlyApplicationCategory"),
                $"读取待办在享类别失败（仅影响展示）: {result.ErrorCode}");
            return new Dictionary<long, string>();
        }

        return (result.Value ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.Category))
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First().Category!);
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

    /// <summary>周岁：出生日期按自然日计算，未过生日减 1。</summary>
    private static int? CalculateAge(DateOnly? birthDate)
    {
        if (birthDate is not { } birth)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var age = today.Year - birth.Year;
        if (birth.AddYears(age) > today)
        {
            age--;
        }

        return age < 0 ? null : age;
    }
}

/// <summary>Core 反射映射需要 public 可写属性与公共无参构造函数。</summary>
public sealed class ElderlyApplicationListRow
{
    public long Id { get; set; }
    public string? ApplicationNo { get; set; }
    public string? Name { get; set; }
    public string? IdCard { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? Category { get; set; }
    public string? Status { get; set; }
    public DateTime? ApplyDate { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? IssueStartMonth { get; set; }
    public decimal IssueAmount { get; set; }
}

public sealed class ElderlyReviewListRow
{
    public long Id { get; set; }
    public string? ReviewNo { get; set; }
    public string? Name { get; set; }
    public string? IdCard { get; set; }
    public string? Status { get; set; }
    public string? TriggerSource { get; set; }
    public string? TriggerReason { get; set; }
    public string? OldCategory { get; set; }
    public string? NewCategory { get; set; }
    public string? ReviewResult { get; set; }
    public string? EffectiveMonth { get; set; }
    public string? IssueStartMonth { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
}

public sealed class ElderlyCategoryLookupRow
{
    public long Id { get; set; }
    public string? Category { get; set; }
}

/// <summary>待办数据范围过滤用：当前用户可见的高龄档案 ID + 身份证。</summary>
public sealed class ElderlyScopeRow
{
    public long Id { get; set; }
    public string? IdCard { get; set; }
}
