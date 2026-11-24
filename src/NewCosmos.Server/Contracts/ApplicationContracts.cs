namespace NewCosmos.Server.Contracts;

#region 申请（P1 只读）

/// <summary>
/// 申请列表卡片。身份证/手机号在此层强制脱敏；完整身份证只在详情返回。
/// </summary>
public sealed record ApplicationListItemDto(
    long Id,
    string ApplicationNo,
    string ApplicantName,
    string ApplicantIdCardMasked,
    string ApplicantPhone,
    string Status,
    string StatusText,
    int CurrentStep,
    int FamilySize,
    string? ClassificationResult,
    decimal TotalAnnualIncome,
    decimal PerCapitaAnnualIncome,
    string Town,
    string Village,
    string Address,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ApplicationListResponse(
    IReadOnlyList<ApplicationListItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

/// <summary>申请详情（详情页/表单回填）；身份证全量仅限本接口。</summary>
public sealed record ApplicationDetailDto(
    long Id,
    string ApplicationNo,
    string Status,
    string StatusText,
    int CurrentStep,
    string ApplicantName,
    string ApplicantIdCard,
    string ApplicantPhone,
    string HukouType,
    string Gender,
    string Ethnicity,
    string HukouAddress,
    int? TownId,
    int? VillageId,
    string Town,
    string Village,
    string Address,
    int FamilySize,
    string? ClassificationResult,
    decimal TotalAnnualIncome,
    decimal PerCapitaAnnualIncome,
    decimal TotalFamilyIncome,
    decimal PerCapitaIncome,
    decimal HouseholdMonthlyGuaranteeAmount,
    string ApplicationReason,
    string ApplicationReasonDetail,
    DateTime? SubmitAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ApplicationMemberDto> Members);

/// <summary>家庭成员；birthDate 只输出日期部分，age 由 birthDate 计算周岁。</summary>
public sealed record ApplicationMemberDto(
    long Id,
    string Name,
    string IdCard,
    string Relation,
    string Gender,
    DateOnly? BirthDate,
    int? Age,
    string HealthStatus,
    string EmploymentStatus,
    decimal MonthlyIncome);

#endregion
