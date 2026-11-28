using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.System;

namespace NewCosmos.Server.Services;

/// <summary>
/// 保障对象动态管理只读查询（列表 / 选中户详情 / 变更历史 / 筛选选项）。
/// 全部显式列取、LIMIT 分页、$1..$n 参数；身份证列表统一脱敏。
/// 金额与分类只回传 Core/数据库已落库结果，服务端不重算。
/// 以静态查询器实现，依赖由端点从 DI 传入，避免改动共享的 Program.cs 注册。
/// </summary>
public static class ChangeManagementQueryService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private static readonly string[] ActiveStatuses =
    [
        ApplicationStatusCodes.APPROVED,
        ApplicationStatusCodes.COMPLETED
    ];

    private static readonly string[] DetailDictionaryCategories =
    [
        DictionaryTypeCodes.FamilyRelationships,
        DictionaryTypeCodes.HealthStatuses,
        DictionaryTypeCodes.EmploymentStatuses,
        DictionaryTypeCodes.LaborAbilities,
        DictionaryTypeCodes.EducationLevels,
        DictionaryTypeCodes.MaritalStatuses,
        DictionaryTypeCodes.PoliticalStatuses
    ];

    private static readonly string[] FilterDictionaryCategories =
    [
        DictionaryTypeCodes.DeathReasons,
        DictionaryTypeCodes.MemberAddReasons,
        DictionaryTypeCodes.ChangeReasons,
        DictionaryTypeCodes.FamilyRelationships,
        DictionaryTypeCodes.HealthStatuses,
        DictionaryTypeCodes.EmploymentStatuses,
        DictionaryTypeCodes.EducationLevels,
        DictionaryTypeCodes.MaritalStatuses,
        DictionaryTypeCodes.PoliticalStatuses,
        DictionaryTypeCodes.LaborAbilities,
        DictionaryTypeCodes.HukouTypes
    ];

    // ───────────────────────────── 列表 ─────────────────────────────

    public static async Task<Result<ChangeManagementListResponse>> ListAsync(
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        ILoggerService logger,
        int userId,
        int page,
        int pageSize,
        string? keyword,
        string? town,
        string? classification,
        CancellationToken ct = default)
    {
        try
        {
            var pageIndex = Math.Max(page, 1);
            var effectivePageSize = Math.Clamp(
                pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);

            var conditions = new List<string>
            {
                "a.deleted_at IS NULL",
                "a.status = ANY($1)"
            };
            var parameters = new List<object?> { ActiveStatuses };

            var keywordValue = Normalize(keyword);
            if (keywordValue is not null)
            {
                parameters.Add(ToLikePattern(keywordValue));
                var index = parameters.Count;
                conditions.Add(
                    $"(a.applicant_name ILIKE ${index} OR a.applicant_id_card ILIKE ${index} " +
                    "OR EXISTS (SELECT 1 FROM nc_biz_family_members m " +
                    "WHERE m.application_id = a.id AND m.deleted_at IS NULL " +
                    $"AND (m.name ILIKE ${index} OR m.id_card ILIKE ${index})))");
            }

            var townValue = Normalize(town);
            if (townValue is not null)
            {
                parameters.Add(townValue);
                conditions.Add($"a.town = ${parameters.Count}");
            }

            var classificationValue = Normalize(classification);
            if (classificationValue is not null)
            {
                parameters.Add(classificationValue);
                conditions.Add($"a.classification_result = ${parameters.Count}");
            }

            var scope = await dataScope.BuildAsync(
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
                logger.Warn($"动态管理列表计数失败: {countResult.ErrorCode} {countResult.Message}");
                return Result.Failure<ChangeManagementListResponse>(
                    countResult.ErrorCode!, countResult.Message!);
            }

            var totalCount = countResult.Value ?? 0;
            if (totalCount == 0)
            {
                return Result.Success(new ChangeManagementListResponse(
                    [], pageIndex, effectivePageSize, 0, 0));
            }

            var dataParameters = new List<object?>(parameters)
            {
                effectivePageSize,
                (long)(pageIndex - 1) * effectivePageSize
            };
            var limitIndex = parameters.Count + 1;
            var offsetIndex = parameters.Count + 2;

            var sql =
                @"SELECT a.id AS application_id,
                         a.application_no,
                         a.applicant_name,
                         a.applicant_id_card,
                         a.town,
                         a.community AS village,
                         a.classification_result,
                         a.family_size,
                         a.household_monthly_guarantee_amount,
                         a.total_guarantee_amount,
                         a.per_capita_annual_income,
                         a.status,
                         gp.end_date AS grace_period_end_date,
                         gp.grace_period_months
                  FROM nc_biz_applications a
                  LEFT JOIN LATERAL (
                      SELECT g.end_date, g.grace_period_months
                      FROM nc_biz_grace_periods g
                      WHERE g.application_id = a.id
                        AND g.is_active = TRUE
                        AND g.deleted_at IS NULL
                      ORDER BY g.id DESC
                      LIMIT 1
                  ) gp ON TRUE" + where +
                $" ORDER BY a.updated_at DESC, a.id DESC LIMIT ${limitIndex} OFFSET ${offsetIndex}";

            var rowsResult = await database.QueryAsync<ChangeListRow>(
                sql, ct, dataParameters.ToArray());
            if (rowsResult.IsFailure)
            {
                logger.Warn($"动态管理列表查询失败: {rowsResult.ErrorCode} {rowsResult.Message}");
                return Result.Failure<ChangeManagementListResponse>(
                    rowsResult.ErrorCode!, rowsResult.Message!);
            }

            var rows = rowsResult.Value ?? [];
            var items = new List<ChangeManagementListItemDto>(rows.Count);
            foreach (var row in rows)
            {
                items.Add(new ChangeManagementListItemDto(
                    row.ApplicationId,
                    row.ApplicationNo ?? string.Empty,
                    row.ApplicantName ?? string.Empty,
                    DataMasker.MaskIdCard(row.ApplicantIdCard ?? string.Empty),
                    row.Town ?? string.Empty,
                    row.Village ?? string.Empty,
                    row.ClassificationResult ?? string.Empty,
                    ClassificationConstants.ConvertFromCode(row.ClassificationResult ?? string.Empty),
                    row.FamilySize,
                    row.HouseholdMonthlyGuaranteeAmount,
                    row.TotalGuaranteeAmount,
                    row.PerCapitaAnnualIncome,
                    row.Status ?? string.Empty,
                    ApplicationQueryService.ToStatusText(row.Status),
                    row.GracePeriodEndDate,
                    row.GracePeriodMonths,
                    row.GracePeriodEndDate is { } endDate
                        ? (endDate.Date - DateTime.Today).Days
                        : null));
            }

            var totalPages = (int)((totalCount + effectivePageSize - 1) / effectivePageSize);
            logger.Info($"动态管理列表: 总{totalCount}条, 第{pageIndex}页");
            return Result.Success(new ChangeManagementListResponse(
                items, pageIndex, effectivePageSize, totalCount, totalPages));
        }
        catch (Exception ex)
        {
            logger.Error($"查询动态管理列表异常: {ex.Message}");
            return Result.Failure<ChangeManagementListResponse>(
                ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    // ───────────────────── 导入库待建档检索（双源搜索的导入库半边） ─────────────────────

    /// <summary>
    /// 按姓名/身份证检索 6 个历史导入库（口径同桌面版变更页双源搜索）。
    /// 只读；与「救助档案管理 → 档案查询」一致，不做数据范围过滤（范围校验在
    /// 建档迁移时执行），返回前按来源表+行号批量补出"是否已建档"。
    /// </summary>
    public static async Task<Result<ImportedArchiveSearchResponse>> SearchImportedAsync(
        IDatabaseService database,
        IImportedArchiveService importedArchives,
        ILoggerService logger,
        string? keyword,
        CancellationToken ct = default)
    {
        try
        {
            var keywordValue = Normalize(keyword);
            if (keywordValue is null)
            {
                return Result.Success(new ImportedArchiveSearchResponse([]));
            }

            var searchResult = await importedArchives.SearchAsync(keywordValue, ct);
            if (searchResult.IsFailure)
            {
                logger.Warn($"动态管理导入库检索失败: {searchResult.ErrorCode} {searchResult.Message}");
                return Result.Failure<ImportedArchiveSearchResponse>(
                    searchResult.ErrorCode!, searchResult.Message!);
            }

            var items = (searchResult.Value ?? [])
                .Where(item => item.Source != ArchiveSearchSource.Current)
                .ToList();
            if (items.Count == 0)
            {
                return Result.Success(new ImportedArchiveSearchResponse([]));
            }

            var existing = await LoadExistingApplicationIdsAsync(database, items, logger, ct);

            var dtos = items.Select(item => new ImportedArchiveItemDto(
                    (int)item.Source,
                    item.SourceDisplay,
                    item.SourceTable,
                    item.SourceId,
                    item.ApplicantName ?? string.Empty,
                    DataMasker.MaskIdCard(item.ApplicantIdCard ?? string.Empty),
                    item.Address ?? string.Empty,
                    item.FamilySize,
                    item.TotalAmount,
                    item.HukouType ?? string.Empty,
                    item.MatchedPersonInfo ?? string.Empty,
                    item.DerivedClassificationCode ?? string.Empty,
                    item.DerivedClassificationName ?? string.Empty,
                    existing.TryGetValue((item.SourceTable, item.SourceId), out var appId) ? appId : null))
                .ToList();

            return Result.Success(new ImportedArchiveSearchResponse(dtos));
        }
        catch (Exception ex)
        {
            logger.Error($"检索动态管理导入库异常: {ex.Message}");
            return Result.Failure<ImportedArchiveSearchResponse>(
                ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    /// <summary>
    /// 建档前的数据范围校验：把导入库记录所在的乡镇/村文本，用与列表完全相同的
    /// 数据范围条件（DataScopeTarget.Applications）套一遍，避免越权建档。
    /// 全部范围直接通过；自助（SELF）范围没有归属人，按拒绝处理。
    /// </summary>
    public static async Task<bool> IsImportedRecordInScopeAsync(
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        ILoggerService logger,
        int userId,
        string? town,
        string? community,
        CancellationToken ct = default)
    {
        // 参数顺序：$1 town、$2 community，数据范围条件从 $3 开始。
        var scope = await dataScope.BuildAsync(userId, DataScopeTarget.Applications, "probe", 3, ct);
        if (scope.IsEmpty)
        {
            return true;
        }

        var parameters = new List<object?>
        {
            (town ?? string.Empty).Trim(),
            (community ?? string.Empty).Trim()
        };
        parameters.AddRange(scope.Parameters);

        var sql =
            "SELECT 1 AS ok FROM (" +
            "SELECT $1::text AS town, $2::text AS community, " +
            "NULL::int AS town_id, NULL::int AS village_id, NULL::text AS created_by" +
            ") probe WHERE " + scope.Sql + " LIMIT 1";

        var result = await database.ExecuteScalarAsync<int?>(sql, ct, parameters.ToArray());
        if (result.IsFailure)
        {
            logger.Warn($"导入库建档数据范围校验失败，按拒绝处理: {result.ErrorCode} {result.Message}");
            return false;
        }

        return result.Value is not null;
    }

    private static async Task<Dictionary<(string Table, long Id), long>> LoadExistingApplicationIdsAsync(
        IDatabaseService database,
        IReadOnlyList<ArchiveSearchResultItem> items,
        ILoggerService logger,
        CancellationToken ct)
    {
        var conditions = new List<string>(items.Count);
        var parameters = new List<object?>(items.Count * 2);
        foreach (var item in items)
        {
            parameters.Add(item.SourceTable);
            var tableIndex = parameters.Count;
            parameters.Add(item.SourceId);
            var idIndex = parameters.Count;
            conditions.Add($"(source_table = ${tableIndex} AND source_id = ${idIndex})");
        }

        var sql =
            "SELECT source_table, source_id, id AS application_id FROM nc_biz_applications " +
            "WHERE source_type = 'ImportedArchive' AND deleted_at IS NULL AND (" +
            string.Join(" OR ", conditions) + ")";

        var result = await database.QueryAsync<ExistingApplicationRow>(sql, ct, parameters.ToArray());
        if (result.IsFailure)
        {
            // 只影响"已建档"角标，不影响检索本身。
            logger.Warn($"查询导入库建档状态失败（降级为未建档展示）: {result.ErrorCode} {result.Message}");
            return new Dictionary<(string, long), long>();
        }

        var map = new Dictionary<(string, long), long>();
        foreach (var row in result.Value ?? [])
        {
            var table = (row.SourceTable ?? string.Empty).Trim();
            if (table.Length > 0)
            {
                map[(table, row.SourceId)] = row.ApplicationId;
            }
        }

        return map;
    }

    // ───────────────────────────── 选中户详情 ─────────────────────────────

    public static async Task<Result<ChangeManagementDetailDto>> GetDetailAsync(
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IChangeService changes,
        IGracePeriodService gracePeriods,
        IFamilyMemberService familyMembers,
        IStandardConfigService standards,
        ILoggerService logger,
        int userId,
        bool canManage,
        long applicationId,
        CancellationToken ct = default)
    {
        try
        {
            var scopeResult = await GracePeriodQueryService.IsApplicationInScopeAsync(
                database, dataScope, logger, userId, applicationId, ct);
            if (scopeResult.IsFailure)
            {
                return Result.Failure<ChangeManagementDetailDto>(
                    scopeResult.ErrorCode!, scopeResult.Message!);
            }

            if (!scopeResult.Value)
            {
                return Result.Failure<ChangeManagementDetailDto>(
                    ErrorCodes.NOT_FOUND, "申请档案不存在或不在数据范围内");
            }

            var appResult = await database.QuerySingleAsync<Application>(
                $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications a " +
                "WHERE a.id = $1 AND a.deleted_at IS NULL",
                ct,
                applicationId);
            if (appResult.IsFailure)
            {
                return Result.Failure<ChangeManagementDetailDto>(
                    appResult.ErrorCode!, appResult.Message!);
            }

            var application = appResult.Value;
            if (application is null)
            {
                return Result.Failure<ChangeManagementDetailDto>(
                    ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            var membersResult = await familyMembers.GetByApplicationIdAsync(applicationId, ct);
            if (membersResult.IsFailure)
            {
                logger.Warn(
                    $"动态管理成员加载失败: ApplicationId={applicationId}, " +
                    $"{membersResult.ErrorCode} {membersResult.Message}");
                return Result.Failure<ChangeManagementDetailDto>(
                    membersResult.ErrorCode!, membersResult.Message!);
            }

            var deathResult = await familyMembers.GetDeadIdCardsByApplicationIdAsync(applicationId, ct);
            if (deathResult.IsFailure)
            {
                logger.Warn(
                    $"动态管理死亡成员过滤失败: ApplicationId={applicationId}, " +
                    $"{deathResult.ErrorCode} {deathResult.Message}");
                return Result.Failure<ChangeManagementDetailDto>(
                    deathResult.ErrorCode!, deathResult.Message!);
            }

            var deathIdCards = new HashSet<string>(
                deathResult.Value ?? [], StringComparer.OrdinalIgnoreCase);

            var allMembers = (membersResult.Value ?? [])
                .Where(m => !deathIdCards.Contains((m.IdCard ?? string.Empty).Trim()))
                .OrderByDescending(m => IsHeadMember(m))
                .ThenBy(m => m.Id)
                .ToList();

            var householdMembers = allMembers
                .Where(m => !string.Equals(
                    m.MemberCategory, MemberCategoryConstants.SUPPORT,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            var headResolution = HouseholdHeadResolver.Resolve(application, householdMembers);
            var familySize = householdMembers.Count
                + (headResolution.Head is not null && headResolution.IsVirtual ? 1 : 0);

            var graceResult = await gracePeriods.GetActiveAsync(applicationId, ct);
            if (graceResult.IsFailure)
            {
                logger.Warn(
                    $"动态管理渐退期加载失败: ApplicationId={applicationId}, " +
                    $"{graceResult.ErrorCode} {graceResult.Message}");
                return Result.Failure<ChangeManagementDetailDto>(
                    graceResult.ErrorCode!, graceResult.Message!);
            }

            var monthlyResult = await changes.GetThisMonthChangeEntriesAsync(applicationId, ct);
            var monthlyEntries = new List<MonthlyChangeEntry>();
            if (monthlyResult.IsSuccess && monthlyResult.Value is { } entries)
            {
                monthlyEntries = entries;
            }

            if (monthlyResult.IsFailure)
            {
                logger.Warn(
                    $"动态管理本月已办加载失败（提示按未办展示）: ApplicationId={applicationId}, " +
                    $"{monthlyResult.ErrorCode} {monthlyResult.Message}");
            }

            var dictionaries = await LoadDictionariesAsync(
                database, logger, DetailDictionaryCategories, ct);

            decimal? subsistenceStandard = null;
            var standardResult = await standards.GetActiveIncomeStandardsAsync(ct);
            if (standardResult.IsSuccess)
            {
                var matched = (standardResult.Value ?? [])
                    .FirstOrDefault(s => string.Equals(
                        s.HukouType, application.HukouType, StringComparison.OrdinalIgnoreCase));
                matched ??= (standardResult.Value ?? []).FirstOrDefault();
                subsistenceStandard = matched?.MonthlyStandard;
            }
            else
            {
                logger.Warn($"动态管理低保标准读取失败: {standardResult.ErrorCode} {standardResult.Message}");
            }

            var activeGrace = graceResult.Value;
            var grace = activeGrace is null
                ? null
                : new ChangeGraceInfoDto(
                    activeGrace.Id,
                    activeGrace.GracePeriodMonths,
                    activeGrace.StartDate,
                    activeGrace.EndDate,
                    activeGrace.OriginalClassification ?? string.Empty,
                    ClassificationConstants.ConvertFromCode(activeGrace.OriginalClassification ?? string.Empty),
                    activeGrace.OriginalGuaranteeAmount,
                    activeGrace.GraceGrantAmount,
                    activeGrace.EndDate is { } graceEnd
                        ? (graceEnd.Date - DateTime.Today).Days
                        : null,
                    activeGrace.IsActive);

            var stopped = string.Equals(
                application.Status, ApplicationStatusCodes.STOPPED, StringComparison.OrdinalIgnoreCase);
            var active = !stopped && (
                string.Equals(application.Status, ApplicationStatusCodes.APPROVED, StringComparison.OrdinalIgnoreCase)
                || string.Equals(application.Status, ApplicationStatusCodes.COMPLETED, StringComparison.OrdinalIgnoreCase));

            var memberDtos = new List<ChangeMemberDto>(allMembers.Count);
            foreach (var member in allMembers)
            {
                memberDtos.Add(MapMember(member, dictionaries));
            }

            var head = headResolution.Head;
            var headInfo = new ChangeHeadInfoDto(
                head?.Id ?? 0,
                head?.Name ?? application.ApplicantName ?? string.Empty,
                head?.IdCard ?? application.ApplicantIdCard ?? string.Empty,
                DataMasker.MaskIdCard(head?.IdCard ?? application.ApplicantIdCard ?? string.Empty),
                headResolution.IsVirtual);

            return Result.Success(new ChangeManagementDetailDto(
                application.Id,
                application.ApplicationNo ?? string.Empty,
                application.ApplicantName ?? string.Empty,
                application.ApplicantIdCard ?? string.Empty,
                DataMasker.MaskIdCard(application.ApplicantIdCard ?? string.Empty),
                application.ApplicantPhone ?? string.Empty,
                application.Gender ?? string.Empty,
                application.HukouType ?? string.Empty,
                PageDefaultValues.GetDisplay(
                    PageDefaultValues.HukouTypeOptions,
                    PageDefaultValues.HukouTypeCodes,
                    application.HukouType ?? string.Empty),
                application.Town ?? string.Empty,
                application.Community ?? string.Empty,
                application.Address ?? string.Empty,
                application.ClassificationResult ?? string.Empty,
                ClassificationConstants.ConvertFromCode(application.ClassificationResult ?? string.Empty),
                application.Status ?? string.Empty,
                ApplicationQueryService.ToStatusText(application.Status),
                stopped,
                familySize,
                application.HouseholdMonthlyGuaranteeAmount,
                application.ClassifiedSubsidyAmount,
                application.CaregiverSubsidyAmount,
                application.TotalGuaranteeAmount,
                application.TotalAnnualIncome,
                application.PerCapitaAnnualIncome,
                application.TotalFamilyIncome,
                application.PerCapitaIncome,
                subsistenceStandard,
                application.FirstApprovedAt,
                application.CreatedAt,
                canManage,
                active && familySize > 1,
                active && familySize > 1,
                active,
                active && grace is not null,
                active,
                new ChangeMonthlyFlagsDto(
                    monthlyEntries.Any(e =>
                        string.Equals(e.ChangeType, DictionaryConstants.ChangeType.FUND_CHANGE, StringComparison.Ordinal)
                        && string.Equals(e.ChangeReasonType, ChangeReasonTypeConstants.EconomicReview, StringComparison.Ordinal)),
                    monthlyEntries.Any(e => e.ChangeType is
                        DictionaryConstants.ChangeType.MEMBER_ADD
                        or DictionaryConstants.ChangeType.MEMBER_REMOVE
                        or DictionaryConstants.ChangeType.MEMBER_MODIFY
                        or DictionaryConstants.ChangeType.MEMBER_DEATH
                        or DictionaryConstants.ChangeType.MEMBER_ATTRIBUTE),
                    monthlyEntries.Any(e => string.Equals(
                        e.ChangeType, DictionaryConstants.ChangeType.HOUSEHOLD_HEAD_CHANGE, StringComparison.Ordinal)),
                    monthlyEntries.Any(e => string.Equals(
                        e.ChangeType, DictionaryConstants.ChangeType.HOUSEHOLD_DEATH, StringComparison.Ordinal))),
                grace,
                headInfo,
                memberDtos));
        }
        catch (Exception ex)
        {
            logger.Error($"查询动态管理详情异常: ApplicationId={applicationId}, {ex.Message}");
            return Result.Failure<ChangeManagementDetailDto>(
                ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    // ───────────────────────────── 变更历史 ─────────────────────────────

    public static async Task<Result<ChangeHistoryResponse>> GetHistoryAsync(
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IChangeService changes,
        ILoggerService logger,
        int userId,
        long applicationId,
        CancellationToken ct = default)
    {
        try
        {
            var scopeResult = await GracePeriodQueryService.IsApplicationInScopeAsync(
                database, dataScope, logger, userId, applicationId, ct);
            if (scopeResult.IsFailure)
            {
                return Result.Failure<ChangeHistoryResponse>(
                    scopeResult.ErrorCode!, scopeResult.Message!);
            }

            if (!scopeResult.Value)
            {
                return Result.Failure<ChangeHistoryResponse>(
                    ErrorCodes.NOT_FOUND, "申请档案不存在或不在数据范围内");
            }

            var result = await changes.GetChangeHistoryAsync(applicationId, ct);
            if (result.IsFailure)
            {
                logger.Warn(
                    $"动态管理变更历史失败: ApplicationId={applicationId}, " +
                    $"{result.ErrorCode} {result.Message}");
                return Result.Failure<ChangeHistoryResponse>(
                    result.ErrorCode!, result.Message!);
            }

            var items = (result.Value ?? [])
                .Select(record => new ChangeHistoryItemDto(
                    record.Id,
                    record.ChangeNo,
                    record.ChangeType,
                    record.ChangeTypeDisplay,
                    record.ChangeReason,
                    record.ChangeReasonType,
                    record.ChangeDate,
                    record.OperatorName,
                    record.ChangedAt))
                .ToList();

            return Result.Success(new ChangeHistoryResponse(items));
        }
        catch (Exception ex)
        {
            logger.Error($"查询动态管理变更历史异常: ApplicationId={applicationId}, {ex.Message}");
            return Result.Failure<ChangeHistoryResponse>(
                ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    // ───────────────────────────── 筛选选项 ─────────────────────────────

    public static async Task<Result<ChangeManagementFiltersResponse>> GetFiltersAsync(
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        ILoggerService logger,
        int userId,
        CancellationToken ct = default)
    {
        try
        {
            var scope = await dataScope.BuildAsync(
                userId, DataScopeTarget.Applications, "a", 2, ct);
            var scopeSql = scope.IsEmpty ? string.Empty : " AND " + scope.Sql;

            var townParameters = new List<object?> { ActiveStatuses };
            townParameters.AddRange(scope.Parameters);
            var townsResult = await database.QueryAsync<ChangeTownRow>(
                "SELECT DISTINCT a.town FROM nc_biz_applications a " +
                "WHERE a.deleted_at IS NULL AND a.status = ANY($1) " +
                "AND a.town IS NOT NULL AND a.town <> ''" + scopeSql +
                " ORDER BY a.town LIMIT 200",
                ct,
                townParameters.ToArray());
            if (townsResult.IsFailure)
            {
                logger.Warn($"动态管理乡镇选项读取失败: {townsResult.ErrorCode} {townsResult.Message}");
                return Result.Failure<ChangeManagementFiltersResponse>(
                    townsResult.ErrorCode!, townsResult.Message!);
            }

            var classificationParameters = new List<object?> { ActiveStatuses };
            classificationParameters.AddRange(scope.Parameters);
            var classificationsResult = await database.QueryAsync<ChangeClassificationRow>(
                "SELECT DISTINCT a.classification_result FROM nc_biz_applications a " +
                "WHERE a.deleted_at IS NULL AND a.status = ANY($1) " +
                "AND a.classification_result IS NOT NULL AND a.classification_result <> ''" + scopeSql +
                " LIMIT 100",
                ct,
                classificationParameters.ToArray());
            if (classificationsResult.IsFailure)
            {
                logger.Warn(
                    $"动态管理分类选项读取失败: {classificationsResult.ErrorCode} {classificationsResult.Message}");
                return Result.Failure<ChangeManagementFiltersResponse>(
                    classificationsResult.ErrorCode!, classificationsResult.Message!);
            }

            var dictionaries = await LoadDictionariesAsync(
                database, logger, FilterDictionaryCategories, ct);

            var classifications = (classificationsResult.Value ?? [])
                .Select(row => row.ClassificationResult ?? string.Empty)
                .Where(code => code.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Select(code => new ChangeClassificationOptionDto(
                    code,
                    ClassificationOptionLabel(code)))
                .OrderBy(option => option.Name, StringComparer.Ordinal)
                .ThenBy(option => option.Code, StringComparer.Ordinal)
                .ToList();

            var towns = (townsResult.Value ?? [])
                .Select(row => row.Town ?? string.Empty)
                .Where(name => name.Length > 0)
                .ToList();

            return Result.Success(new ChangeManagementFiltersResponse(
                towns,
                classifications,
                Options(dictionaries, DictionaryTypeCodes.DeathReasons),
                Options(dictionaries, DictionaryTypeCodes.MemberAddReasons),
                Options(dictionaries, DictionaryTypeCodes.ChangeReasons),
                Options(dictionaries, DictionaryTypeCodes.FamilyRelationships),
                Options(dictionaries, DictionaryTypeCodes.HealthStatuses),
                Options(dictionaries, DictionaryTypeCodes.EmploymentStatuses),
                Options(dictionaries, DictionaryTypeCodes.EducationLevels),
                Options(dictionaries, DictionaryTypeCodes.MaritalStatuses),
                Options(dictionaries, DictionaryTypeCodes.PoliticalStatuses),
                Options(dictionaries, DictionaryTypeCodes.LaborAbilities),
                Options(dictionaries, DictionaryTypeCodes.HukouTypes),
                MemberCategoryOptions(),
                GenderOptions()));
        }
        catch (Exception ex)
        {
            logger.Error($"查询动态管理筛选选项异常: {ex.Message}");
            return Result.Failure<ChangeManagementFiltersResponse>(
                ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    // ───────────────────────────── 共用 ─────────────────────────────

    /// <summary>成员是否户主（成员实体 IsHouseholdHead 由服务层镜像 IsApplicant，另兼容历史关系值）。</summary>
    public static bool IsHeadMember(FamilyMember member)
        => member.IsHouseholdHead
           || member.IsApplicant
           || string.Equals(member.MemberCategory, MemberCategoryConstants.HOUSEHOLD_HEAD, StringComparison.OrdinalIgnoreCase)
           // 关系列可能是历史中文（本人/户主、本人）也可能是字典 key（Head），统一归一后比较。
           || string.Equals(
               DictValueMapper.ToCoreRelationship(member.RelationshipToHead),
               DictionaryConstants.FamilyRelationship.HEAD,
               StringComparison.Ordinal);

    public static string MemberCategoryText(string? code) => (code ?? string.Empty).Trim() switch
    {
        MemberCategoryConstants.HOUSEHOLD_HEAD => MemberCategoryConstants.HOUSEHOLD_HEAD_DISPLAY,
        MemberCategoryConstants.SUPPORT => MemberCategoryConstants.SUPPORT_DISPLAY,
        MemberCategoryConstants.SHARED_LIVING => MemberCategoryConstants.SHARED_LIVING_DISPLAY,
        "" => string.Empty,
        var value => value
    };

    private static ChangeMemberDto MapMember(
        FamilyMember member,
        IReadOnlyDictionary<string, Dictionary<string, string>> dictionaries)
    {
        var relationship = member.RelationshipToHead ?? string.Empty;
        var health = member.HealthStatus ?? string.Empty;
        var employment = member.EmploymentStatus ?? string.Empty;
        var workCapacity = member.WorkCapacity ?? string.Empty;
        var education = member.EducationLevel ?? string.Empty;
        var marital = member.MaritalStatus ?? string.Empty;
        var political = member.PoliticalStatus ?? string.Empty;

        return new ChangeMemberDto(
            member.Id,
            member.Name ?? string.Empty,
            member.IdCard ?? string.Empty,
            DataMasker.MaskIdCard(member.IdCard ?? string.Empty),
            relationship,
            Display(dictionaries, DictionaryTypeCodes.FamilyRelationships, relationship),
            member.Gender ?? string.Empty,
            ResolveAge(member),
            member.BirthDate == default ? null : member.BirthDate,
            health,
            Display(dictionaries, DictionaryTypeCodes.HealthStatuses, health),
            employment,
            Display(dictionaries, DictionaryTypeCodes.EmploymentStatuses, employment),
            workCapacity,
            Display(dictionaries, DictionaryTypeCodes.LaborAbilities, workCapacity),
            member.Phone ?? string.Empty,
            member.WorkUnit ?? string.Empty,
            education,
            Display(dictionaries, DictionaryTypeCodes.EducationLevels, education),
            marital,
            Display(dictionaries, DictionaryTypeCodes.MaritalStatuses, marital),
            political,
            Display(dictionaries, DictionaryTypeCodes.PoliticalStatuses, political),
            member.MemberCategory ?? string.Empty,
            MemberCategoryText(member.MemberCategory),
            IsHeadMember(member),
            member.IsApplicant,
            string.Equals(
                member.MemberCategory, MemberCategoryConstants.SUPPORT, StringComparison.OrdinalIgnoreCase));
    }

    public static int? ResolveAge(FamilyMember member)
    {
        if (member.Age is { } age && age > 0)
        {
            return age;
        }

        if (member.BirthDate == default)
        {
            return null;
        }

        var today = DateTime.Today;
        var years = today.Year - member.BirthDate.Year;
        if (member.BirthDate.Date.AddYears(years) > today)
        {
            years--;
        }

        return years is >= 0 and <= 150 ? years : null;
    }

    private static string Display(
        IReadOnlyDictionary<string, Dictionary<string, string>> dictionaries,
        string category,
        string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        return dictionaries.TryGetValue(category, out var items)
               && items.TryGetValue(code, out var text)
            ? text
            : code;
    }

    private static IReadOnlyList<ChangeDictOptionDto> Options(
        IReadOnlyDictionary<string, Dictionary<string, string>> dictionaries,
        string category)
    {
        if (!dictionaries.TryGetValue(category, out var items))
        {
            return [];
        }

        return items
            .Select(pair => new ChangeDictOptionDto(pair.Key, pair.Value))
            .ToList();
    }

    private static IReadOnlyList<ChangeDictOptionDto> MemberCategoryOptions() =>
    [
        new(MemberCategoryConstants.SHARED_LIVING, MemberCategoryConstants.SHARED_LIVING_DISPLAY),
        new(MemberCategoryConstants.SUPPORT, MemberCategoryConstants.SUPPORT_DISPLAY)
    ];

    private static IReadOnlyList<ChangeDictOptionDto> GenderOptions() =>
        PageDefaultValues.GenderCodes
            .Select((code, index) => new ChangeDictOptionDto(
                code,
                index < PageDefaultValues.GenderOptions.Length
                    ? PageDefaultValues.GenderOptions[index]
                    : code))
            .ToList();

    /// <summary>
    /// 一次批查多分类字典（category = ANY($1)），保持 sort_order 顺序；
    /// 字典读取失败时降级为空映射（显示原码），不阻断档案查询本身。
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, Dictionary<string, string>>> LoadDictionariesAsync(
        IDatabaseService database,
        ILoggerService logger,
        IReadOnlyCollection<string> categories,
        CancellationToken ct)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (categories.Count == 0)
        {
            return result;
        }

        var query = await database.QueryAsync<ChangeDictRow>(
            "SELECT category, item_key, item_value, sort_order FROM nc_dict_items " +
            "WHERE category = ANY($1) AND is_active " +
            "ORDER BY category, sort_order, id LIMIT 500",
            ct,
            categories.ToArray());
        if (query.IsFailure)
        {
            logger.Warn($"动态管理字典读取失败，按原码展示: {query.ErrorCode} {query.Message}");
            return result;
        }

        foreach (var row in query.Value ?? [])
        {
            var category = (row.Category ?? string.Empty).Trim();
            var key = (row.ItemKey ?? string.Empty).Trim();
            if (category.Length == 0 || key.Length == 0)
            {
                continue;
            }

            if (!result.TryGetValue(category, out var items))
            {
                items = new Dictionary<string, string>(StringComparer.Ordinal);
                result[category] = items;
            }

            var itemValue = (row.ItemValue ?? string.Empty).Trim();
            items[key] = itemValue.Length > 0 ? itemValue : key;
        }

        return result;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>筛选下拉用：同一对外名称的农村/城市码加后缀区分，避免两个选项同名。</summary>
    private static string ClassificationOptionLabel(string code)
    {
        var name = ClassificationConstants.ConvertFromCode(code);
        var scope = code.StartsWith("Rural", StringComparison.Ordinal)
            ? "农村"
            : code.StartsWith("Urban", StringComparison.Ordinal)
                ? "城市"
                : string.Empty;
        return scope.Length == 0 ? name : $"{name}（{scope}）";
    }

    private static string ToLikePattern(string keyword)
    {
        var escaped = keyword
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }
}

/// <summary>列表行（Core 反射映射需要 public 可写属性与无参构造）。</summary>
public sealed class ChangeListRow
{
    public long ApplicationId { get; set; }
    public string? ApplicationNo { get; set; }
    public string? ApplicantName { get; set; }
    public string? ApplicantIdCard { get; set; }
    public string? Town { get; set; }
    public string? Village { get; set; }
    public string? ClassificationResult { get; set; }
    public int FamilySize { get; set; }
    public decimal HouseholdMonthlyGuaranteeAmount { get; set; }
    public decimal TotalGuaranteeAmount { get; set; }
    public decimal PerCapitaAnnualIncome { get; set; }
    public string? Status { get; set; }
    public DateTime? GracePeriodEndDate { get; set; }
    public int? GracePeriodMonths { get; set; }
}

public sealed class ChangeTownRow
{
    public string? Town { get; set; }
}

public sealed class ChangeClassificationRow
{
    public string? ClassificationResult { get; set; }
}

public sealed class ChangeDictRow
{
    public string? Category { get; set; }
    public string? ItemKey { get; set; }
    public string? ItemValue { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>导入库来源行 → 当前库档案 ID（建档状态角标用）。</summary>
public sealed class ExistingApplicationRow
{
    public string? SourceTable { get; set; }
    public long SourceId { get; set; }
    public long ApplicationId { get; set; }
}
