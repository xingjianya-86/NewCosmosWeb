using System.Text.Json;
using NewCosmos.Helpers;

namespace NewCosmos.Models.Lottery;

/// <summary>
/// 用户购彩记录（含实际开奖号码，用于命中着色）
/// </summary>
public class UserPurchaseRecord
{
    public int Id { get; set; }
    public LotteryType LotteryType { get; set; }
    public string DrawNumber { get; set; } = string.Empty;
    public string RedNumbersJson { get; set; } = "[]";
    public string BlueNumbersJson { get; set; } = "[]";

    /// <summary>实际开奖红球/前区（无对应开奖记录时为空数组）</summary>
    public string ActualRedNumbersJson { get; set; } = "[]";

    /// <summary>实际开奖蓝球/后区（无对应开奖记录时为空数组）</summary>
    public string ActualBlueNumbersJson { get; set; } = "[]";

    public string Algorithm { get; set; } = string.Empty;
    public DateTime PurchaseDate { get; set; }
    public bool IsVerified { get; set; }
    public bool IsHit { get; set; }
    public int HitRedCount { get; set; }
    public int HitBlueCount { get; set; }
    public int PrizeLevel { get; set; }

    /// <summary>中奖金额（元），0 表示未中奖或奖级明细缺失</summary>
    public decimal PrizeAmount { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>是否待开奖</summary>
    public bool IsPending => !IsVerified;

    /// <summary>状态文本</summary>
    public string StatusText => !IsVerified ? "待开奖" : (IsHit ? "已中奖" : "未中奖");

    /// <summary>奖级文本</summary>
    public string PrizeLevelText => IsHit && PrizeLevel > 0
        ? LotteryPrizeResolver.GetPrizeLevelName(LotteryType, PrizeLevel)
        : string.Empty;

    public int[] GetRedNumbers() => DeserializeInts(RedNumbersJson);

    public int[] GetBlueNumbers() => DeserializeInts(BlueNumbersJson);

    public int[] GetActualRedNumbers() => DeserializeInts(ActualRedNumbersJson);

    public int[] GetActualBlueNumbers() => DeserializeInts(ActualBlueNumbersJson);

    private static int[] DeserializeInts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<int>();

        try
        {
            return JsonSerializer.Deserialize<int[]>(json) ?? Array.Empty<int>();
        }
        catch (JsonException ex)
        {
            Serilog.Log.Warning(ex, "[UserPurchaseRecord] 号码 JSON 解析失败，按空号码处理");
            return Array.Empty<int>();
        }
    }
}
