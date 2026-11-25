namespace NewCosmos.Server.Contracts;

#region 快速核验 · 模拟测算

/// <summary>
/// 快速核验结果（GET /api/social-assistance/quick-verify）。
/// 10 组固定顺序返回：失败组带原因、零命中组带空态标记（HasItems=false），由前端决定是否隐藏。
/// </summary>
public sealed record QuickVerifyResponse(
    string IdCard,
    string IdCardMasked,
    string QueryName,
    string NameSource,
    string NameDisplay,
    string Summary,
    bool HasAnyRecord,
    int HitGroupCount,
    IReadOnlyList<QuickVerifyGroupDto> Groups,
    QuickVerifyImportSuggestionDto ImportSuggestion);

/// <summary>台账核验单组（IsFailure 时 Items 为空且 FailureReason 有值，绝不冒充无数据）。</summary>
public sealed record QuickVerifyGroupDto(
    string Key,
    string Title,
    bool IsFailure,
    string FailureReason,
    string MatchNote,
    bool HasItems,
    int Count,
    string CountText,
    IReadOnlyList<QuickVerifyItemDto> Items);

/// <summary>台账核验单行（数值已由 Core 格式化）。</summary>
public sealed record QuickVerifyItemDto(
    string Title,
    string Detail,
    string Value,
    string Badge);

/// <summary>「从核验带入」建议值（土地面积与种植补贴年值，Core 汇总，前端不重算）。</summary>
public sealed record QuickVerifyImportSuggestionDto(
    decimal LandTotal,
    string LandSource,
    decimal SubsidyTotal,
    int SubsidyCount);

/// <summary>
/// 测算所需标准（GET /api/social-assistance/quick-verify/standards）。
/// 低保标准来自 nc_config_standards；未配置时 StandardAvailable=false + StandardError，不伪造数值。
/// </summary>
public sealed record QuickVerifyStandardsResponse(
    string Hukou,
    string HukouText,
    int Year,
    string YearNote,
    string StandardType,
    string StandardSource,
    bool StandardAvailable,
    string StandardError,
    decimal SubsistenceStandardMonthly,
    decimal EdgeStandardMonthly,
    decimal PerChildMonthlyFee,
    decimal SupportFeeRatio,
    decimal LowIncomeMultiplier,
    decimal SelfFarmUnitPrice,
    decimal SubleaseUnitPrice,
    decimal ContractUnitPrice,
    string UnitPriceText,
    string StandardText,
    string EdgeText,
    string PerChildFeeText);

/// <summary>模拟测算请求（与旧版输入字段一一对应）。</summary>
public class QuickVerifySimulateRequest
{
    public string? Name { get; set; }
    public string? IdCard { get; set; }
    public string? Hukou { get; set; }

    /// <summary>家庭人口（不含户主）。</summary>
    public int FamilySizeExcludingHead { get; set; }

    public int Children { get; set; }

    /// <summary>每子女月赡养费；null/不传 = 低保标准 × 20%。</summary>
    public decimal? PerChildMonthlyFee { get; set; }

    public decimal LandSelfArea { get; set; }
    public decimal LandSubleaseArea { get; set; }
    public decimal LandContractArea { get; set; }
    public decimal SubsidyAnnual { get; set; }
}

/// <summary>模拟测算结果（数值与展示文本均由 Core 计算/格式化）。</summary>
public sealed record QuickVerifySimulationResponse(
    string Name,
    string IdCard,
    string Hukou,
    string HukouText,
    int FamilySize,
    int Children,
    decimal PerChildMonthlyFee,
    decimal AlimonyAnnual,
    decimal LandAnnual,
    decimal SubsidyAnnual,
    decimal AnnualFamilyIncome,
    decimal PerCapitaAnnual,
    decimal PerCapitaMonthly,
    decimal SubsistenceStandardMonthly,
    decimal EdgeStandardMonthly,
    decimal LandSelfArea,
    decimal LandSubleaseArea,
    decimal LandContractArea,
    decimal SelfFarmUnitPrice,
    decimal SubleaseUnitPrice,
    decimal ContractUnitPrice,
    string ZoneKind,
    string ZoneText,
    string AnnualIncomeText,
    string PerCapitaAnnualText,
    string PerCapitaMonthlyText,
    string StandardText,
    string EdgeText,
    string PerChildFeeText,
    IReadOnlyList<QuickVerifySimulationLineDto> Lines);

/// <summary>测算明细行（标签 + 已格式化数值）。</summary>
public sealed record QuickVerifySimulationLineDto(
    string Label,
    string Value);

/// <summary>报告请求：在测算输入基础上声明报告包含哪几段（与旧版 CanExportReport 语义一致）。</summary>
public sealed class QuickVerifyReportRequest : QuickVerifySimulateRequest
{
    /// <summary>是否包含收入核算段（默认包含）。</summary>
    public bool IncludeIncome { get; set; } = true;

    /// <summary>是否重新执行台账核验段（默认包含）。</summary>
    public bool IncludeVerification { get; set; } = true;
}

/// <summary>报告响应：完整文本由 Core 生成，前端只负责复制/打印（不在 Vue 拼装）。</summary>
public sealed record QuickVerifyReportResponse(
    string Text,
    int Length,
    bool IncomeIncluded,
    bool VerificationIncluded,
    string VerificationError);

#endregion
