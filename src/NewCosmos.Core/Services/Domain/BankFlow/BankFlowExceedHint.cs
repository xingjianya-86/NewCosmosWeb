using System.Globalization;
using NewCosmos.Constants;

namespace NewCosmos.Services.Domain.BankFlow;

/// <summary>
/// 银行流水核对「超阈值提示」纯逻辑（业务公式只在 Core，Web/Server 不重算金额）。
/// <para>
/// 阈值 = 月低保标准 × <see cref="BankFlowConstants.MonthsFor"/>（按分类分档 30/36）
/// × max(1, 家庭人数)；命中仅生成红字提示，**不自动决策**（权威判定仍走分类流程）。
/// </para>
/// <para>
/// 标准配置缺失（月标准 ≤ 0）一律返回空串跳过提示，禁止硬编码回退值误导群众。
/// </para>
/// </summary>
public static class BankFlowExceedHint
{
    /// <summary>家庭人数兜底：至少按 1 人（申请人）计入阈值。</summary>
    public static int EffectiveFamilySize(int familySize) => familySize < 1 ? 1 : familySize;

    /// <summary>
    /// 阈值金额（元）。月标准未配置（≤ 0）返回 0，由 <see cref="Build"/> 据此跳过提示。
    /// </summary>
    public static decimal ComputeThreshold(
        decimal monthlyStandard,
        int familySize,
        string? classificationCode)
    {
        if (monthlyStandard <= 0)
        {
            return 0m;
        }

        return monthlyStandard
               * BankFlowConstants.MonthsFor(classificationCode)
               * EffectiveFamilySize(familySize);
    }

    /// <summary>
    /// 生成超阈值提示文案；月标准缺失或未超阈值返回空串（不提示、不误导）。
    /// 文案模板与参数顺序见 <see cref="BankFlowConstants.BalanceExceedFormat"/>：
    /// {0}=合计余额，{1}=人数，{2}=阈值金额，{3}=货币财产月数。
    /// </summary>
    public static string Build(
        decimal totalBalance,
        decimal monthlyStandard,
        int familySize,
        string? classificationCode)
    {
        var threshold = ComputeThreshold(monthlyStandard, familySize, classificationCode);
        if (threshold <= 0 || totalBalance <= threshold)
        {
            return string.Empty;
        }

        // 千分位与小数位固定用 InvariantCulture，保证提示文案与运行区域设置无关。
        return string.Format(
            CultureInfo.InvariantCulture,
            BankFlowConstants.BalanceExceedFormat,
            totalBalance.ToString("N2", CultureInfo.InvariantCulture),
            EffectiveFamilySize(familySize),
            threshold.ToString("N2", CultureInfo.InvariantCulture),
            BankFlowConstants.MonthsFor(classificationCode));
    }
}
