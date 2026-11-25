using NewCosmos.Models.Entities;

namespace NewCosmos.Server.Contracts;

#region 低收入人口认定五步表单

/// <summary>
/// 表单基本信息（Step1）。字段名与 web/src/views/applications/form/types.ts 保持一致；
/// 主表没有 hukou_province/hukou_city 等**名称**列，只有 hukou_address 与
/// hukou_city_id/hukou_county_id/hukou_town_id/hukou_village_id；保存时名称合并进
/// hukou_address、并按名称解析 ID 写入，读取时由 Service 反解为省/市/区/镇/村 + 详细地址。
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
    decimal? MedicalSelfPaid12m,
    // 2026-11 地址级联/疾病补全：新增字段一律追加在末尾并带默认值，保持旧客户端反序列化兼容。
    // DiseaseCategory = 一级疾病（落 disease_name），SecondaryDiseaseName = 二级疾病（落 secondary_disease_name）。
    string? DiseaseCode = null,
    string? DiseaseCategory = null,
    string? SecondaryDiseaseName = null,
    bool IsSevereDisease = false);

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
    string SelfCareAbility,
    // 2026-11 地址级联/疾病补全：追加在末尾并带默认值，保持旧客户端兼容。
    // 注意 DiseaseCategory=一级疾病（落 disease_name），DiseaseName=二级疾病（落 secondary_disease）；
    // 成员表没有 hukou_village 列，故这里也不提供 HukouVillage。
    string? DiseaseCode = null,
    string? SecondaryDiseaseName = null,
    string? HomeProvince = null,
    string? HomeCity = null,
    string? HomeDistrict = null,
    string? HomeTown = null,
    string? HomeVillage = null,
    string? HukouProvince = null,
    string? HukouCity = null,
    string? HukouDistrict = null,
    string? HukouTown = null);

/// <summary>
/// 赡养/抚养/扶养义务人（Step3）。
/// <para>
/// 新增字段一律放在末尾并带默认值，保持旧客户端（只传前 9 个字段）反序列化兼容。
/// </para>
/// <para>
/// 口径：AnnualSupportFee 为只读派生值，服务端按
/// <c>round(MonthlySupportFee × SupportMonths × FamilySize, 2)</c> 重算并忽略前端传值；
/// FamilySize 是**申请表家庭人口**（含户主，年费乘数，只读展示），不是赡养人自家人口——
/// 后者落在 nc_biz_family_members.family_size，服务端沿用旧值、不在表单编辑。
/// </para>
/// </summary>
public sealed record ApplicationFormSupporterDto(
    long Id,
    string Name,
    string IdCard,
    string Relation,
    string Gender,
    int? Age,
    decimal? AnnualSupportFee,
    decimal? MonthlySupportFee,
    bool IsSupportAbility,
    string? PersonType = null,
    decimal? MonthlyIncomeCapacity = null,
    int? SupportMonths = null,
    int? FamilySize = null,
    string? Phone = null,
    string? Address = null);

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
