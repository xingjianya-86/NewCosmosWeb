using NewCosmos.Helpers;
using NewCosmos.Models.Lottery;

namespace NewCosmos.Services.Domain.Lottery;

/// <summary>
/// 彩票奖级判定规则（唯一权威：legacy/NewCosmos.Desktop/Services/Lottery/UserPurchaseService.cs）。
/// <para>
/// 双色球 6 个奖级；大乐透第 26014 期（2026-02-02 首期开奖）起由 9 个奖级优化为 7 个奖级，
/// 之前按 9 个奖级判定。奖金解析统一走 <see cref="LotteryPrizeResolver"/>。
/// </para>
/// </summary>
public static class LotteryPrizeRules
{
    /// <summary>大乐透新规则（7 个奖级）首期期号序号。</summary>
    public const int DLT_SEVEN_LEVEL_START_SEQUENCE = 26014;

    /// <summary>大乐透新规则首期开奖日（期号缺失时的兜底判定）。</summary>
    public static readonly DateTime DLT_SEVEN_LEVEL_START_DATE = new(2026, 2, 2);

    /// <summary>
    /// 判定奖级。返回 0 表示未中奖。
    /// </summary>
    public static int DeterminePrizeLevel(
        LotteryType lotteryType,
        int redHitCount,
        int blueHitCount,
        string? drawNumber,
        DateTime drawDate)
    {
        if (lotteryType == LotteryType.SSQ)
            return DetermineSsqPrizeLevel(redHitCount, blueHitCount);

        return IsDltNewRules(drawNumber, drawDate)
            ? DetermineDltPrizeLevel7(redHitCount, blueHitCount)
            : DetermineDltPrizeLevel9(redHitCount, blueHitCount);
    }

    /// <summary>双色球 6 个奖级</summary>
    public static int DetermineSsqPrizeLevel(int redHitCount, int blueHitCount)
    {
        if (redHitCount == 6 && blueHitCount == 1) return 1;
        if (redHitCount == 6) return 2;
        if (redHitCount == 5 && blueHitCount == 1) return 3;
        if (redHitCount == 5 || (redHitCount == 4 && blueHitCount == 1)) return 4;
        if (redHitCount == 4 || (redHitCount == 3 && blueHitCount == 1)) return 5;
        if (blueHitCount == 1) return 6;
        return 0;
    }

    /// <summary>大乐透是否适用 2026-02-02（第 26014 期）起的新规则（7 个奖级）</summary>
    public static bool IsDltNewRules(string? drawNumber, DateTime drawDate)
    {
        var seq = ParseDltDrawSequence(drawNumber);
        if (seq.HasValue)
            return seq.Value >= DLT_SEVEN_LEVEL_START_SEQUENCE;

        return drawDate.Date >= DLT_SEVEN_LEVEL_START_DATE;
    }

    /// <summary>解析大乐透期号序号，兼容 "26014" 与 "2026014" 两种格式；失败返回 null。</summary>
    public static int? ParseDltDrawSequence(string? drawNumber)
    {
        if (string.IsNullOrWhiteSpace(drawNumber))
            return null;

        var digits = new string(drawNumber.Where(char.IsDigit).ToArray());
        if (digits.Length == 0 || !int.TryParse(digits, out var n))
            return null;

        return n >= 100000 ? n % 100000 : n;
    }

    /// <summary>大乐透新规则（第 26014 期起）：7 个奖级</summary>
    public static int DetermineDltPrizeLevel7(int redHitCount, int blueHitCount)
    {
        if (redHitCount == 5 && blueHitCount == 2) return 1;                        // 5+2
        if (redHitCount == 5 && blueHitCount == 1) return 2;                        // 5+1
        if (redHitCount == 5 || (redHitCount == 4 && blueHitCount == 2)) return 3;  // 5+0 / 4+2
        if (redHitCount == 4 && blueHitCount == 1) return 4;                        // 4+1
        if (redHitCount == 4 || (redHitCount == 3 && blueHitCount == 2)) return 5;  // 4+0 / 3+2
        if ((redHitCount == 3 && blueHitCount == 1) || (redHitCount == 2 && blueHitCount == 2)) return 6; // 3+1 / 2+2
        if (redHitCount == 3 || (redHitCount == 2 && blueHitCount == 1)
            || (redHitCount == 1 && blueHitCount == 2) || blueHitCount == 2) return 7; // 3+0 / 2+1 / 1+2 / 0+2
        return 0;
    }

    /// <summary>大乐透旧规则（第 26013 期及以前）：9 个奖级</summary>
    public static int DetermineDltPrizeLevel9(int redHitCount, int blueHitCount)
    {
        if (redHitCount == 5 && blueHitCount == 2) return 1;                        // 5+2
        if (redHitCount == 5 && blueHitCount == 1) return 2;                        // 5+1
        if (redHitCount == 5) return 3;                                             // 5+0
        if (redHitCount == 4 && blueHitCount == 2) return 4;                        // 4+2
        if (redHitCount == 4 && blueHitCount == 1) return 5;                        // 4+1
        if (redHitCount == 3 && blueHitCount == 2) return 6;                        // 3+2
        if (redHitCount == 4) return 7;                                             // 4+0
        if ((redHitCount == 3 && blueHitCount == 1) || (redHitCount == 2 && blueHitCount == 2)) return 8; // 3+1 / 2+2
        if (redHitCount == 3 || (redHitCount == 2 && blueHitCount == 1)
            || (redHitCount == 1 && blueHitCount == 2) || blueHitCount == 2) return 9; // 3+0 / 2+1 / 1+2 / 0+2
        return 0;
    }

    /// <summary>奖级中文名（纯文本，供购彩记录展示）</summary>
    public static string GetPrizeLevelText(LotteryType lotteryType, int level)
        => LotteryPrizeResolver.GetPrizeLevelName(lotteryType, level);

    /// <summary>
    /// 最佳奖级描述（legacy 文案，含表情；用于验证摘要）。
    /// </summary>
    public static string GetPrizeDescription(LotteryType lotteryType, int level) => level switch
    {
        1 => "🏆 一等奖！",
        2 => "🥈 二等奖！",
        3 => "🥉 三等奖！",
        4 => "四等奖",
        5 => "五等奖",
        6 => "六等奖",
        7 => "七等奖",
        8 => "八等奖",
        9 => "九等奖",
        _ => $"第{level}等奖"
    };
}
