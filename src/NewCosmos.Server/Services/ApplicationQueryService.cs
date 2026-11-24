using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Services;

/// <summary>
/// P1 申请只读查询：列表分页/关键字筛选 + 详情（含家庭成员）。
/// SQL 全部使用 $1..$n 参数，列表显式列取 ApplicationColumns.Full，禁用 SELECT *。
/// </summary>
public sealed class ApplicationQueryService(IDatabaseService database)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private const string ListSqlPrefix =
        "SELECT " + ApplicationColumns.Full + " FROM nc_biz_applications a";

    /// <summary>详情只取详情页真正消费的列（submit_at 需保持可空，故不套用 Full 实体映射）。</summary>
    private const string DetailSql =
        @"SELECT a.id, a.application_no, a.status, a.current_step, a.applicant_name, a.applicant_id_card,
                 a.applicant_phone, a.hukou_type, a.gender, a.ethnicity, a.hukou_address, a.town_id, a.village_id,
                 a.town, a.community AS village, a.address, a.family_size, a.classification_result, a.total_annual_income,
                 a.per_capita_annual_income, a.total_family_income, a.per_capita_income,
                 a.household_monthly_guarantee_amount, a.application_reason, a.application_reason_detail,
                 a.submit_at, a.created_at, a.updated_at
          FROM nc_biz_applications a
          WHERE a.id = $1 AND a.deleted_at IS NULL";

    private const string MemberSql =
        @"SELECT id, name, id_card, relationship_to_head, gender, birth_date,
                 health_status, employment_status, monthly_income_capacity
          FROM nc_biz_family_members
          WHERE application_id = $1 AND deleted_at IS NULL
          ORDER BY id ASC";

    /// <summary>不套数据范围的旧重载，保留给内部/测试调用。</summary>
    public Task<Result<ApplicationListResponse>> ListAsync(
        int page,
        int pageSize,
        string? keyword,
        string? status,
        CancellationToken ct = default)
        => ListAsync(page, pageSize, keyword, status, null, ct);

    /// <summary>
    /// 带数据范围的列表查询。scope.Sql 的占位符编号必须与
    /// <see cref="CountFilterParameters"/> 推算出的起始序号一致（由端点构造）。
    /// </summary>
    public async Task<Result<ApplicationListResponse>> ListAsync(
        int page,
        int pageSize,
        string? keyword,
        string? status,
        DataScopeClause? scope,
        CancellationToken ct = default)
    {
        var pageIndex = Math.Max(page, 1);
        var effectivePageSize = Math.Clamp(
            pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);
        var filter = BuildFilter(Normalize(status), Normalize(keyword), scope);

        var countResult = await database.ExecuteScalarAsync<long?>(
            "SELECT COUNT(*) FROM nc_biz_applications a" + filter.Where,
            ct,
            filter.Parameters.ToArray());

        if (countResult.IsFailure)
        {
            return Result.Failure<ApplicationListResponse>(
                countResult.ErrorCode!, countResult.Message!);
        }

        var totalCount = countResult.Value ?? 0;
        if (totalCount == 0)
        {
            return Result.Success(new ApplicationListResponse(
                [], pageIndex, effectivePageSize, 0, 0));
        }

        // 页数参数位置紧跟在筛选参数之后；offset 用 long 避免大页码 int 溢出。
        var listParameters = new List<object?>(filter.Parameters)
        {
            effectivePageSize,
            (long)(pageIndex - 1) * effectivePageSize
        };
        var limitIndex = filter.Parameters.Count + 1;
        var offsetIndex = filter.Parameters.Count + 2;

        var listSql = $"{ListSqlPrefix}{filter.Where} " +
                      $"ORDER BY a.created_at DESC, a.id DESC LIMIT ${limitIndex} OFFSET ${offsetIndex}";

        var rowsResult = await database.QueryAsync<Application>(listSql, ct, listParameters.ToArray());
        if (rowsResult.IsFailure)
        {
            return Result.Failure<ApplicationListResponse>(
                rowsResult.ErrorCode!, rowsResult.Message!);
        }

        var rows = rowsResult.Value ?? [];
        var items = new List<ApplicationListItemDto>(rows.Count);
        foreach (var row in rows)
        {
            items.Add(new ApplicationListItemDto(
                row.Id,
                row.ApplicationNo,
                row.ApplicantName,
                DataMasker.MaskIdCard(row.ApplicantIdCard),
                DataMasker.MaskPhone(row.ApplicantPhone),
                row.Status,
                ToStatusText(row.Status),
                row.CurrentStep,
                row.FamilySize,
                NullIfEmpty(row.ClassificationResult),
                row.TotalAnnualIncome,
                row.PerCapitaAnnualIncome,
                row.Town,
                row.Community,
                row.Address,
                row.CreatedAt,
                row.UpdatedAt));
        }

        var totalPages = (int)((totalCount + effectivePageSize - 1) / effectivePageSize);
        return Result.Success(new ApplicationListResponse(
            items, pageIndex, effectivePageSize, totalCount, totalPages));
    }

    public Task<Result<ApplicationDetailDto>> GetDetailAsync(
        long id,
        CancellationToken ct = default)
        => GetDetailAsync(id, null, ct);

    /// <summary>带数据范围的详情查询；scope 占位符从 $2 开始（id 固定 $1）。</summary>
    public async Task<Result<ApplicationDetailDto>> GetDetailAsync(
        long id,
        DataScopeClause? scope,
        CancellationToken ct = default)
    {
        var detailSql = DetailSql;
        var detailParameters = new List<object?> { id };
        if (scope is { IsEmpty: false })
        {
            detailSql += " AND " + scope.Sql;
            detailParameters.AddRange(scope.Parameters);
        }

        var detailResult = await database.QuerySingleAsync<ApplicationDetailRow>(
            detailSql, ct, detailParameters.ToArray());
        if (detailResult.IsFailure)
        {
            return Result.Failure<ApplicationDetailDto>(
                detailResult.ErrorCode!, detailResult.Message!);
        }

        var detail = detailResult.Value;
        if (detail is null)
        {
            return Result.Failure<ApplicationDetailDto>(ApiErrorCodes.NotFound, "申请不存在");
        }

        var membersResult = await database.QueryAsync<ApplicationMemberRow>(MemberSql, ct, id);
        if (membersResult.IsFailure)
        {
            return Result.Failure<ApplicationDetailDto>(
                membersResult.ErrorCode!, membersResult.Message!);
        }

        var members = new List<ApplicationMemberDto>();
        foreach (var member in membersResult.Value ?? [])
        {
            var birthDate = member.BirthDate is { Year: >= 1900 } value
                ? DateOnly.FromDateTime(value)
                : (DateOnly?)null;

            members.Add(new ApplicationMemberDto(
                member.Id,
                member.Name ?? string.Empty,
                member.IdCard ?? string.Empty,
                member.RelationshipToHead ?? string.Empty,
                member.Gender ?? string.Empty,
                birthDate,
                CalculateAge(birthDate),
                member.HealthStatus ?? string.Empty,
                member.EmploymentStatus ?? string.Empty,
                member.MonthlyIncomeCapacity ?? 0m));
        }

        return Result.Success(new ApplicationDetailDto(
            detail.Id,
            detail.ApplicationNo ?? string.Empty,
            detail.Status ?? string.Empty,
            ToStatusText(detail.Status),
            detail.CurrentStep,
            detail.ApplicantName ?? string.Empty,
            detail.ApplicantIdCard ?? string.Empty,
            detail.ApplicantPhone ?? string.Empty,
            detail.HukouType ?? string.Empty,
            detail.Gender ?? string.Empty,
            detail.Ethnicity ?? string.Empty,
            detail.HukouAddress ?? string.Empty,
            detail.TownId,
            detail.VillageId,
            detail.Town ?? string.Empty,
            detail.Village ?? string.Empty,
            detail.Address ?? string.Empty,
            detail.FamilySize,
            NullIfEmpty(detail.ClassificationResult),
            detail.TotalAnnualIncome,
            detail.PerCapitaAnnualIncome,
            detail.TotalFamilyIncome,
            detail.PerCapitaIncome,
            detail.HouseholdMonthlyGuaranteeAmount,
            detail.ApplicationReason ?? string.Empty,
            detail.ApplicationReasonDetail ?? string.Empty,
            detail.SubmitAt,
            detail.CreatedAt ?? DateTime.MinValue,
            detail.UpdatedAt ?? DateTime.MinValue,
            members));
    }

    /// <summary>status 映射为中文；未知状态保留数据库原值。</summary>
    public static string ToStatusText(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return string.Empty;
        }

        return status switch
        {
            ApplicationStatusCodes.DRAFT => "草稿",
            ApplicationStatusCodes.SUBMITTED => "已提交",
            ApplicationStatusCodes.APPROVED => "已审批",
            ApplicationStatusCodes.COMPLETED => "已完成",
            ApplicationStatusCodes.STOPPED => "已停保",
            ApplicationStatusCodes.REFUSED => "已驳回",
            _ => status
        };
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 基础筛选（status + keyword）占用的参数个数。端点据此把数据范围占位符
    /// 起始序号算成 <c>CountFilterParameters(...) + 1</c>，避免与关键字参数错位。
    /// </summary>
    public static int CountFilterParameters(string? status, string? keyword)
        => (Normalize(status) is null ? 0 : 1) + (Normalize(keyword) is null ? 0 : 1);

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>周岁：出生日期按自然日计算，未过生日减 1；birthDate 缺失返回 null。</summary>
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

    /// <summary>转义 LIKE 通配符，关键字按字面量匹配（PostgreSQL 默认转义字符为反斜杠）。</summary>
    private static string ToLikePattern(string keyword)
    {
        var escaped = keyword
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static (string Where, List<object?> Parameters) BuildFilter(
        string? status,
        string? keyword,
        DataScopeClause? scope)
    {
        var conditions = new List<string> { "a.deleted_at IS NULL" };
        var parameters = new List<object?>();

        if (status is not null)
        {
            parameters.Add(status);
            conditions.Add($"a.status = ${parameters.Count}");
        }

        if (keyword is not null)
        {
            parameters.Add(ToLikePattern(keyword));
            var index = parameters.Count;
            conditions.Add(
                $"(a.applicant_name ILIKE ${index} OR a.applicant_id_card ILIKE ${index} " +
                "OR EXISTS (SELECT 1 FROM nc_biz_family_members m " +
                "WHERE m.application_id = a.id AND m.deleted_at IS NULL " +
                $"AND (m.name ILIKE ${index} OR m.id_card ILIKE ${index})))");
        }

        if (scope is { IsEmpty: false })
        {
            conditions.Add(scope.Sql);
            parameters.AddRange(scope.Parameters);
        }

        return (" WHERE " + string.Join(" AND ", conditions), parameters);
    }
}

/// <summary>Core 反射映射需要 public 可写属性与公共无参构造函数。</summary>
public sealed class ApplicationDetailRow
{
    public long Id { get; set; }
    public string? ApplicationNo { get; set; }
    public string? Status { get; set; }
    public int CurrentStep { get; set; }
    public string? ApplicantName { get; set; }
    public string? ApplicantIdCard { get; set; }
    public string? ApplicantPhone { get; set; }
    public string? HukouType { get; set; }
    public string? Gender { get; set; }
    public string? Ethnicity { get; set; }
    public string? HukouAddress { get; set; }
    public int? TownId { get; set; }
    public int? VillageId { get; set; }
    public string? Town { get; set; }
    public string? Village { get; set; }
    public string? Address { get; set; }
    public int FamilySize { get; set; }
    public string? ClassificationResult { get; set; }
    public decimal TotalAnnualIncome { get; set; }
    public decimal PerCapitaAnnualIncome { get; set; }
    public decimal TotalFamilyIncome { get; set; }
    public decimal PerCapitaIncome { get; set; }
    public decimal HouseholdMonthlyGuaranteeAmount { get; set; }
    public string? ApplicationReason { get; set; }
    public string? ApplicationReasonDetail { get; set; }
    public DateTime? SubmitAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>家庭成员行；birth_date 由 Npgsql 返回 DateOnly，Core 映射统一转为 DateTime。</summary>
public sealed class ApplicationMemberRow
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? IdCard { get; set; }
    public string? RelationshipToHead { get; set; }
    public string? Gender { get; set; }
    public DateTime? BirthDate { get; set; }
    public string? HealthStatus { get; set; }
    public string? EmploymentStatus { get; set; }
    public decimal? MonthlyIncomeCapacity { get; set; }
}
