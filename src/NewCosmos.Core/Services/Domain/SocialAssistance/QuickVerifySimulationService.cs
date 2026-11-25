using System.Globalization;
using System.Text;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.System;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 快速核验 · 模拟测算服务实现。
/// 【复刻来源】旧版 ViewModels/SocialAssistance/QuickVerifyViewModel.Income.cs（收入核算）与
/// .Report.cs（报告文本）。所有金额计算均转调 IIncomeCalculationService，标准转调 IStandardConfigService，
/// 单价/比例复用 Constants，禁止本类另写一套公式或硬编码标准。
/// 【只读】不写任何表；审计只记动作与脱敏身份证（§10）。
/// </summary>
public class QuickVerifySimulationService : BaseService, IQuickVerifySimulationService
{
    protected override string ServiceName => "QuickVerifySimulationService";

    private readonly IQuickVerifyService _quickVerifyService;
    private readonly IStandardConfigService _standardConfigService;
    private readonly IIncomeCalculationService _incomeCalculationService;

    private const string StandardTypeRural = "RuralSubsistenceStandard";
    private const string StandardTypeUrban = "UrbanSubsistenceStandard";

    public QuickVerifySimulationService(
        IQuickVerifyService quickVerifyService,
        IStandardConfigService standardConfigService,
        IIncomeCalculationService incomeCalculationService,
        ILoggerService logger) : base(logger)
    {
        _quickVerifyService = quickVerifyService;
        _standardConfigService = standardConfigService;
        _incomeCalculationService = incomeCalculationService;
    }

    #region 台账核验展示

    public async Task<Result<QuickVerifyVerificationView>> VerifyViewAsync(
        string idCard, string? name, CancellationToken ct = default)
    {
        try
        {
            var result = await _quickVerifyService.VerifyAsync(idCard, name, ct);
            if (result.IsFailure)
                return Result.Failure<QuickVerifyVerificationView>(
                    result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "台账查询失败");

            return Result.Success(QuickVerifyVerificationPresenter.Build(result.Value!));
        }
        catch (Exception ex)
        {
            LogException(ex, "快速核验台账查询失败");
            return Result.FromException<QuickVerifyVerificationView>(ex);
        }
    }

    #endregion

    #region 标准读取

    public async Task<Result<QuickVerifyStandards>> GetStandardsAsync(
        string? hukouType, int? year, CancellationToken ct = default)
    {
        if (!TryNormalizeHukou(hukouType, out var hukou, out var hukouText))
            return Result.Failure<QuickVerifyStandards>(ErrorCodes.VALIDATION_FAILED,
                "户口性质不正确（应为农村户口或城市户口）");

        var standardType = hukou == ClassificationConstants.HukouType.URBAN
            ? StandardTypeUrban
            : StandardTypeRural;

        var standards = new QuickVerifyStandards
        {
            Hukou = hukou,
            HukouText = hukouText,
            Year = year.HasValue && year.Value is >= 1900 and <= 9999
                ? year.Value
                : DateTime.Now.Year,
            YearNote = "标准按当前生效配置读取（系统不按年度存档历史标准），year 仅用于回显。",
            StandardType = standardType,
            StandardSource = "数据中心-标准配置管理",
            SupportFeeRatio = IncomeTypeConstants.DEFAULT_SUPPORT_FEE_RATIO,
            LowIncomeMultiplier = ClassificationConstants.LowIncomeMultiplier,
            SelfFarmUnitPrice = IncomeTypeConstants.LandUnitPrice.SELF_FARM,
            SubleaseUnitPrice = IncomeTypeConstants.LandUnitPrice.SUBLEASE,
            ContractUnitPrice = IncomeTypeConstants.LandUnitPrice.CONTRACT
        };
        standards.UnitPriceText =
            $"单价：自种 {QuickVerifyFormat.Price(standards.SelfFarmUnitPrice)} ｜ " +
            $"转包 {QuickVerifyFormat.Price(standards.SubleaseUnitPrice)} ｜ " +
            $"承包 {QuickVerifyFormat.Price(standards.ContractUnitPrice)} 元/亩";

        var standardResult = await _standardConfigService.GetStandardValueAsync(
            standardType, hukou, ct: ct);
        if (standardResult.IsFailure)
        {
            return Result.Failure<QuickVerifyStandards>(
                standardResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                standardResult.Message ?? "低保标准读取失败");
        }

        if (standardResult.Value <= 0)
        {
            // 未配置 ≠ 查询失败：显式告知标准不可用，不伪造数值（旧版在核算时显式中止）
            standards.StandardAvailable = false;
            standards.StandardError =
                $"低保标准配置缺失或无效（{standardType}/{hukou}），请在「数据中心-标准配置管理」维护后重试。";
            return Result.Success(standards);
        }

        ApplyStandardValues(standards, standardResult.Value);
        return Result.Success(standards);
    }

    private static void ApplyStandardValues(QuickVerifyStandards standards, decimal standardMonthly)
    {
        standards.StandardAvailable = true;
        standards.StandardError = string.Empty;
        standards.SubsistenceStandardMonthly = standardMonthly;
        standards.EdgeStandardMonthly = standardMonthly * ClassificationConstants.LowIncomeMultiplier;
        standards.PerChildMonthlyFee = Math.Round(
            standardMonthly * IncomeTypeConstants.DEFAULT_SUPPORT_FEE_RATIO, 2);
        standards.StandardText = $"{QuickVerifyFormat.Money(standardMonthly)} 元/月";
        standards.EdgeText = $"{QuickVerifyFormat.Money(standards.EdgeStandardMonthly)} 元/月";
        standards.PerChildFeeText = QuickVerifyFormat.AreaShort(standards.PerChildMonthlyFee);
    }

    #endregion

    #region 收入模拟测算

    public async Task<Result<QuickVerifySimulationResult>> SimulateAsync(
        QuickVerifySimulationInput input, CancellationToken ct = default)
    {
        var validationError = ValidateInput(input);
        if (validationError is not null)
            return Result.Failure<QuickVerifySimulationResult>(ErrorCodes.VALIDATION_FAILED, validationError);

        var idCard = (input.IdCard ?? string.Empty).Trim().ToUpperInvariant();
        var name = (input.Name ?? string.Empty).Trim();
        TryNormalizeHukou(input.HukouType, out var hukou, out var hukouText);
        var standardType = hukou == ClassificationConstants.HukouType.URBAN
            ? StandardTypeUrban
            : StandardTypeRural;

        var standardResult = await ReadStandardAsync(standardType, hukou, ct);
        if (standardResult.IsFailure)
            return Result.Failure<QuickVerifySimulationResult>(
                standardResult.ErrorCode ?? ErrorCodes.VALIDATION_FAILED,
                standardResult.Message ?? "低保标准读取失败");

        var standard = standardResult.Value;
        var familySize = input.FamilySizeExcludingHead + 1;
        var children = input.Children;

        // ── 每子女月费：留空 = 低保标准 × 20%（与申请表 GetCachedSubsistenceStandardAsync 同机制）──
        var perChildFee = input.PerChildMonthlyFee.HasValue && input.PerChildMonthlyFee.Value >= 0
            ? input.PerChildMonthlyFee.Value
            : Math.Round(standard * IncomeTypeConstants.DEFAULT_SUPPORT_FEE_RATIO, 2);

        // ── 赡养年值：Supporter 实体权威公式（月费×月数×申请表家庭人口），每子女一行 ──
        var supporters = new List<Supporter>(children);
        for (var i = 0; i < children; i++)
        {
            supporters.Add(new Supporter
            {
                MonthlySupportFee = perChildFee,
                SupportMonths = 12,
                CalcFamilySize = familySize
            });
        }
        var alimonyAnnual = _incomeCalculationService.CalculateAlimonyAnnual(supporters);

        // ── 土地年值：三用途面积 × 单价（权威单点）──
        var landAnnual = _incomeCalculationService.CalculateLandIncome(
            input.LandSelfArea, (double)IncomeTypeConstants.LandUnitPrice.SELF_FARM,
            input.LandSubleaseArea, (double)IncomeTypeConstants.LandUnitPrice.SUBLEASE,
            input.LandContractArea, (double)IncomeTypeConstants.LandUnitPrice.CONTRACT);

        // ── 补贴年值（已是年值）──
        var subsidyAnnual = input.SubsidyAnnual;

        // ── 年家庭总收入（月项与刚性支出均为 0，仍走权威服务保证口径与舍入一致）──
        var annual = _incomeCalculationService.CalculateAnnualFamilyIncome(
            0, 0, 0, 0, 0, alimonyAnnual, landAnnual, subsidyAnnual, 0);
        var perCapitaAnnual = _incomeCalculationService.CalculatePerCapitaAnnual(annual, familySize);
        var perCapitaMonthly = _incomeCalculationService.PerCapitaMonthly(annual, familySize);

        // ── 标准与区间（口径与 ClassificationService 三层判定一致：超标 ≥ 边缘 > 低保）──
        var edgeThreshold = standard * ClassificationConstants.LowIncomeMultiplier;
        var zoneKind = perCapitaMonthly >= edgeThreshold ? "Over"
            : perCapitaMonthly >= standard ? "Edge"
            : "Low";
        var zoneText = zoneKind switch
        {
            "Low" => $"月人均 {QuickVerifyFormat.Money(perCapitaMonthly)} 元低于低保标准 → 低保区间",
            "Edge" => $"月人均 {QuickVerifyFormat.Money(perCapitaMonthly)} 元在低保~边缘标准之间 → 边缘区间",
            _ => $"月人均 {QuickVerifyFormat.Money(perCapitaMonthly)} 元超出边缘标准上限 → 超标区间"
        };

        var result = new QuickVerifySimulationResult
        {
            Name = name,
            IdCard = idCard,
            Hukou = hukou,
            HukouText = hukouText,
            FamilySize = familySize,
            Children = children,
            PerChildMonthlyFee = perChildFee,
            AlimonyAnnual = alimonyAnnual,
            LandAnnual = landAnnual,
            SubsidyAnnual = subsidyAnnual,
            AnnualFamilyIncome = annual,
            PerCapitaAnnual = perCapitaAnnual,
            PerCapitaMonthly = perCapitaMonthly,
            SubsistenceStandardMonthly = standard,
            EdgeStandardMonthly = edgeThreshold,
            LandSelfArea = input.LandSelfArea,
            LandSubleaseArea = input.LandSubleaseArea,
            LandContractArea = input.LandContractArea,
            SelfFarmUnitPrice = IncomeTypeConstants.LandUnitPrice.SELF_FARM,
            SubleaseUnitPrice = IncomeTypeConstants.LandUnitPrice.SUBLEASE,
            ContractUnitPrice = IncomeTypeConstants.LandUnitPrice.CONTRACT,
            ZoneKind = zoneKind,
            ZoneText = zoneText,
            AnnualIncomeText = $"{QuickVerifyFormat.Money(annual)} 元/年",
            PerCapitaAnnualText = $"{QuickVerifyFormat.Money(perCapitaAnnual)} 元/年",
            PerCapitaMonthlyText = $"{QuickVerifyFormat.Money(perCapitaMonthly)} 元/月",
            StandardText = $"{QuickVerifyFormat.Money(standard)} 元/月",
            EdgeText = $"{QuickVerifyFormat.Money(edgeThreshold)} 元/月",
            PerChildFeeText = QuickVerifyFormat.AreaShort(perChildFee)
        };

        result.Lines.Add(new QuickVerifySimulationLine(
            $"赡养费年值（{children} 子女 × {QuickVerifyFormat.Money(perChildFee)} 元/月 × 12 × {familySize} 人）",
            QuickVerifyFormat.Money(alimonyAnnual)));
        result.Lines.Add(new QuickVerifySimulationLine(
            $"土地（自种 {QuickVerifyFormat.AreaShort(input.LandSelfArea)} 亩×{QuickVerifyFormat.Price(result.SelfFarmUnitPrice)}" +
            $" + 转包 {QuickVerifyFormat.AreaShort(input.LandSubleaseArea)} 亩×{QuickVerifyFormat.Price(result.SubleaseUnitPrice)}" +
            $" + 承包 {QuickVerifyFormat.AreaShort(input.LandContractArea)} 亩×{QuickVerifyFormat.Price(result.ContractUnitPrice)}）",
            QuickVerifyFormat.Money(landAnnual)));
        result.Lines.Add(new QuickVerifySimulationLine(
            "种植补贴年值", QuickVerifyFormat.Money(subsidyAnnual)));
        result.Lines.Add(new QuickVerifySimulationLine(
            "══ 年家庭总收入", QuickVerifyFormat.Money(annual)));

        // 业务审计点：测算属敏感家庭经济数据操作（身份证脱敏、不记金额明细）
        Logger.LogBusiness("快速核验模拟测算",
            ("IdCard", DataMasker.MaskIdCard(idCard)),
            ("Hukou", hukou),
            ("FamilySize", familySize),
            ("Children", children));

        return Result.Success(result);
    }

    private async Task<Result<decimal>> ReadStandardAsync(
        string standardType, string hukou, CancellationToken ct)
    {
        var standardResult = await _standardConfigService.GetStandardValueAsync(
            standardType, hukou, ct: ct);
        if (standardResult.IsFailure)
        {
            return Result.Failure<decimal>(
                standardResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                standardResult.Message ?? "低保标准读取失败");
        }

        // 标准缺失 = 显式失败（禁止回退过期硬编码标准，与申请表/分类判定同纪律）
        if (standardResult.Value <= 0)
        {
            return Result.Failure<decimal>(ErrorCodes.VALIDATION_FAILED,
                $"低保标准配置缺失或无效（{standardType}/{hukou}），请在「数据中心-标准配置管理」维护后重试。");
        }

        return Result.Success(standardResult.Value);
    }

    /// <summary>校验输入；返回 null = 通过，否则返回用户可读文案（与旧版 ValidateIncomeInputs 一致）。</summary>
    private static string? ValidateInput(QuickVerifySimulationInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return "请输入姓名";
        if (!IdCardValidator.IsValid((input.IdCard ?? string.Empty).Trim()))
            return "请输入正确的 18 位身份证号";
        if (!TryNormalizeHukou(input.HukouType, out _, out _))
            return "户口性质不正确（应为农村户口或城市户口）";
        if (input.FamilySizeExcludingHead < 0)
            return "家庭人口（不含户主）请输入 0 或正整数";
        if (input.Children < 0)
            return "子女数量请输入 0 或正整数";
        // 服务端防护：避免异常大值导致赡养人列表分配/循环失控（线下测算真实值远小于此上限）
        if (input.FamilySizeExcludingHead > 500)
            return "家庭人口（不含户主）超出合理范围";
        if (input.Children > 100)
            return "子女数量超出合理范围";

        foreach (var (label, value) in new[]
                 {
                     ("自种面积", input.LandSelfArea),
                     ("转包面积", input.LandSubleaseArea),
                     ("承包面积", input.LandContractArea),
                     ("补贴年值", input.SubsidyAnnual)
                 })
        {
            if (value < 0)
                return $"{label}格式不正确";
        }

        if (input.PerChildMonthlyFee.HasValue && input.PerChildMonthlyFee.Value < 0)
            return "每子女月赡养费格式不正确";

        return null;
    }

    /// <summary>户口归一：空值按农村（与旧版默认选中项一致），非法值返回 false。</summary>
    private static bool TryNormalizeHukou(string? raw, out string hukou, out string hukouText)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            hukou = ClassificationConstants.HukouType.RURAL;
            hukouText = "农村";
            return true;
        }

        if (ClassificationConstants.HukouType.IsHukouUrban(raw))
        {
            hukou = ClassificationConstants.HukouType.URBAN;
            hukouText = "城镇";
            return true;
        }

        if (ClassificationConstants.HukouType.IsHukouRural(raw))
        {
            hukou = ClassificationConstants.HukouType.RURAL;
            hukouText = "农村";
            return true;
        }

        hukou = string.Empty;
        hukouText = string.Empty;
        return false;
    }

    #endregion

    #region 报告文本

    public async Task<Result<QuickVerifyReport>> BuildReportAsync(
        QuickVerifySimulationInput input,
        bool includeIncome,
        bool includeVerification,
        CancellationToken ct = default)
    {
        if (!includeIncome && !includeVerification)
        {
            return Result.Failure<QuickVerifyReport>(ErrorCodes.VALIDATION_FAILED,
                "请先「开始核算」或触发核验后再导出");
        }

        if (!IdCardValidator.IsValid((input.IdCard ?? string.Empty).Trim()))
        {
            return Result.Failure<QuickVerifyReport>(ErrorCodes.VALIDATION_FAILED,
                "请输入正确的 18 位身份证号");
        }

        if (!TryNormalizeHukou(input.HukouType, out _, out _))
        {
            return Result.Failure<QuickVerifyReport>(ErrorCodes.VALIDATION_FAILED,
                "户口性质不正确（应为农村户口或城市户口）");
        }

        if (input.FamilySizeExcludingHead < 0 || input.Children < 0)
        {
            return Result.Failure<QuickVerifyReport>(ErrorCodes.VALIDATION_FAILED,
                "家庭人口（不含户主）与子女数量请输入 0 或正整数");
        }

        QuickVerifySimulationResult? simulation = null;
        if (includeIncome)
        {
            var simulationResult = await SimulateAsync(input, ct);
            if (simulationResult.IsFailure)
            {
                return Result.Failure<QuickVerifyReport>(
                    simulationResult.ErrorCode ?? ErrorCodes.VALIDATION_FAILED,
                    simulationResult.Message ?? "收入测算失败");
            }
            simulation = simulationResult.Value;
        }

        QuickVerifyVerificationView? verification = null;
        var verificationError = string.Empty;
        var verificationIncluded = false;
        if (includeVerification)
        {
            var verifyResult = await VerifyViewAsync(input.IdCard ?? string.Empty, input.Name, ct);
            if (verifyResult.IsFailure)
            {
                // 核验失败仍可导出收入段，但必须明示失败原因（§7）
                verificationError = verifyResult.Message ?? "台账查询失败";
            }
            else
            {
                verification = verifyResult.Value;
                verificationIncluded = true;
            }
        }

        var text = BuildReportText(input, simulation, verification, verificationError, DateTime.Now);

        Logger.LogBusiness("导出简易报告",
            ("IdCard", DataMasker.MaskIdCard((input.IdCard ?? string.Empty).Trim())),
            ("Length", text.Length),
            ("Income", includeIncome),
            ("Verify", verificationIncluded));

        return Result.Success(new QuickVerifyReport
        {
            Text = text,
            IncomeIncluded = simulation is not null,
            VerificationIncluded = verificationIncluded,
            VerificationError = verificationError
        });
    }

    /// <summary>
    /// 三段式报告（与旧版 Report.cs 同结构）：一、被核算人；二、收入核算；三、台账核验（只读）。
    /// 身份证完整显示（内部核对用，2026-11-15 拍板）；末尾附打印方式说明（不伪造 Office 公文打印）。
    /// </summary>
    private static string BuildReportText(
        QuickVerifySimulationInput input,
        QuickVerifySimulationResult? simulation,
        QuickVerifyVerificationView? verification,
        string verificationError,
        DateTime generatedAt)
    {
        var sb = new StringBuilder();
        var familySize = input.FamilySizeExcludingHead + 1;
        TryNormalizeHukou(input.HukouType, out _, out var hukouText);

        sb.AppendLine("══════ 家庭收入核算报告 ══════");
        sb.AppendLine($"生成时间：{generatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");

        // ── 一、被核算人 ──
        sb.AppendLine();
        sb.AppendLine("一、被核算人");
        sb.AppendLine($"姓名：{(input.Name ?? string.Empty).Trim()}");
        sb.AppendLine($"身份证号：{(input.IdCard ?? string.Empty).Trim().ToUpperInvariant()}");
        sb.AppendLine($"户口性质：{hukouText} ｜ 家庭人口：{familySize} 人（含户主）｜ 子女数量：{input.Children} 人");

        // ── 二、收入核算 ──
        sb.AppendLine();
        sb.AppendLine("二、收入核算");
        if (simulation is null)
        {
            sb.AppendLine("（未核算——本次仅执行了台账核验）");
        }
        else
        {
            foreach (var line in simulation.Lines)
                sb.AppendLine($"{line.Label}：{line.Value}");

            sb.AppendLine($"年人均 {simulation.PerCapitaAnnualText} ｜ 月人均 {simulation.PerCapitaMonthlyText}");
            sb.AppendLine($"低保标准 {simulation.StandardText} ｜ 边缘标准上限 {simulation.EdgeText}");
            sb.AppendLine($"区间结论：{simulation.ZoneText}");
        }

        // ── 三、台账核验 ──
        sb.AppendLine();
        sb.AppendLine("三、台账核验（只读）");
        if (!string.IsNullOrWhiteSpace(verificationError))
        {
            sb.AppendLine($"台账核验失败：{verificationError}");
        }
        else if (verification is null)
        {
            sb.AppendLine("（未核验——本次仅执行了收入核算）");
        }
        else
        {
            var emptyTitles = new List<string>();
            foreach (var group in verification.Groups)
            {
                if (group.IsFailure)
                {
                    sb.AppendLine($"▶ {group.Title}：查询失败 - {group.FailureReason}");
                    continue;
                }

                if (!group.HasItems)
                {
                    emptyTitles.Add(StripIcon(group.Title));
                    continue;
                }

                var header = string.IsNullOrEmpty(group.MatchNote)
                    ? group.Title
                    : $"{group.Title}（{group.MatchNote}）";
                var countText = string.IsNullOrEmpty(group.CountText) ? string.Empty : $" {group.CountText}";
                sb.AppendLine($"▶ {header}{countText}：");

                var index = 0;
                foreach (var item in group.Items)
                {
                    index++;
                    var parts = new List<string> { item.Title };
                    if (item.HasDetail) parts.Add(item.Detail);
                    if (item.HasValue) parts.Add(item.Value);
                    if (item.HasBadge) parts.Add($"【{item.Badge}】");
                    sb.AppendLine($"   {index}. {string.Join("｜", parts)}");
                }
            }

            if (emptyTitles.Count > 0)
                sb.AppendLine($"无记录：{string.Join("、", emptyTitles)}");
        }

        sb.AppendLine("说明：本报告由网页版「快速核验 · 模拟测算」结果生成，仅供内部核对；");
        sb.AppendLine("      网页端「打印」使用浏览器打印，不生成公文格式 Word/PDF（公文输出请走桌面端 Agent）。");
        sb.AppendLine("════════════════════════════");
        return sb.ToString();
    }

    /// <summary>去掉标题前的 emoji 前缀，让"无记录：…"名单更干净（与旧版一致）。</summary>
    private static string StripIcon(string title)
    {
        var idx = title.IndexOf(' ');
        return idx > 0 && idx <= 5 ? title[(idx + 1)..] : title;
    }

    #endregion
}
