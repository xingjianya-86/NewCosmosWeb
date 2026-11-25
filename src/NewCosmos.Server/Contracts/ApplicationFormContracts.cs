using System.Text.Json;
using System.Text.Json.Serialization;
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
/// 后者落在 nc_biz_family_members.family_size，经 SupporterFamilySize 读写。
/// </para>
/// <para>
/// 2026-11 赡养卡全量字段：个人信息/地址/健康残疾/就业收入与共同生活成员同口径。
/// 健康与残疾字段写 nc_biz_family_members 的 disease_*/disability_*/medical_self_paid_12m 列；
/// 疾病口径：DiseaseCategory = 一级疾病（落 disease_name/disease_category），
/// SecondaryDiseaseName = 二级疾病（落 secondary_disease），DiseaseCode 落 disease_code。
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
    string? Address = null,
    // 2026-11 赡养卡补齐：以下字段均追加在末尾并带默认值，保持旧客户端反序列化兼容。
    string? Ethnicity = null,
    string? MaritalStatus = null,
    string? EducationLevel = null,
    string? PoliticalStatus = null,
    string? HukouType = null,
    string? HealthStatus = null,
    string? EmploymentStatus = null,
    string? WorkUnit = null,
    string? MainIncomeSource = null,
    string? HukouProvince = null,
    string? HukouCity = null,
    string? HukouDistrict = null,
    string? HukouTown = null,
    string? HomeProvince = null,
    string? HomeCity = null,
    string? HomeDistrict = null,
    string? HomeTown = null,
    string? HomeVillage = null,
    string? HomeAddress = null,
    string? DiseaseCode = null,
    string? DiseaseCategory = null,
    string? SecondaryDiseaseName = null,
    // 可空 bool：null = 旧客户端未传，服务端沿用旧值；false = 用户显式取消勾选。
    bool? IsSevereDisease = null,
    decimal? MedicalSelfPaid12m = null,
    string? DisabilityType = null,
    string? DisabilityLevel = null,
    string? DisabilityCertificateNo = null,
    // 赡养人自家人口（nc_biz_family_members.family_size，不参与年费计算）。
    int? SupporterFamilySize = null);

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
/// 经济明细（Step3/Step4）。直接复用 Core 实体列表，字段名与前端 economicDetails 的花名册一致。
/// 土地确权归户表按组整包读写（组内 records/persons 一起保存）；null 表示旧客户端未携带该块，
/// 服务端沿用库内已保存的组，避免整表重写把确权数据清空。
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
    List<LandRegistration>? LandRegistrations,
    [property: JsonConverter(typeof(LandConfirmationGroupsJsonConverter))]
    List<LandConfirmationGroup>? LandConfirmationGroups = null);

/// <summary>
/// 土地确权组列表转换器。
/// <para>
/// Core 的 <see cref="LandConfirmationGroup.Records"/> / <see cref="LandConfirmationGroup.Persons"/>
/// 是只读集合（仅 getter），System.Text.Json 默认不会向只读集合回填元素，直接反序列化会得到
/// 空组，保存时会把地块与人员静默丢掉。此转换器显式读取嵌套数组后逐个 Add，保证整包读写。
/// </para>
/// </summary>
public sealed class LandConfirmationGroupsJsonConverter : JsonConverter<List<LandConfirmationGroup>>
{
    public override List<LandConfirmationGroup>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("landConfirmationGroups 必须是数组");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var groups = new List<LandConfirmationGroup>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("土地确权组必须是对象");
            }

            groups.Add(ReadGroup(element, options));
        }

        return groups;
    }

    public override void Write(
        Utf8JsonWriter writer,
        List<LandConfirmationGroup> value,
        JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var group in value)
        {
            JsonSerializer.Serialize(writer, group, options);
        }
        writer.WriteEndArray();
    }

    private static LandConfirmationGroup ReadGroup(
        JsonElement element,
        JsonSerializerOptions options)
    {
        var group = new LandConfirmationGroup
        {
            Id = ReadInt64(element, "id"),
            GroupId = ReadInt32(element, "groupId"),
            ContractorName = ReadString(element, "contractorName"),
            VillageName = ReadString(element, "villageName"),
            DryFieldArea = ReadDouble(element, "dryFieldArea"),
            WetFieldArea = ReadDouble(element, "wetFieldArea"),
        };

        if (element.TryGetProperty("records", out var records)
            && records.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in records.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var record = item.Deserialize<LandConfirmationRecord>(options);
                if (record is not null)
                {
                    group.Records.Add(record);
                }
            }
        }

        if (element.TryGetProperty("persons", out var persons)
            && persons.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in persons.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var person = item.Deserialize<LandConfirmationPerson>(options);
                if (person is not null)
                {
                    group.Persons.Add(person);
                }
            }
        }

        return group;
    }

    private static string ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long ReadInt64(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt64(out var parsed)
            ? parsed
            : 0;

    private static int ReadInt32(JsonElement element, string name)
        => (int)ReadInt64(element, name);

    private static double ReadDouble(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetDouble(out var parsed)
            ? parsed
            : 0;
}

/// <summary>回访补录请求。applicantIdCard 由服务端从主表同源带出，不接受前端传入。</summary>
public sealed record ApplicationVisitCreateRequest(
    string? VisitAt,
    string? VisitBy,
    string? VisitResult,
    string? VisitRemark);

/// <summary>回访补录返回。</summary>
public sealed record ApplicationVisitCreatedResult(long Id);

/// <summary>
/// 分类判定结果（Step5）。金额与分类只由 Core IClassificationService 产出。
/// <para>
/// 2026-11 Step5 legacy 对齐：渐退期/能力鉴定/判定明细/分类施保/单人保/特困供养字段
/// 一律追加在末尾并带默认值，保持旧客户端反序列化兼容。
/// 渐退期只从 nc_biz_grace_periods 读取（主表没有渐退列）。
/// </para>
/// </summary>
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
    string GracePeriodEndDate,
    string? ClassifiedSubsidyTypes = null,
    int? ClassifiedSubsidyCount = null,
    decimal? ClassifiedSubsidyPerPersonAmount = null,
    List<string>? DeterminationDetails = null,
    bool NeedsSingleRescueDraft = false,
    List<FamilyMember>? SingleRescueMembers = null,
    string GracePeriodStartDate = "",
    decimal? OriginalGuaranteeAmount = null,
    string GracePeriodStatusText = "",
    decimal? GraceGrantAmount = null,
    bool? CapabilityEating = null,
    bool? CapabilityDressing = null,
    bool? CapabilityGettingInOutOfBed = null,
    bool? CapabilityUsingToilet = null,
    bool? CapabilityIndoorWalking = null,
    bool? CapabilityBathing = null,
    int? CapabilityCompletedItems = null,
    string? CapabilitySelfCareLevel = null,
    string? DestituteSupportType = null,
    long? SupportInstitutionId = null,
    string? SupportInstitutionName = null);

/// <summary>特困供养方式更新请求（Step5）：只写 destitute_support_type / support_institution_id。</summary>
public sealed record ApplicationFormSupportModeRequest(
    string? SupportType,
    long? InstitutionId);

/// <summary>供养机构选项（Step5）。当前 Web 端无机构数据源时返回空数组，前端显示“暂无机构”。</summary>
public sealed record ApplicationSupportInstitutionDto(
    long Id,
    string Name,
    string Address,
    string Phone,
    decimal BedFee,
    decimal CareFee,
    decimal TotalFee,
    int Capacity,
    int CurrentOccupancy);

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
