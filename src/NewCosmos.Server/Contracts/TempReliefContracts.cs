namespace NewCosmos.Server.Contracts;

#region 临时救助：列表 / 统计

/// <summary>
/// 临时救助列表项。身份证按 Web 端安全口径脱敏，完整证件只在详情返回。
/// 列表覆盖「两大类（在册/非在册）× 三小类（常规·上级 / 常规·备用金 / 小额快救）」展示字段。
/// </summary>
public sealed record TempReliefListItemDto(
    long Id,
    string ApplicationNo,
    string ApplicantName,
    string ApplicantIdCardMasked,
    string ReliefType,
    string ReliefTypeText,
    string PopulationStatus,
    string PopulationStatusText,
    string PaymentChannel,
    string PaymentChannelText,
    string CategoryDisplayName,
    string FamilyCategory,
    string Town,
    string Village,
    DateTime? ApplyDate,
    decimal? ConfirmAmount,
    string Status,
    string StatusText,
    string DeadlineText,
    DateTime? DeadlineDate,
    bool IsOverdue,
    DateTime? StoppedAt,
    string StopReason,
    string ReportUnit,
    DateTime? CreatedAt,
    int ReliefPersonCount,
    string? FilingStatus,
    string FilingStatusText,
    bool CanSubmit,
    bool CanReturn,
    bool CanReject,
    bool CanConfirm,
    bool CanMarkFiled,
    bool CanFile);

public sealed record TempReliefListResponse(
    IReadOnlyList<TempReliefListItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

/// <summary>
/// 页头统计：待办 / 已提交 / 本年确认 / 逾期 / 本年金额。
/// 兼容旧前端字段（办理中 / 已确认累计 / 已终止）；单项失败为 null，前端显示「—」，不伪造 0。
/// </summary>
public sealed record TempReliefSummaryResponse(
    int? DraftCount,
    int? ConfirmedCount,
    int? StoppedCount,
    int? YearConfirmed,
    decimal? YearConfirmedAmount,
    int? PendingCount,
    int? SubmittedCount,
    int? OverdueCount);

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

/// <summary>
/// 临时救助完整档案：主表全部新字段 + 家庭成员 + 疾病 / 意外灾害 / 教育支出明细，
/// 并附服务端计算展示字段（业务口径全部来自 Core，不在端点重写公式）。
/// </summary>
public sealed class TempReliefDetailResponse
{
    public long Id { get; init; }
    public string ApplicationNo { get; init; } = string.Empty;
    public string ReliefType { get; init; } = string.Empty;
    public string ReliefTypeText { get; init; } = string.Empty;
    public string PopulationStatus { get; init; } = string.Empty;
    public string PopulationStatusText { get; init; } = string.Empty;
    public string PaymentChannel { get; init; } = string.Empty;
    public string PaymentChannelText { get; init; } = string.Empty;
    public string CategoryDisplayName { get; init; } = string.Empty;
    public string ApplyYear { get; init; } = string.Empty;
    public string SourceType { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string SourceTable { get; init; } = string.Empty;
    public long SourceFamilyId { get; init; }
    public bool IsManualEntry { get; init; }
    public string HukouType { get; init; } = string.Empty;
    public string ApplicantName { get; init; } = string.Empty;
    public string ApplicantIdCard { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public int? Age { get; init; }
    public string Phone { get; init; } = string.Empty;
    public string? BeneficiaryName { get; init; }
    public string? BeneficiaryIdCard { get; init; }
    public string? BeneficiaryGender { get; init; }
    public int? BeneficiaryAge { get; init; }
    public string? BeneficiaryRelation { get; init; }
    public string FamilyProvince { get; init; } = string.Empty;
    public string FamilyCity { get; init; } = string.Empty;
    public string FamilyDistrict { get; init; } = string.Empty;
    public string FamilyTown { get; init; } = string.Empty;
    public string FamilyDetail { get; init; } = string.Empty;
    public string FamilyAddress { get; init; } = string.Empty;
    public string HukouProvince { get; init; } = string.Empty;
    public string HukouCity { get; init; } = string.Empty;
    public string HukouDistrict { get; init; } = string.Empty;
    public string HukouTown { get; init; } = string.Empty;
    public string HukouAddress { get; init; } = string.Empty;
    public string Town { get; init; } = string.Empty;
    public string Village { get; init; } = string.Empty;
    public int? FamilySize { get; init; }
    public int ReliefPersonCount { get; init; }
    public string FamilyCategory { get; init; } = string.Empty;
    public string BankName { get; init; } = string.Empty;
    public string BankAccount { get; init; } = string.Empty;
    public string PolicyEnjoyed { get; init; } = string.Empty;
    public string FamilyMemberStatus { get; init; } = string.Empty;
    public string DifficultyReason { get; init; } = string.Empty;
    public string DifficultyType { get; init; } = string.Empty;
    public DateTime? ApplyDate { get; init; }
    public string ReportUnit { get; init; } = string.Empty;
    public DateTime? ReportTime { get; init; }
    public string SmallAmountLevel { get; init; } = string.Empty;
    public string? StandardTier { get; init; }
    public string StandardTierLabel { get; init; } = string.Empty;
    public decimal? StandardMultiplier { get; init; }
    public decimal? ConfirmAmount { get; init; }
    public DateTime? PublicizeStartDate { get; init; }
    public DateTime? PublicizeEndDate { get; init; }
    public string PublicizeRangeText { get; init; } = string.Empty;
    public int? PublicizeDays { get; init; }
    public bool PublicizeRequired { get; init; }
    public string VerifyResult { get; init; } = string.Empty;
    public string AcceptancePerson { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public DateTime? AcceptedAt { get; init; }
    public DateTime? InvestigationDoneAt { get; init; }
    public DateTime? DeadlineDate { get; init; }
    public string DeadlineText { get; init; } = string.Empty;
    public int? RemainingDays { get; init; }
    public bool IsOverdue { get; init; }
    public bool CanSubmit { get; init; }
    public bool CanReturn { get; init; }
    public bool CanReject { get; init; }
    public bool CanConfirm { get; init; }
    public bool CanMarkFiled { get; init; }
    /// <summary>可登记备案（与 CanMarkFiled 同义，按任务契约提供 CanFile 别名）。</summary>
    public bool CanFile => CanMarkFiled;
    public bool EconomicCheckRequired { get; init; }
    public bool CountyConfirmRequired { get; init; }
    public bool FilingRequired { get; init; }
    public DateTime? CountyConfirmedAt { get; init; }
    public string CountyConfirmedBy { get; init; } = string.Empty;
    public string? FilingStatus { get; init; }
    public string FilingStatusText { get; init; } = string.Empty;
    public string? RejectReason { get; init; }
    public DateTime? RejectNotifiedAt { get; init; }
    public string? RejectNoticeMethod { get; init; }
    public DateTime? ConfirmedAt { get; init; }
    public string ConfirmedBy { get; init; } = string.Empty;
    public DateTime? StoppedAt { get; init; }
    public string StoppedBy { get; init; } = string.Empty;
    public string StopReason { get; init; } = string.Empty;
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public string UpdatedBy { get; init; } = string.Empty;
    public IReadOnlyList<TempReliefMemberDto> Members { get; init; } = Array.Empty<TempReliefMemberDto>();
    public IReadOnlyList<TempReliefDiseaseDto> Diseases { get; init; } = Array.Empty<TempReliefDiseaseDto>();
    public IReadOnlyList<TempReliefAccidentDto> Accidents { get; init; } = Array.Empty<TempReliefAccidentDto>();
    public IReadOnlyList<TempReliefEducationDto> Educations { get; init; } = Array.Empty<TempReliefEducationDto>();
}

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

/// <summary>在册身份自动判定结果（按身份证反查四类在册台账，人工不可改判）。</summary>
public sealed record TempReliefInsuredIdentityResponse(
    bool IsInsured,
    string PopulationStatus,
    string PopulationStatusText,
    IReadOnlyList<string> LedgerNames,
    string PrimaryLedgerName,
    string BasisText);

public sealed record TempReliefSmallAmountLevelDto(
    string StandardType,
    string StandardName,
    decimal StandardValue,
    string Unit,
    string HukouType);

public sealed record TempReliefSmallAmountLevelsResponse(
    IReadOnlyList<TempReliefSmallAmountLevelDto> Levels);

/// <summary>小额快救政策档位（50% / 75% / 100%），金额上限由 Core 公式计算。</summary>
public sealed record TempReliefSmallTierDto(
    string Code,
    string Label,
    decimal Multiplier,
    decimal StandardValue,
    int PersonCount,
    decimal CeilingAmount);

public sealed record TempReliefSmallTiersResponse(
    decimal BaseStandard,
    int PersonCount,
    IReadOnlyList<TempReliefSmallTierDto> Items);

public sealed record TempReliefPolicySnapshotResponse(IReadOnlyList<string> Items);

public sealed record TempReliefFamilyStatusResponse(string StatusText);

public sealed record TempReliefAnnualCheckResponse(bool Exists);

public sealed record TempReliefReportUnitPhoneResponse(string Phone);

#endregion

#region 临时救助：打印数据 / 照片 / OCR

/// <summary>打印预览数据：Template FieldKeys 字段 + 成员行 + 打印分类。</summary>
public sealed record TempReliefPrintDataResponse(
    long Id,
    string ApplicationNo,
    string ApplicantName,
    string ApplicantIdCardMasked,
    string ReliefType,
    string CategoryDisplayName,
    string Classification,
    DateTime? AcceptanceDate,
    DateTime? InvestigationDate,
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<Dictionary<string, string>> TableRows,
    IReadOnlyList<TempReliefMemberDto> Members);

public sealed record TempReliefPhotoDto(
    long Id,
    string PhotoCategory,
    string PhotoCategoryName,
    string PhotoName,
    string ImageFormat,
    string DataUrl,
    long FileSize,
    DateTime CreatedAt,
    string CreatedByName);

public sealed record TempReliefPhotoListResponse(IReadOnlyList<TempReliefPhotoDto> Items);

public sealed record TempReliefPhotoUploadResponse(
    long Id,
    string PhotoCategory,
    string PhotoName,
    long FileSize);

/// <summary>身份证 OCR 结果（类别仅支持 ID_CARD）。</summary>
public sealed record TempReliefOcrResponse(
    string Category,
    string Name,
    string IdCard,
    string Gender,
    DateTime? BirthDate,
    string Address,
    IReadOnlyDictionary<string, string> Fields);

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
/// 人群状态、发放渠道与金额上限均由 Core 按身份证/政策重新判定，
/// 本请求中的 populationStatus / paymentChannel 仅作表单回显输入，不作为落库依据。
/// </summary>
public sealed class TempReliefSaveRequest
{
    public string? ReliefType { get; set; }

    /// <summary>人群状态（Insured/Uninsured）；服务端以 Core 判定结果为准。</summary>
    public string? PopulationStatus { get; set; }

    /// <summary>发放渠道（County/Reserve）；小额固定 Reserve，由 Core 归一。</summary>
    public string? PaymentChannel { get; set; }

    /// <summary>来源表；留空表示非在册手工登记（source_type=Manual）。</summary>
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

    /// <summary>家庭遇困人数（含户主；空/0 时由 Core 按家庭人口兜底）。</summary>
    public int? ReliefPersonCount { get; set; }

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

    /// <summary>小额档位展示名（兼容旧前端；服务端以 standardTier 为准）。</summary>
    public string? SmallAmountLevel { get; set; }

    /// <summary>小额快救档位码：S50 / S75 / S100；常规留空。</summary>
    public string? StandardTier { get; set; }

    /// <summary>确定金额：小额可下调、常规备用金手工填、常规上级不填。</summary>
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

#endregion

#region 临时救助：动作请求

public sealed class TempReliefReasonRequest
{
    public string? Reason { get; set; }
}

public sealed class TempReliefRejectRequest
{
    public string? Reason { get; set; }
    public DateTime? NotifiedAt { get; set; }
    public string? NoticeMethod { get; set; }
}

public sealed class TempReliefChangeDateRequest
{
    public DateTime? ApplyDate { get; set; }
}

public sealed record TempReliefChangeDateResponse(string ApplicationNo);

/// <summary>兼容旧前端的终止请求（仅 reason）。</summary>
public sealed class TempReliefStopRequest
{
    public string? Reason { get; set; }
}

#endregion
