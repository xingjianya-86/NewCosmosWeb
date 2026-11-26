using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.Lottery;

/// <summary>
/// 用户购彩记录服务：预测/机选入库、开奖后验证派奖、分页查询与汇总。
/// <para>
/// 奖级判定唯一权威 <see cref="LotteryPrizeRules"/>；奖金解析 <see cref="NewCosmos.Helpers.LotteryPrizeResolver"/>。
/// </para>
/// </summary>
public interface ILotteryPurchaseService
{
    /// <summary>
    /// 批量保存预测/机选为购彩记录。
    /// <para>
    /// sourceJobId 非空时幂等：同一任务重复保存不会重复入库；多条预测以 jobId#序号 派生唯一键。
    /// </para>
    /// </summary>
    Task<Result<int>> SaveFromPredictionsAsync(
        LotteryType lotteryType,
        IReadOnlyList<PredictionResult> predictions,
        string algorithm,
        string? sourceJobId = null,
        CancellationToken ct = default);

    /// <summary>
    /// 验证所有待验证购彩记录（对照已入库开奖结果），事务内批量更新并返回摘要。
    /// </summary>
    Task<Result<VerificationSummary>> VerifyAndRewardAsync(
        LotteryType lotteryType,
        CancellationToken ct = default);

    /// <summary>分页获取购彩记录（含实际开奖号码，用于命中着色）。</summary>
    Task<Result<List<UserPurchaseRecord>>> GetPurchasesAsync(
        LotteryType lotteryType,
        LotteryPurchaseFilter filter = LotteryPurchaseFilter.All,
        int page = 1,
        int pageSize = 30,
        CancellationToken ct = default);

    /// <summary>获取购彩记录总数（可按状态筛选）。</summary>
    Task<Result<int>> GetPurchaseCountAsync(
        LotteryType lotteryType,
        LotteryPurchaseFilter filter = LotteryPurchaseFilter.All,
        CancellationToken ct = default);

    /// <summary>获取购彩汇总统计。</summary>
    Task<Result<LotteryPurchaseSummary>> GetPurchaseSummaryAsync(
        LotteryType lotteryType,
        CancellationToken ct = default);
}
