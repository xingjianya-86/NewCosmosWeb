using System.Globalization;

namespace NewCosmos.Models.Results;

/// <summary>
/// 快速核验 · 模拟测算输入（与旧版 QuickVerifyViewModel.Income.cs 的输入字段一一对应）。
/// 【口径】家庭人口 = <see cref="FamilySizeExcludingHead"/> + 1（户主）；子女数量独立于家庭人口。
/// 所有金额/面积字段均为用户输入盈余值，负数由服务层显式拒绝。
/// </summary>
public class QuickVerifySimulationInput
{
    /// <summary>被核算人姓名（旧版必填）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>被核算人身份证号（旧版必填，用于台账核验与审计）。</summary>
    public string IdCard { get; set; } = string.Empty;

    /// <summary>户口性质（Rural/Urban，兼容"农村/城镇"等旧值）。</summary>
    public string HukouType { get; set; } = string.Empty;

    /// <summary>家庭人口（不含户主）。</summary>
    public int FamilySizeExcludingHead { get; set; }

    /// <summary>子女数量。</summary>
    public int Children { get; set; }

    /// <summary>每子女月赡养费；null/空 = 低保标准 × 20%（旧版默认机制）。</summary>
    public decimal? PerChildMonthlyFee { get; set; }

    /// <summary>自种面积（亩）。</summary>
    public decimal LandSelfArea { get; set; }

    /// <summary>转包面积（亩）。</summary>
    public decimal LandSubleaseArea { get; set; }

    /// <summary>承包面积（亩）。</summary>
    public decimal LandContractArea { get; set; }

    /// <summary>种植补贴年值（元/年）。</summary>
    public decimal SubsidyAnnual { get; set; }
}

/// <summary>
/// 测算所需标准（GET /quick-verify/standards 返回）。
/// 低保标准唯一来源为 <c>nc_config_standards</c>（IStandardConfigService），
/// 单价的唯一来源为 <c>IncomeTypeConstants.LandUnitPrice</c>，比例唯一来源为
/// <c>IncomeTypeConstants.DEFAULT_SUPPORT_FEE_RATIO</c> 与 <c>ClassificationConstants.LowIncomeMultiplier</c>。
/// </summary>
public class QuickVerifyStandards
{
    public string Hukou { get; set; } = string.Empty;
    public string HukouText { get; set; } = string.Empty;

    /// <summary>请求回显年度。系统标准配置未按年度存档，恒按当前生效档读取。</summary>
    public int Year { get; set; }

    /// <summary>年度说明（显式告知：不提供历史年度标准）。</summary>
    public string YearNote { get; set; } = string.Empty;

    public string StandardType { get; set; } = string.Empty;
    public string StandardSource { get; set; } = string.Empty;

    /// <summary>低保标准（元/月）是否可用。</summary>
    public bool StandardAvailable { get; set; }

    /// <summary>标准不可用时的原因（用户可读，由页面提示，不伪造数值）。</summary>
    public string StandardError { get; set; } = string.Empty;

    public decimal SubsistenceStandardMonthly { get; set; }
    public decimal EdgeStandardMonthly { get; set; }

    /// <summary>每子女月赡养费默认值 = 低保标准 × 20%（标准不可用时为 0）。</summary>
    public decimal PerChildMonthlyFee { get; set; }

    public decimal SupportFeeRatio { get; set; }
    public decimal LowIncomeMultiplier { get; set; }

    public decimal SelfFarmUnitPrice { get; set; }
    public decimal SubleaseUnitPrice { get; set; }
    public decimal ContractUnitPrice { get; set; }

    public string UnitPriceText { get; set; } = string.Empty;
    public string StandardText { get; set; } = string.Empty;
    public string EdgeText { get; set; } = string.Empty;
    public string PerChildFeeText { get; set; } = string.Empty;
}

/// <summary>模拟测算明细行（标签 + 已格式化数值，与旧版 SimLineView 一致）。</summary>
public class QuickVerifySimulationLine
{
    public QuickVerifySimulationLine(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; }
    public string Value { get; }
}

/// <summary>
/// 模拟测算结果（POST /quick-verify/simulate 返回）。
/// 【公式单点】全部数值由 Core 的 IIncomeCalculationService 计算，展示文本也由 Core 生成，前端只渲染。
/// 【年值基准】年家庭总收入一次舍入到分；人均年/月均由年值派生（§9）。
/// </summary>
public class QuickVerifySimulationResult
{
    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string Hukou { get; set; } = string.Empty;
    public string HukouText { get; set; } = string.Empty;

    /// <summary>判定家庭人口（不含户主数量 + 1）。</summary>
    public int FamilySize { get; set; }

    public int Children { get; set; }

    /// <summary>本次实际采用的每子女月赡养费（回显）。</summary>
    public decimal PerChildMonthlyFee { get; set; }

    public decimal AlimonyAnnual { get; set; }
    public decimal LandAnnual { get; set; }
    public decimal SubsidyAnnual { get; set; }

    public decimal AnnualFamilyIncome { get; set; }
    public decimal PerCapitaAnnual { get; set; }
    public decimal PerCapitaMonthly { get; set; }

    public decimal SubsistenceStandardMonthly { get; set; }
    public decimal EdgeStandardMonthly { get; set; }

    public decimal LandSelfArea { get; set; }
    public decimal LandSubleaseArea { get; set; }
    public decimal LandContractArea { get; set; }

    public decimal SelfFarmUnitPrice { get; set; }
    public decimal SubleaseUnitPrice { get; set; }
    public decimal ContractUnitPrice { get; set; }

    /// <summary>区间：Low=低保区间 / Edge=边缘区间 / Over=超标区间。</summary>
    public string ZoneKind { get; set; } = string.Empty;

    public string ZoneText { get; set; } = string.Empty;

    public string AnnualIncomeText { get; set; } = string.Empty;
    public string PerCapitaAnnualText { get; set; } = string.Empty;
    public string PerCapitaMonthlyText { get; set; } = string.Empty;
    public string StandardText { get; set; } = string.Empty;
    public string EdgeText { get; set; } = string.Empty;
    public string PerChildFeeText { get; set; } = string.Empty;

    public List<QuickVerifySimulationLine> Lines { get; set; } = new();
}

/// <summary>快速核验单行展示模型（旧版 QuickVerifyItemView 的 Core 版本）。</summary>
public class QuickVerifyVerificationItem
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Badge { get; set; } = string.Empty;

    public bool HasDetail => !string.IsNullOrEmpty(Detail);
    public bool HasValue => !string.IsNullOrEmpty(Value);
    public bool HasBadge => !string.IsNullOrEmpty(Badge);
}

/// <summary>快速核验单组展示模型（10 组固定顺序，零命中组保留但默认由前端隐藏，与旧版一致）。</summary>
public class QuickVerifyVerificationGroup
{
    /// <summary>稳定键（contractLand/historyLand/...），前端折叠与测试用。</summary>
    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public bool IsFailure { get; set; }
    public string FailureReason { get; set; } = string.Empty;

    /// <summary>匹配方式提示（含同名风险提示）。</summary>
    public string MatchNote { get; set; } = string.Empty;

    public int Count { get; set; }
    public string CountText { get; set; } = string.Empty;
    public bool HasItems { get; set; }
    public List<QuickVerifyVerificationItem> Items { get; set; } = new();

    public bool ShowMatchNote => !string.IsNullOrEmpty(MatchNote);
    public string EmptyText => "本组无记录";
}

/// <summary>
/// 台账核验展示结果（GET /quick-verify 返回）。
/// 【同源】10 组标题、明细拼接、"同名风险"提示、姓名来源均由 Core 统一生成，
/// 报告与页面共用同一份视图数据，避免两处各拼一套。
/// </summary>
public class QuickVerifyVerificationView
{
    public string IdCard { get; set; } = string.Empty;
    public string IdCardMasked { get; set; } = string.Empty;

    /// <summary>匹配用姓名（输入值或人员库反查值）。</summary>
    public string QueryName { get; set; } = string.Empty;

    /// <summary>姓名来源（"输入" / "人员库反查" / 空）。</summary>
    public string NameSource { get; set; } = string.Empty;

    /// <summary>姓名展示："{姓名}（来源）" 或 "未匹配到姓名"。</summary>
    public string NameDisplay { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    /// <summary>是否有任一分组命中（false 时页面显示"系统内未查到该身份证的任何台账记录"）。</summary>
    public bool HasAnyRecord { get; set; }

    /// <summary>命中组数。</summary>
    public int HitGroupCount { get; set; }

    /// <summary>固定 10 组（含失败组、零命中组）。</summary>
    public List<QuickVerifyVerificationGroup> Groups { get; set; } = new();

    /// <summary>「从核验带入」土地面积合计（承包台账优先，其次历史确权）。</summary>
    public decimal LandImportTotal { get; set; }

    /// <summary>土地面积来源（"承包台账" / "历史确权" / 空）。</summary>
    public string LandImportSource { get; set; } = string.Empty;

    /// <summary>「从核验带入」种植补贴年值合计（金额型直接取金额，面积型按分类单价折算）。</summary>
    public decimal SubsidyImportTotal { get; set; }

    /// <summary>补贴命中条数。</summary>
    public int SubsidyImportCount { get; set; }
}

/// <summary>
/// 简易报告（POST /quick-verify/report 返回）。
/// 文本由 Core 按旧版 QuickVerifyViewModel.Report.cs 的三段式生成（页面只复制/打印）。
/// </summary>
public class QuickVerifyReport
{
    public string Text { get; set; } = string.Empty;
    public int Length => Text.Length;
    public bool IncomeIncluded { get; set; }
    public bool VerificationIncluded { get; set; }

    /// <summary>核验整体失败时的原因（报告仍可导出收入段，失败原因明示，不冒充无数据）。</summary>
    public string VerificationError { get; set; } = string.Empty;
}

/// <summary>展示文本格式化（Core 单点，禁止前端重复拼口径）。</summary>
internal static class QuickVerifyFormat
{
    public static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    public static string Area(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    public static string AreaShort(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Price(decimal value) => value.ToString("0", CultureInfo.InvariantCulture);

    public static string Date(DateTime? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
}
