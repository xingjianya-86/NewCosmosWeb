namespace NewCosmos.Server.Contracts;

#region 临时救助：列表 / 统计

/// <summary>临时救助列表项。身份证按 Web 端安全口径脱敏，完整证件只在详情返回。</summary>
public sealed record TempReliefListItemDto(
    long Id,
    string ApplicationNo,
    string ApplicantName,
    string ApplicantIdCardMasked,
    string ReliefType,
    string ReliefTypeText,
    string FamilyCategory,
    string Town,
    string Village,
    DateTime? ApplyDate,
    decimal? ConfirmAmount,
    string Status,
    string StatusText,
    DateTime? StoppedAt,
    string StopReason,
    string ReportUnit,
    DateTime? CreatedAt);

public sealed record TempReliefListResponse(
    IReadOnlyList<TempReliefListItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

/// <summary>
/// 页头统计：办理中 / 已确认累计 / 已终止 / 本年确认人次 / 本年确认金额。
/// 单项失败为 null，前端显示「—」，不伪造 0。
/// </summary>
public sealed record TempReliefSummaryResponse(
    int? DraftCount,
    int? ConfirmedCount,
    int? StoppedCount,
    int? YearConfirmed,
    decimal? YearConfirmedAmount);

public sealed record TempReliefCreatedResponse(long Id);

#endregion

#region 临时救助：表单详情

public sealed record TempReliefMemberDto(
    long Id,
    string MemberName,
    string Gender,
    string Relation,
    string IdCard,
    string WorkUnit,
    decimal? AnnualIncome,
    int SortOrder);

public sealed record TempReliefDiseaseDto(
    long Id,
    string DiseaseName,
    string DiseaseCode,
    string MemberName,
    string Hospital,
    DateTime? TreatStartDate,
    DateTime? TreatEndDate,
    decimal? MedicalTotal,
    decimal? InsurancePaid,
    decimal? SelfPaid,
    int SortOrder);

public sealed record TempReliefAccidentDto(
    long Id,
    string AccidentType,
    string MemberName,
    DateTime? HappenDate,
    string HappenPlace,
    string InjurySituation,
    decimal? PropertyLoss,
    decimal? CompensationPaid,
    string ResponsibilityDesc,
    string MaterialDesc,
    int SortOrder);

public sealed record TempReliefEducationDto(
    long Id,
    string StudentName,
    string EducationStage,
    string SchoolName,
    int? SchoolDuration,
    decimal? TuitionFee,
    DateTime? FeeDate,
    int SortOrder);

/// <summary>临时救助完整档案（主表 + 家庭成员 + 疾病 / 意外灾害 / 教育支出明细）。</summary>
public sealed record TempReliefDetailResponse(
    long Id,
    string ApplicationNo,
    string ReliefType,
    string ReliefTypeText,
    string ApplyYear,
    string SourceType,
    string SourceTable,
    long SourceFamilyId,
    string HukouType,
    string ApplicantName,
    string ApplicantIdCard,
    string Gender,
    int? Age,
    string Phone,
    string? BeneficiaryName,
    string? BeneficiaryIdCard,
    string? BeneficiaryGender,
    int? BeneficiaryAge,
    string? BeneficiaryRelation,
    string FamilyProvince,
    string FamilyCity,
    string FamilyDistrict,
    string FamilyTown,
    string FamilyDetail,
    string FamilyAddress,
    string HukouProvince,
    string HukouCity,
    string HukouDistrict,
    string HukouTown,
    string HukouAddress,
    string Town,
    string Village,
    int? FamilySize,
    string FamilyCategory,
    string BankName,
    string BankAccount,
    string PolicyEnjoyed,
    string FamilyMemberStatus,
    string DifficultyReason,
    string DifficultyType,
    DateTime? ApplyDate,
    string ReportUnit,
    DateTime? ReportTime,
    string SmallAmountLevel,
    decimal? ConfirmAmount,
    DateTime? PublicizeStartDate,
    DateTime? PublicizeEndDate,
    string VerifyResult,
    string AcceptancePerson,
    string Status,
    string StatusText,
    DateTime? ConfirmedAt,
    string ConfirmedBy,
    DateTime? StoppedAt,
    string StoppedBy,
    string StopReason,
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<TempReliefMemberDto> Members,
    IReadOnlyList<TempReliefDiseaseDto> Diseases,
    IReadOnlyList<TempReliefAccidentDto> Accidents,
    IReadOnlyList<TempReliefEducationDto> Educations);

#endregion

#region 临时救助：候选人 / 回填辅助

public sealed record TempReliefMemberSnapshotDto(
    string MemberName,
    string Gender,
    string Relation,
    string IdCard,
    string WorkUnit,
    decimal? AnnualIncome);

public sealed record TempReliefCandidateDto(
    long SourceFamilyId,
    string SourceTable,
    string SourceName,
    string HukouType,
    string Name,
    string IdCard,
    string Gender,
    string Phone,
    string Province,
    string City,
    string District,
    string DetailAddress,
    string FamilyAddress,
    string HukouAddress,
    string Town,
    string Village,
    int? FamilySize,
    string FamilyCategory,
    string BankName,
    string BankAccount,
    string OneCardAccount,
    string MatchedPersonInfo,
    string MatchedMemberIdCard,
    bool HasAppliedThisYear,
    IReadOnlyList<TempReliefMemberSnapshotDto> Members);

public sealed record TempReliefCandidateListResponse(IReadOnlyList<TempReliefCandidateDto> Items);

public sealed record TempReliefHukouTypeResponse(string HukouType);

public sealed record TempReliefSmallAmountLevelDto(
    string StandardType,
    string StandardName,
    decimal StandardValue,
    string Unit,
    string HukouType);

public sealed record TempReliefSmallAmountLevelsResponse(
    IReadOnlyList<TempReliefSmallAmountLevelDto> Levels);

public sealed record TempReliefPolicySnapshotResponse(IReadOnlyList<string> Items);

public sealed record TempReliefFamilyStatusResponse(string StatusText);

public sealed record TempReliefAnnualCheckResponse(bool Exists);

public sealed record TempReliefReportUnitPhoneResponse(string Phone);

#endregion

#region 临时救助：保存请求

public sealed class TempReliefMemberInput
{
    public long Id { get; set; }
    public string? MemberName { get; set; }
    public string? Gender { get; set; }
    public string? Relation { get; set; }
    public string? IdCard { get; set; }
    public string? WorkUnit { get; set; }
    public decimal? AnnualIncome { get; set; }
    public int SortOrder { get; set; }
}

public sealed class TempReliefDiseaseInput
{
    public long Id { get; set; }
    public string? DiseaseName { get; set; }
    public string? DiseaseCode { get; set; }
    public string? MemberName { get; set; }
    public string? Hospital { get; set; }
    public DateTime? TreatStartDate { get; set; }
    public DateTime? TreatEndDate { get; set; }
    public decimal? MedicalTotal { get; set; }
    public decimal? InsurancePaid { get; set; }
    public decimal? SelfPaid { get; set; }
    public int SortOrder { get; set; }
}

public sealed class TempReliefAccidentInput
{
    public long Id { get; set; }
    public string? AccidentType { get; set; }
    public string? MemberName { get; set; }
    public DateTime? HappenDate { get; set; }
    public string? HappenPlace { get; set; }
    public string? InjurySituation { get; set; }
    public decimal? PropertyLoss { get; set; }
    public decimal? CompensationPaid { get; set; }
    public string? ResponsibilityDesc { get; set; }
    public string? MaterialDesc { get; set; }
    public int SortOrder { get; set; }
}

public sealed class TempReliefEducationInput
{
    public long Id { get; set; }
    public string? StudentName { get; set; }
    public string? EducationStage { get; set; }
    public string? SchoolName { get; set; }
    public int? SchoolDuration { get; set; }
    public decimal? TuitionFee { get; set; }
    public DateTime? FeeDate { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// 临时救助保存请求（创建 / 更新共用）。
/// 申请人身份字段仅用于服务端与来源台账复核；金额上限/档位由服务端按 Core 配置标准重新确认。
/// </summary>
public sealed class TempReliefSaveRequest
{
    public string? ReliefType { get; set; }
    public string? SourceTable { get; set; }
    public long SourceFamilyId { get; set; }
    public string? HukouType { get; set; }
    public string? ApplicantName { get; set; }
    public string? ApplicantIdCard { get; set; }
    public string? Gender { get; set; }
    public int? Age { get; set; }
    public string? Phone { get; set; }
    public string? BeneficiaryName { get; set; }
    public string? BeneficiaryIdCard { get; set; }
    public string? BeneficiaryGender { get; set; }
    public int? BeneficiaryAge { get; set; }
    public string? BeneficiaryRelation { get; set; }
    public string? FamilyProvince { get; set; }
    public string? FamilyCity { get; set; }
    public string? FamilyDistrict { get; set; }
    public string? FamilyTown { get; set; }
    public string? FamilyDetail { get; set; }
    public string? FamilyAddress { get; set; }
    public string? HukouProvince { get; set; }
    public string? HukouCity { get; set; }
    public string? HukouDistrict { get; set; }
    public string? HukouTown { get; set; }
    public string? HukouAddress { get; set; }
    public string? Town { get; set; }
    public string? Village { get; set; }
    public int? FamilySize { get; set; }
    public string? FamilyCategory { get; set; }
    public string? BankName { get; set; }
    public string? BankAccount { get; set; }
    public string? PolicyEnjoyed { get; set; }
    public string? FamilyMemberStatus { get; set; }
    public string? DifficultyReason { get; set; }
    public string? DifficultyType { get; set; }
    public DateTime? ApplyDate { get; set; }
    public string? ReportUnit { get; set; }
    public DateTime? ReportTime { get; set; }
    public string? SmallAmountLevel { get; set; }
    public decimal? ConfirmAmount { get; set; }
    public DateTime? PublicizeStartDate { get; set; }
    public DateTime? PublicizeEndDate { get; set; }
    public string? VerifyResult { get; set; }
    public string? AcceptancePerson { get; set; }
    public List<TempReliefMemberInput>? Members { get; set; }
    public List<TempReliefDiseaseInput>? Diseases { get; set; }
    public List<TempReliefAccidentInput>? Accidents { get; set; }
    public List<TempReliefEducationInput>? Educations { get; set; }
}

public sealed class TempReliefStopRequest
{
    public string? Reason { get; set; }
}

#endregion
