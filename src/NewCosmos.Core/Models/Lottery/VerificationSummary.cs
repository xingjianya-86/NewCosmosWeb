namespace NewCosmos.Models.Lottery;

/// <summary>
/// 购彩验证结果摘要（同步开奖后自动验证的返回口径）
/// </summary>
public class VerificationSummary
{
    /// <summary>本次验证注数</summary>
    public int TotalVerified { get; set; }

    /// <summary>本次命中注数</summary>
    public int HitCount { get; set; }

    /// <summary>仍待开奖注数（未匹配到开奖记录）</summary>
    public int PendingCount { get; set; }

    /// <summary>本次命中奖金合计（元）</summary>
    public decimal TotalPrizeAmount { get; set; }

    /// <summary>最佳奖级（0=未中奖，数字越小奖级越高）</summary>
    public int BestPrizeLevel { get; set; }

    /// <summary>最佳奖级描述（legacy 文案）</summary>
    public string BestPrizeDescription { get; set; } = string.Empty;

    /// <summary>命中明细</summary>
    public List<HitDetail> HitDetails { get; set; } = new();

    /// <summary>鼓励文案（legacy 文案）</summary>
    public string EncouragementMessage { get; set; } = string.Empty;
}

/// <summary>
/// 单注命中明细
/// </summary>
public class HitDetail
{
    public string DrawNumber { get; set; } = string.Empty;
    public int RedHits { get; set; }
    public int BlueHits { get; set; }
    public int PrizeLevel { get; set; }
    public decimal PrizeAmount { get; set; }

    /// <summary>购彩号码展示（红 + 蓝）</summary>
    public string Numbers { get; set; } = string.Empty;
}
