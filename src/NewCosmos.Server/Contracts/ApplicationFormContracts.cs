using NewCosmos.Models.Entities;

namespace NewCosmos.Server.Contracts;

#region 低收入人口认定五步表单

/// <summary>
/// 表单基本信息（Step1）。字段名与 web/src/views/applications/form/types.ts 保持一致；
/// 主表没有独立的户籍省/市/区/镇列，保存时合并进 hukou_address，读取时统一放在
/// hukou_detail_address 中回填（户籍分级字段返回空串）。
/// </summary>
public sealed record ApplicationFormBasicDto(
    string HukouType,
    string ApplicantName,
    string ApplicantIdCard,
    string Gender,
    string Ethnicity,
    string Phone,
    string HukouProvince,
    string HukouCity,
    string HukouDistrict,
    string HukouTown,
    string HukouVillage,
    string HukouDetailAddress,
    string Province,
    string City,
    string District,
    string Town,
    string Village,
    string Address,
    string BankName,
    string BankAccount,
    string DisabilityCardNo,
    string ApplicationReason,
    string ApplicationReasonDetail,
    string Remark,
    string EducationLevel,
    string MaritalStatus,
    string PoliticalStatus,
    string HealthStatus,
    string DiseaseName,
    string DisabilityType,
    string DisabilityLevel,
    decimal? MedicalSelfPaid12m);

/// <summary>家庭成员（Step2）。id=0 表示新增；birthDate 使用 yyyy-MM-dd。</summary>
public sealed record ApplicationFormMemberDto(
    long Id,
    string Name,
    string IdCard,
    string Relation,
    string Gender,
    string BirthDate,
    int? Age,
    string Ethnicity,
    string Phone,
    string HukouType,
    string HealthStatus,
    string EmploymentStatus,
    string WorkUnit,
    string MainIncomeSource,
    decimal? AnnualIncome,
    decimal? MonthlyIncomeCapacity,
    bool IsHouseholdHead,
    bool IsApplicant,
    string MemberCategory,
    bool HasDisability,
    string DisabilityType,
    string DisabilityLevel,
    string DiseaseName,
    bool IsSevereDisease,
    bool IsLaborExempt,
    string EducationLevel,
    string MaritalStatus,
    string PoliticalStatus,
    string HukouAddress,
    string HomeAddress,
    string DiseaseCategory,
    decimal? MedicalSelfPaid12m,
    string DisabilityCertificateNo,
    string PersonType,
    bool IsSingleRescue,
    string SelfCareAbility);

/// <summary>赡养/抚养/扶养义务人（Step3）。</summary>
public sealed record ApplicationFormSupporterDto(
    long Id,
    string Name,
    string IdCard,
    string Relation,
    string Gender,
    int? Age,
    decimal? AnnualSupportFee,
    decimal? MonthlySupportFee,
    bool IsSupportAbility);

/// <summary>
/// 家庭经济收入（Step4）。月项：work/business/property/transfer/other/rigidExpenditure；
/// 年项：alimony/land/subsidy。total* 为服务端按 Core 公式重算后的只读汇总。
/// </summary>
public sealed record ApplicationFormEconomyDto(
    decimal? WorkIncome,
    decimal? BusinessIncome,
    decimal? PropertyIncome,
    decimal? TransferIncome,
    decimal? OtherIncome,
    decimal? AlimonyIncome,
    decimal? LandIncome,
    decimal? SubsidyTotal,
    decimal? RigidExpenditure,
    decimal? FamilyLandArea,
    decimal? SelfFarmedLandArea,
    decimal? SubleasedLandArea,
    decimal? ContractedLandArea,
    decimal TotalAnnualIncome,
    decimal TotalFamilyIncome,
    decimal PerCapitaAnnualIncome,
    decimal PerCapitaIncome);

/// <summary>入户调查（Step4）。</summary>
public sealed record ApplicationFormSurveyDto(
    string SurveyDate,
    string SurveyorName,
    string SurveyorOrganization,
    string ApplicationReason,
    string ApplicationReasonDetail,
    string SurveyNotes,
    string RespondentName,
    string RespondentRelation,
    string SurveyConclusion);

/// <summary>
/// 经济明细（Step4）。直接复用 Core 实体列表，字段名与前端 economicDetails 的花名册一致。
/// 土地确权组（LandConfirmationGroup）含嵌套集合，暂不进入表单契约；保存时服务端沿用旧组，
/// 避免整表重写把确权数据清空。
/// </summary>
public sealed record ApplicationFormEconomicDetailsDto(
    List<LaborIncome>? LaborIncomes,
    List<BusinessIncome>? BusinessIncomes,
    List<PropertyIncome>? PropertyIncomes,
    List<TransferIncome>? TransferIncomes,
    List<OtherIncome>? OtherIncomes,
    List<Subsidy>? Subsidies,
    List<BreedingIncome>? BreedingIncomes,
    List<RigidExpenditure>? RigidExpenditures,
    List<FamilyProperty>? FamilyProperties,
    List<Vehicle>? Vehicles,
    List<Machinery>? Machineries,
    List<FinancialAsset>? FinancialAssets,
    List<LandRegistration>? LandRegistrations);

/// <summary>回访补录请求。applicantIdCard 由服务端从主表同源带出，不接受前端传入。</summary>
public sealed record ApplicationVisitCreateRequest(
    string? VisitAt,
    string? VisitBy,
    string? VisitResult,
    string? VisitRemark);

/// <summary>回访补录返回。</summary>
public sealed record ApplicationVisitCreatedResult(long Id);

/// <summary>分类判定结果（Step5）。金额与分类只由 Core IClassificationService 产出。</summary>
public sealed record ApplicationFormClassificationDto(
    string ClassificationResult,
    string ClassificationText,
    bool IsEligible,
    string IneligibleReason,
    string DeterminationBasis,
    decimal? HouseholdMonthlyGuaranteeAmount,
    decimal? ClassifiedSubsidyAmount,
    decimal? CaregiverSubsidyAmount,
    decimal? TotalGuaranteeAmount,
    bool IsInGracePeriod,
    int? GracePeriodMonths,
    string GracePeriodEndDate);

/// <summary>完整表单回填模型。</summary>
public sealed record ApplicationFormDto(
    long Id,
    string ApplicationNo,
    string Status,
    string StatusText,
    int CurrentStep,
    ApplicationFormBasicDto Basic,
    IReadOnlyList<ApplicationFormMemberDto> Members,
    IReadOnlyList<ApplicationFormSupporterDto> Supporters,
    ApplicationFormEconomyDto Economy,
    ApplicationFormSurveyDto Survey,
    ApplicationFormClassificationDto Classification,
    ApplicationFormEconomicDetailsDto? EconomicDetails,
    List<Caregiver>? Caregivers,
    List<VisitRecord> VisitRecords);

/// <summary>
/// 表单保存请求。与响应用同一结构；保存端读取 basic/members/supporters/economy/survey/
/// economicDetails/caregivers，classification 块忽略（分类只能走 POST /classify，防止前端注入金额）。
/// visitRecords 只在 GET 返回，补录走 POST /api/applications/{id}/visits。
/// 各块为 null 表示本次不修改该块（更新时保留原值；新建时按空处理）。
/// </summary>
public sealed record ApplicationFormSaveRequest(
    long? Id,
    string? ApplicationNo,
    string? Status,
    string? StatusText,
    int? CurrentStep,
    ApplicationFormBasicDto? Basic,
    List<ApplicationFormMemberDto>? Members,
    List<ApplicationFormSupporterDto>? Supporters,
    ApplicationFormEconomyDto? Economy,
    ApplicationFormSurveyDto? Survey,
    ApplicationFormClassificationDto? Classification,
    ApplicationFormEconomicDetailsDto? EconomicDetails,
    List<Caregiver>? Caregivers);

/// <summary>新建/保存草稿返回。</summary>
public sealed record ApplicationFormSaveResult(long Id, string ApplicationNo);

/// <summary>归档完成返回。</summary>
public sealed record ApplicationFormCompleteResult(string Status, int CurrentStep);

#endregion
