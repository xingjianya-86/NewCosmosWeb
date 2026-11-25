namespace NewCosmos.Server.Contracts;

#region 保障对象动态管理：查询

/// <summary>动态管理在享对象列表（分页）。</summary>
public sealed record ChangeManagementListResponse(
    IReadOnlyList<ChangeManagementListItemDto> Items,
    int PageIndex,
    int PageSize,
    long TotalCount,
    int TotalPages);

/// <summary>在享对象列表项。身份证统一脱敏；金额为 Core 已落库结果，前端不重算。</summary>
public sealed record ChangeManagementListItemDto(
    long ApplicationId,
    string ApplicationNo,
    string ApplicantName,
    string ApplicantIdCardMasked,
    string Town,
    string Village,
    string Classification,
    string ClassificationName,
    int FamilySize,
    decimal HouseholdMonthlyGuaranteeAmount,
    decimal TotalGuaranteeAmount,
    decimal PerCapitaAnnualIncome,
    string Status,
    string StatusText,
    DateTime? GracePeriodEndDate,
    int? GracePeriodMonths,
    int? GraceDaysLeft);

/// <summary>
/// 动态管理筛选与表单选项：乡镇/分类来自当前数据范围内的在享对象；
/// 其余字典项来自 nc_dict_items（增/减员原因、死亡原因、家庭关系等），不在代码里复制业务清单。
/// </summary>
public sealed record ChangeManagementFiltersResponse(
    IReadOnlyList<string> Towns,
    IReadOnlyList<ChangeClassificationOptionDto> Classifications,
    IReadOnlyList<ChangeDictOptionDto> DeathReasons,
    IReadOnlyList<ChangeDictOptionDto> MemberAddReasons,
    IReadOnlyList<ChangeDictOptionDto> MemberRemoveReasons,
    IReadOnlyList<ChangeDictOptionDto> Relationships,
    IReadOnlyList<ChangeDictOptionDto> HealthStatuses,
    IReadOnlyList<ChangeDictOptionDto> EmploymentStatuses,
    IReadOnlyList<ChangeDictOptionDto> EducationLevels,
    IReadOnlyList<ChangeDictOptionDto> MaritalStatuses,
    IReadOnlyList<ChangeDictOptionDto> PoliticalStatuses,
    IReadOnlyList<ChangeDictOptionDto> LaborAbilities,
    IReadOnlyList<ChangeDictOptionDto> HukouTypes,
    IReadOnlyList<ChangeDictOptionDto> MemberCategories,
    IReadOnlyList<ChangeDictOptionDto> Genders);

public sealed record ChangeClassificationOptionDto(string Code, string Name);

public sealed record ChangeDictOptionDto(string Code, string Name);

/// <summary>选中户详情：档案摘要 + 共同生活成员 + 渐退期 + 本月已办提示 + 入口可用性。</summary>
public sealed record ChangeManagementDetailDto(
    long ApplicationId,
    string ApplicationNo,
    string ApplicantName,
    string ApplicantIdCard,
    string ApplicantIdCardMasked,
    string ApplicantPhone,
    string Gender,
    string HukouType,
    string HukouTypeName,
    string Town,
    string Village,
    string Address,
    string Classification,
    string ClassificationName,
    string Status,
    string StatusText,
    bool IsStopped,
    int FamilySize,
    decimal HouseholdMonthlyGuaranteeAmount,
    decimal ClassifiedSubsidyAmount,
    decimal CaregiverSubsidyAmount,
    decimal TotalGuaranteeAmount,
    decimal TotalAnnualIncome,
    decimal PerCapitaAnnualIncome,
    decimal TotalFamilyIncome,
    decimal PerCapitaIncome,
    decimal? SubsistenceStandard,
    DateTime? FirstApprovedAt,
    DateTime? CreatedAt,
    bool CanManage,
    bool CanChangeMember,
    bool CanChangeHead,
    bool CanHouseholdDeath,
    bool CanGraceConfirm,
    bool CanVerifyStop,
    ChangeMonthlyFlagsDto Monthly,
    ChangeGraceInfoDto? Grace,
    ChangeHeadInfoDto Head,
    IReadOnlyList<ChangeMemberDto> Members);

/// <summary>本月已办理提示（口径同旧版 ChangeViewModel：FundChange+经济复核 / 成员类 / 户主变更 / 户主死亡）。</summary>
public sealed record ChangeMonthlyFlagsDto(
    bool EconomicReviewDone,
    bool MemberChangeDone,
    bool HeadChangeDone,
    bool HeadDeathDone);

/// <summary>当前在效渐退期（无则详情 grace 为 null）。</summary>
public sealed record ChangeGraceInfoDto(
    long Id,
    int? Months,
    DateTime? StartDate,
    DateTime? EndDate,
    string OriginalClassification,
    string OriginalClassificationName,
    decimal? OriginalGuaranteeAmount,
    decimal? GraceGrantAmount,
    int? DaysLeft,
    bool IsActive);

public sealed record ChangeHeadInfoDto(
    long MemberId,
    string Name,
    string IdCard,
    string IdCardMasked,
    bool IsVirtual);

/// <summary>共同生活成员行（已排除赡养人与已登记死亡成员）。</summary>
public sealed record ChangeMemberDto(
    long Id,
    string Name,
    string IdCard,
    string IdCardMasked,
    string Relationship,
    string RelationshipText,
    string Gender,
    int? Age,
    DateTime? BirthDate,
    string HealthStatus,
    string HealthStatusText,
    string EmploymentStatus,
    string EmploymentStatusText,
    string WorkCapacity,
    string WorkCapacityText,
    string Phone,
    string WorkUnit,
    string EducationLevel,
    string EducationLevelText,
    string MaritalStatus,
    string MaritalStatusText,
    string PoliticalStatus,
    string PoliticalStatusText,
    string MemberCategory,
    string MemberCategoryText,
    bool IsHouseholdHead,
    bool IsApplicant,
    bool IsSupport);

/// <summary>变更历史行。</summary>
public sealed record ChangeHistoryItemDto(
    long Id,
    string ChangeNo,
    string ChangeType,
    string ChangeTypeDisplay,
    string ChangeReason,
    string? ChangeReasonType,
    DateTime ChangeDate,
    string OperatorName,
    DateTime ChangedAt);

public sealed record ChangeHistoryResponse(IReadOnlyList<ChangeHistoryItemDto> Items);

#endregion

#region 保障对象动态管理：写请求

/// <summary>户主变更：新户主必须是本户非户主共同生活成员。</summary>
public sealed record HeadChangeRequest(long NewHeadMemberId, string ChangeReason);

/// <summary>
/// 户主死亡变更。DeathDate/DeathTime 由前端日期时间控件拆出；
/// 单人户（无其他共同生活成员）忽略 NewHeadMemberId，走整户停止。
/// </summary>
public sealed record HouseholdDeathRequest(
    long? DeceasedHeadMemberId,
    long? NewHeadMemberId,
    string DeathDate,
    string DeathTime,
    string DeathReason,
    string? DeathCertificateNo);

/// <summary>渐退期确认：对当前在效渐退期确认/调整月数（1–12）。</summary>
public sealed record GraceConfirmRequest(int Months);

/// <summary>核验停止（拒绝配合核验工作）：只停不建新档。</summary>
public sealed record VerifyStopRequest(string? StopDate);

/// <summary>
/// 家庭成员变更：新增/减员/属性变更三路一起提交；逐人原因必填。
/// 服务端先在同一事务内落成员行，再调 Core ExecuteMemberChangeAsync 重新认定并停旧建新。
/// </summary>
public sealed record MemberChangeRequest(
    string ChangeReason,
    IReadOnlyList<MemberAddRequest>? Additions,
    IReadOnlyList<MemberRemoveRequest>? Removals,
    IReadOnlyList<MemberModifyRequest>? Modifications);

public sealed record MemberAddRequest(
    string Name,
    string IdCard,
    string RelationshipToHead,
    string? Gender,
    int? Age,
    string? BirthDate,
    string? HealthStatus,
    string? Phone,
    string ReasonCode,
    string ReasonName,
    string? EventDate,
    string? Remark);

public sealed record MemberRemoveRequest(
    long MemberId,
    string ReasonCode,
    string ReasonName,
    string? EventDate,
    string? EventTime,
    string? Remark);

/// <summary>
/// 属性变更（姓名/身份证/户主标识不在可改范围）：仅允许关系、健康、就业、劳动能力、
/// 联系方式、工作单位、文化程度、婚姻、政治面貌。逐人原因写入变更明细。
/// </summary>
public sealed record MemberModifyRequest(
    long MemberId,
    string? RelationshipToHead,
    string? HealthStatus,
    string? EmploymentStatus,
    string? WorkCapacity,
    string? Phone,
    string? WorkUnit,
    string? EducationLevel,
    string? MaritalStatus,
    string? PoliticalStatus,
    string Reason,
    string? Remark);

/// <summary>变更办理结果（金额/分类均为 Core 判定结果，前端只展示）。</summary>
public sealed record ChangeOperationResultDto(
    long ChangeId,
    bool IsSuccess,
    string OldClassification,
    string OldClassificationName,
    string NewClassification,
    string NewClassificationName,
    decimal OldGuaranteeAmount,
    decimal NewGuaranteeAmount,
    bool TriggeredGracePeriod,
    bool TriggeredStop,
    bool Rebuilt,
    long NewApplicationId);

/// <summary>渐退确认结果。</summary>
public sealed record GraceConfirmResultDto(
    bool Success,
    int Months,
    DateTime? StartDate,
    DateTime? EndDate);

#endregion
