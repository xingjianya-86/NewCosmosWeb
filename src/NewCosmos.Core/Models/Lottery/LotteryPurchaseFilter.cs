namespace NewCosmos.Models.Lottery;

/// <summary>
/// 购彩记录状态筛选（与 legacy 桌面端一致）
/// </summary>
public enum LotteryPurchaseFilter
{
    /// <summary>全部</summary>
    All = 0,

    /// <summary>待开奖（尚未验证）</summary>
    Pending = 1,

    /// <summary>已中奖</summary>
    Hit = 2,

    /// <summary>未中奖</summary>
    Miss = 3
}
