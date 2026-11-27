using NewCosmos.Constants;
using NewCosmos.Services.Domain.BankFlow;
using Xunit;

namespace NewCosmos.Core.Tests;

/// <summary>
/// 超阈值提示纯逻辑（Step3「从核对数据带入」；权威：BankFlowExceedHint 注释与
/// BankFlowConstants.BalanceExceedFormat）。
/// 锁点：① 阈值 = 月低保标准 × <c>MonthsFor(分类)</c> × max(1, 家庭人数)；
/// ② 月标准缺失（≤0）一律空串跳过提示，禁止硬编码回退值；③ 未超阈值（含恰好等于）空串；
/// ④ 文案按分类分档渲染 30/36 个月，且千分位/小数位与区域设置无关。
/// </summary>
public class BankFlowExceedHintTests
{
    /// <summary>农村低保月标准 632 元（2026-04 起 seed 配置，同 RigidExpenditureRuleTests）</summary>
    private const decimal MonthlyStandard = 632m;

    // ── 阈值公式 ──

    [Fact]
    public void 阈值_等于月标准乘档位月数乘人数()
    {
        // 632 × 30（低保/未判定档）× 3 人 = 56,880
        Assert.Equal(56_880m, BankFlowExceedHint.ComputeThreshold(
            MonthlyStandard, 3, ClassificationConstants.RuralSubsistence));
        // 632 × 36（边缘档）× 2 人 = 45,504
        Assert.Equal(45_504m, BankFlowExceedHint.ComputeThreshold(
            MonthlyStandard, 2, ClassificationConstants.RuralLowIncome));
    }

    [Fact]
    public void 家庭人数_小于1时按1人计()
    {
        Assert.Equal(1, BankFlowExceedHint.EffectiveFamilySize(0));
        Assert.Equal(1, BankFlowExceedHint.EffectiveFamilySize(-3));
        Assert.Equal(5, BankFlowExceedHint.EffectiveFamilySize(5));

        // 0 人不得把阈值算成 0（否则任何余额都会“超阈值”误导）
        Assert.Equal(18_960m, BankFlowExceedHint.ComputeThreshold(
            MonthlyStandard, 0, null));
    }

    [Fact]
    public void 月标准未配置_阈值为0且不产出提示()
    {
        Assert.Equal(0m, BankFlowExceedHint.ComputeThreshold(0m, 3, null));
        Assert.Equal(0m, BankFlowExceedHint.ComputeThreshold(-1m, 3, null));

        Assert.Equal(string.Empty, BankFlowExceedHint.Build(999_999m, 0m, 3, null));
        Assert.Equal(string.Empty, BankFlowExceedHint.Build(999_999m, -1m, 3, null));
    }

    // ── 提示产出 ──

    [Fact]
    public void 未超阈值_返回空串()
    {
        // 阈值 = 632 × 30 × 2 = 37,920
        Assert.Equal(string.Empty, BankFlowExceedHint.Build(
            37_920m, MonthlyStandard, 2, ClassificationConstants.RuralSubsistence));
        // 恰好等于阈值不算超（沿用桌面版 totalBalance > threshold 严格大于）
        Assert.Equal(string.Empty, BankFlowExceedHint.Build(
            37_919.99m, MonthlyStandard, 2, ClassificationConstants.RuralSubsistence));
    }

    [Fact]
    public void 超阈值_渲染30个月档文案()
    {
        var text = BankFlowExceedHint.Build(
            50_000m, MonthlyStandard, 2, ClassificationConstants.RuralSubsistence);

        Assert.NotEqual(string.Empty, text);
        Assert.Contains("50,000.00", text);
        Assert.Contains("37,920.00", text);
        Assert.Contains("超过 30 个月低保标准", text);
        Assert.Contains("2 人 × 30 × 月标准", text);
        Assert.Contains("仅提示，请人工核定", text);
    }

    [Fact]
    public void 超阈值_边缘分类渲染36个月档()
    {
        var text = BankFlowExceedHint.Build(
            50_000m, MonthlyStandard, 2, ClassificationConstants.RuralLowIncome);

        Assert.Contains("超过 36 个月低保标准", text);
        Assert.Contains("2 人 × 36 × 月标准", text);
        Assert.Contains("45,504.00", text);
    }

    [Fact]
    public void 超阈值_未知分类码从严按30个月()
    {
        // 未知/未判定 → MonthsFor 回落 30（禁止回落 36 宽松方向）
        var text = BankFlowExceedHint.Build(
            40_000m, MonthlyStandard, 2, "NoSuchClassificationCode");

        Assert.Contains("30 个月", text);
        Assert.Contains("37,920.00", text);
    }

    [Fact]
    public void 提示文案_千分位与小数位不随区域设置变化()
    {
        // 阈值 = 632 × 30 × 1 人 = 18,960.00；余额超阈值才产出文案
        var text = BankFlowExceedHint.Build(
            25_000.6m, MonthlyStandard, 1, ClassificationConstants.RuralSubsistence);

        Assert.Contains("25,000.60", text);
        Assert.Contains("18,960.00", text);
    }
}
