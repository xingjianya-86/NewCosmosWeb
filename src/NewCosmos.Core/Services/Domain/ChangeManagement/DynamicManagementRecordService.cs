using System.Globalization;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.NearRelative;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.UserManagement;

namespace NewCosmos.Services.Domain.ChangeManagement;

/// <summary>
/// 社会救助对象动态管理记录（补打最小子集）。
/// <para>
/// 字段口径与 PC <c>DynamicManagementRecordService.BuildFieldsAsync</c> 一致：
/// 收入列全部年值口径；净收入 = 主表年总收入 − 刚性月值×12 − 就业成本年值；
/// 货币财产 = 现金 + 银行存款 + 有价证券 + 商业保险，人均一次舍入到分；
/// 赡养义务人取前 3 名；动态管理周期固定“一年期”；乡镇（街道）意见取入户调查结论。
/// </para>
/// <para>
/// 依赖全部使用 NewCosmos.Core 既有服务，金额只读取主表年值/人均年值字段，
/// 不重抄收入公式；导出/打印/PDF 不在本服务范围内（服务端打印走队列）。
/// </para>
/// </summary>
public class DynamicManagementRecordService : BaseService, IDynamicManagementRecordService
{
    protected override string ServiceName => "DynamicManagementRecordService";

    private const string CycleText = "一年期";

    private readonly IDatabaseService _db;
    private readonly IApplicationService _applicationService;
    private readonly ISupporterService _supporterService;
    private readonly IEconomicDetailService _economicDetailService;
    private readonly IHouseholdSurveyService _householdSurveyService;
    private readonly IOrganizationService _organizationService;
    private readonly INearRelativeService _nearRelativeService;
    private readonly IDictCacheService _dictCacheService;

    public DynamicManagementRecordService(
        IDatabaseService db,
        IApplicationService applicationService,
        ISupporterService supporterService,
        IEconomicDetailService economicDetailService,
        IHouseholdSurveyService householdSurveyService,
        IOrganizationService organizationService,
        INearRelativeService nearRelativeService,
        IDictCacheService dictCacheService,
        ILoggerService logger) : base(logger)
    {
        _db = db;
        _applicationService = applicationService;
        _supporterService = supporterService;
        _economicDetailService = economicDetailService;
        _householdSurveyService = householdSurveyService;
        _organizationService = organizationService;
        _nearRelativeService = nearRelativeService;
        _dictCacheService = dictCacheService;
    }

    public async Task<Result<Dictionary<string, string>>> BuildFieldsAsync(
        long applicationId,
        CancellationToken ct = default)
    {
        try
        {
            // 1. 档案主表（金额年值权威口径全部取自此处，禁止重算）
            var appResult = await _applicationService.GetByIdAsync(applicationId, ct);
            if (appResult.IsFailure)
            {
                return Result.Failure<Dictionary<string, string>>(
                    appResult.ErrorCode ?? ErrorCodes.APPLICATION_NOT_FOUND,
                    appResult.Message ?? "档案查询失败");
            }

            var app = appResult.Value;
            if (app is null)
            {
                return Result.Failure<Dictionary<string, string>>(
                    ErrorCodes.APPLICATION_NOT_FOUND, "档案不存在或已删除");
            }

            // 2. 共同生活家庭成员人数（剔除 Support 类别；无户主行时补 1）
            var sizeResult = await _db.ExecuteScalarAsync<long>(
                @"SELECT COUNT(*) + CASE WHEN COUNT(*) FILTER (WHERE is_applicant = TRUE
                                              OR relationship_to_head = 'Head'
                                              -- 历史中文关系值与字典 key 并存，两套都算户主行
                                              OR relationship_to_head IN ('本人/户主', '本人', '户主')
                                              OR member_category IS NULL OR member_category = '') > 0
                                          THEN 0 ELSE 1 END
                  FROM nc_biz_family_members
                  WHERE application_id = $1 AND deleted_at IS NULL
                    AND (member_category IS NULL OR member_category <> 'Support')",
                ct, applicationId);
            if (sizeResult.IsFailure)
            {
                return Result.Failure<Dictionary<string, string>>(
                    sizeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    sizeResult.Message ?? "家庭成员人数统计失败");
            }

            var familySize = (int)sizeResult.Value;

            // 3. 赡养（扶、抚）义务人（前 3 名）
            var supportersResult = await _supporterService.GetByApplicationIdAsync(applicationId, ct);
            if (supportersResult.IsFailure)
            {
                return Result.Failure<Dictionary<string, string>>(
                    supportersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    supportersResult.Message ?? "赡养抚养扶养人查询失败");
            }

            var supporters = (supportersResult.Value ?? []).Take(3).ToList();

            // 4. 经济明细（加载失败必须显式失败——空集会被当作“无财产”写入档案）
            var econResult = await _economicDetailService.LoadAllAsync(applicationId, ct);
            if (econResult.IsFailure)
            {
                return Result.Failure<Dictionary<string, string>>(
                    econResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    econResult.Message ?? "经济明细加载失败");
            }

            var econ = econResult.Value;

            // 5. 就业成本年值汇总（当前表无录入入口，空表显示 "-"，参与计算按 0）
            var empCostResult = await _db.ExecuteScalarAsync<decimal>(
                @"SELECT COALESCE(SUM(annual_amount), 0) FROM nc_biz_employment_costs
                  WHERE application_id = $1 AND deleted_at IS NULL",
                ct, applicationId);
            if (empCostResult.IsFailure)
            {
                return Result.Failure<Dictionary<string, string>>(
                    empCostResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    empCostResult.Message ?? "就业成本查询失败");
            }

            var employmentCost = empCostResult.Value;

            // 6. 近亲属备案（复用既有服务，字段口径同 PC：staff_name / relation / work_unit）
            var nearRelativeResult = await _nearRelativeService.GetPairsByApplicationIdAsync(applicationId, ct);
            if (nearRelativeResult.IsFailure)
            {
                return Result.Failure<Dictionary<string, string>>(
                    nearRelativeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    nearRelativeResult.Message ?? "近亲属备案查询失败");
            }

            // 7. 入户调查结论（乡镇街道意见；查询失败只告警，不阻断整份档案）
            var surveyResult = await _householdSurveyService.GetByApplicationIdAsync(applicationId, ct);
            if (surveyResult.IsFailure)
            {
                LogWarn($"动态管理记录：入户调查结论查询失败，乡镇意见栏将为空: ApplicationId={applicationId}, {surveyResult.Message}");
            }

            var townOpinion = surveyResult.HasValue
                ? surveyResult.Value.SurveyConclusion ?? string.Empty
                : string.Empty;

            // 8. 填报单位（当前登录用户所在机构）
            var unitName = await ResolveCurrentUnitAsync(ct);

            // 9. 金额口径（AGENTS §9：年值权威，只读主表字段，一次舍入到分）
            var familyIncome = Math.Round(app.TotalAnnualIncome, 2);
            var rigidAnnual = Math.Round(app.RigidExpenditure * 12m, 2);
            var netIncome = Math.Round(familyIncome - rigidAnnual - employmentCost, 2);
            var perCapita = Math.Round(app.PerCapitaAnnualIncome, 2);

            // 货币财产 = 现金 + 银行存款 + 有价证券 + 商业保险
            var moneyAssets = econ.FinancialAssets?.Sum(f =>
                f.CashAmount + f.BankDepositAmount + f.SecuritiesAmount + f.CommercialInsuranceAmount) ?? 0m;
            var moneyAssetsTotal = Math.Round(moneyAssets, 2);
            var moneyAssetsPerCapita = familySize > 0
                ? Math.Round(moneyAssetsTotal / familySize, 2)
                : 0m;

            var fields = new Dictionary<string, string>
            {
                [FieldKeys.DM_CURRENT_UNIT] = unitName,
                [FieldKeys.DM_REPORT_YEAR] = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture),
                [FieldKeys.DM_CURRENT_USER] = CurrentUserContext.FullName,
                [FieldKeys.CURRENT_DATE] = DateTime.Now.ToString("yyyy年M月d日"),

                [FieldKeys.DM_FAMILY_SIZE] = familySize.ToString(CultureInfo.InvariantCulture),
                [FieldKeys.APPLICANT_NAME] = app.ApplicantName ?? string.Empty,
                [FieldKeys.HUKOU_ADDRESS] = app.HukouAddress ?? string.Empty,
                [FieldKeys.RESIDENCE_ADDRESS] = AddressResolver.BuildAddressFromText(
                    app.District, app.Town, app.Community, app.Address),
                [FieldKeys.DM_CATEGORY] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? string.Empty),
                [FieldKeys.DM_CYCLE] = CycleText,

                [FieldKeys.DM_FAMILY_INCOME] = FormatMoney(familyIncome),
                [FieldKeys.DM_RIGID_EXPENDITURE] = FormatMoney(rigidAnnual),
                [FieldKeys.DM_EMPLOYMENT_COST] = employmentCost > 0 ? FormatMoney(employmentCost) : "-",
                [FieldKeys.DM_NET_INCOME] = FormatMoney(netIncome),
                [FieldKeys.DM_PER_CAPITA_INCOME] = FormatMoney(perCapita),
                [FieldKeys.DM_MONEY_ASSET_TOTAL] = FormatMoney(moneyAssetsTotal),
                [FieldKeys.DM_MONEY_ASSET_PER_CAPITA] = FormatMoney(moneyAssetsPerCapita),
                [FieldKeys.DM_REAL_ESTATE] = BuildRealEstateText(econ),
                [FieldKeys.DM_VEHICLES] = BuildVehicleText(econ),
                [FieldKeys.DM_MACHINERY] = BuildMachineryText(econ.Machineries),

                [FieldKeys.DM_NEAR_RELATIVE] = BuildNearRelativeText(nearRelativeResult.Value),
                [FieldKeys.DM_OTHER_SITUATION] = string.Empty,
                [FieldKeys.TOWN_OPINION] = townOpinion,
            };

            FillSupporterSlots(fields, supporters);
            return Result.Success(fields);
        }
        catch (Exception ex)
        {
            LogException(ex, "装配动态管理记录字段");
            return Result.FromException<Dictionary<string, string>>(ex);
        }
    }

    public async Task<Result<List<ChangedArchiveSummary>>> SearchChangedArchivesRawAsync(
        string? keyword,
        int limit = 20,
        CancellationToken ct = default)
    {
        try
        {
            if (limit <= 0) limit = 20;

            var trimmed = keyword?.Trim() ?? string.Empty;
            var pattern = string.IsNullOrEmpty(trimmed) ? string.Empty : $"%{trimmed}%";

            // 不归一：历史旧档案与现役档案各返回一条，供补打中心选择版本。
            var result = await _db.QueryAsync<ChangedArchiveSummary>(
                @"SELECT a.id AS application_id,
                         a.applicant_name AS head_name,
                         a.applicant_id_card AS id_card,
                         a.application_no AS application_no,
                         a.classification_result AS classification,
                         a.status AS status,
                         a.chain_type AS chain_type,
                         MAX(cr.change_date) AS last_change_date
                  FROM nc_biz_change_records cr
                  JOIN nc_biz_applications a ON a.id = cr.application_id AND a.deleted_at IS NULL
                  WHERE cr.deleted_at IS NULL
                    AND ($1 = '' OR a.applicant_name LIKE $1 OR a.applicant_id_card LIKE $1)
                  GROUP BY a.id, a.applicant_name, a.applicant_id_card, a.application_no,
                           a.classification_result, a.status, a.chain_type
                  ORDER BY last_change_date DESC
                  LIMIT $2",
                ct, pattern, limit);

            if (result.IsFailure)
            {
                return Result.Failure<List<ChangedArchiveSummary>>(
                    result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "变更档案（不归一）搜索失败");
            }

            return Result.Success(result.Value ?? []);
        }
        catch (Exception ex)
        {
            LogException(ex, "搜索变更档案（不归一）");
            return Result.FromException<List<ChangedArchiveSummary>>(ex);
        }
    }

    public async Task<Result<long>> NormalizeToLatestApplicationIdAsync(
        long applicationId,
        CancellationToken ct = default)
    {
        try
        {
            // 沿 original_application_id 递归到链尾（MAX(id)，停旧建新新档案 id 严格递增）
            var result = await _db.ExecuteScalarAsync<long>(
                @"WITH RECURSIVE chain AS (
                      SELECT id, original_application_id FROM nc_biz_applications WHERE id = $1
                      UNION ALL
                      SELECT a.id, a.original_application_id
                      FROM nc_biz_applications a
                      JOIN chain c ON a.original_application_id = c.id
                  )
                  SELECT COALESCE(MAX(id), 0) FROM chain",
                ct, applicationId);

            if (result.IsFailure)
            {
                return Result.Failure<long>(
                    result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "档案链归一查询失败");
            }

            var tailId = result.Value;
            if (tailId <= 0)
            {
                return Result.Failure<long>(
                    ErrorCodes.APPLICATION_NOT_FOUND, $"档案 {applicationId} 不存在");
            }

            var aliveResult = await _db.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL",
                ct, tailId);
            if (aliveResult.IsFailure)
            {
                return Result.Failure<long>(
                    aliveResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    aliveResult.Message ?? "档案状态查询失败");
            }

            if (aliveResult.Value == 0)
            {
                return Result.Failure<long>(
                    ErrorCodes.APPLICATION_NOT_FOUND,
                    $"档案 {applicationId} 的现役档案（{tailId}）已被删除，无法打印");
            }

            return Result.Success(tailId);
        }
        catch (Exception ex)
        {
            LogException(ex, "档案链归一");
            return Result.FromException<long>(ex);
        }
    }

    private async Task<string> ResolveCurrentUnitAsync(CancellationToken ct)
    {
        var orgId = CurrentUserContext.OrganizationId;
        if (!orgId.HasValue) return "-";

        var orgResult = await _organizationService.GetByIdAsync(orgId.Value, ct);
        return orgResult.HasValue ? orgResult.Value.Name : "-";
    }

    /// <summary>赡养人 3 槽位字段键（占位符已含序号，禁止拼串生成键）。</summary>
    private static readonly (string Name, string Relation, string Health, string Occupation, string Fee)[] SupporterSlots =
    [
        (FieldKeys.DM_SUPPORTER_NAME_1, FieldKeys.DM_SUPPORTER_RELATION_1, FieldKeys.DM_SUPPORTER_HEALTH_1, FieldKeys.DM_SUPPORTER_OCCUPATION_1, FieldKeys.DM_SUPPORTER_FEE_1),
        (FieldKeys.DM_SUPPORTER_NAME_2, FieldKeys.DM_SUPPORTER_RELATION_2, FieldKeys.DM_SUPPORTER_HEALTH_2, FieldKeys.DM_SUPPORTER_OCCUPATION_2, FieldKeys.DM_SUPPORTER_FEE_2),
        (FieldKeys.DM_SUPPORTER_NAME_3, FieldKeys.DM_SUPPORTER_RELATION_3, FieldKeys.DM_SUPPORTER_HEALTH_3, FieldKeys.DM_SUPPORTER_OCCUPATION_3, FieldKeys.DM_SUPPORTER_FEE_3)
    ];

    private void FillSupporterSlots(Dictionary<string, string> fields, List<Supporter> supporters)
    {
        for (var i = 0; i < SupporterSlots.Length; i++)
        {
            var slot = SupporterSlots[i];
            var supporter = i < supporters.Count ? supporters[i] : null;

            fields[slot.Name] = supporter?.Name ?? string.Empty;
            fields[slot.Relation] = supporter is null
                ? string.Empty
                : DictDisplay(DictionaryTypeCodes.FamilyRelationships, supporter.Relationship);
            fields[slot.Health] = supporter is null
                ? string.Empty
                : DictDisplay(DictionaryTypeCodes.HealthStatuses, supporter.HealthStatus);
            fields[slot.Occupation] = supporter is null
                ? string.Empty
                : DictDisplay(DictionaryTypeCodes.EmploymentStatuses, supporter.EmploymentStatus);
            fields[slot.Fee] = supporter is null || supporter.AnnualSupportFee <= 0
                ? string.Empty
                : FormatMoney(supporter.AnnualSupportFee);
        }
    }

    /// <summary>字典显示：命中字典用显示值，未命中回退原始值。</summary>
    private string DictDisplay(string category, string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;

        var display = _dictCacheService.GetValue(category, code);
        return string.IsNullOrEmpty(display) ? code : display;
    }

    private static string FormatMoney(decimal value)
        => value.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>不动产详情：房产聚合文本，无 → "无"。</summary>
    private static string BuildRealEstateText(EconomicDetailData econ)
    {
        var items = (econ.FamilyProperties ?? []).Select(p =>
            $"{p.HousingStructure ?? "未知"}结构，面积{FormatMoney(p.Area)}㎡，{p.HousingNature ?? "未知"}");
        var text = string.Join("；", items);
        return string.IsNullOrEmpty(text) ? "无" : text;
    }

    /// <summary>机动车辆详情：车辆聚合文本，无 → "无"。</summary>
    private static string BuildVehicleText(EconomicDetailData econ)
    {
        var items = (econ.Vehicles ?? []).Select(v =>
            $"{v.Brand} {v.Model}（{v.LicensePlate}），估值{FormatMoney(v.EstimatedValue)}元");
        var text = string.Join("；", items);
        return string.IsNullOrEmpty(text) ? "无" : text;
    }

    /// <summary>船舶/农机具详情：农机具聚合文本。</summary>
    private static string BuildMachineryText(List<Machinery>? machineries)
    {
        if (machineries is not { Count: > 0 }) return "无";

        var items = machineries.OrderBy(m => m.Id).Select(m =>
        {
            var type = string.IsNullOrWhiteSpace(m.MachineryType) ? "农机具" : m.MachineryType;
            var spec = string.Join(" ", new[] { m.Brand, m.Model }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            var qty = m.Quantity > 0 ? m.Quantity : 1;

            return string.IsNullOrEmpty(spec) ? $"{type}×{qty}" : $"{type} {spec}×{qty}";
        });

        return string.Join("；", items);
    }

    /// <summary>近亲属备案情况：无 → "无"。</summary>
    private static string BuildNearRelativeText(List<NearRelativePair>? pairs)
    {
        if (pairs is not { Count: > 0 }) return "无";

        var items = pairs.OrderBy(p => p.Link.Id).Select(p =>
        {
            var staff = p.Staff.StaffName ?? string.Empty;
            var relation = p.Link.Relation ?? string.Empty;
            var unit = p.Staff.WorkUnit ?? string.Empty;
            var detail = string.Join("，", new[] { relation, unit }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

            return string.IsNullOrEmpty(detail)
                ? $"工作人员{staff}已备案"
                : $"工作人员{staff}（{detail}）已备案";
        });

        return string.Join("；", items);
    }
}
