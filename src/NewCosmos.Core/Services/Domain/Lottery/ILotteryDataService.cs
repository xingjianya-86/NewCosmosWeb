using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.Lottery;

/// <summary>
/// 彩票开奖数据服务：官方接口抓取（双色球/大乐透）、入库与查询。
/// <para>
/// 数据源为官方公开接口；落库表 <c>nc_lottery_draws</c>，也是 LSTM 训练/预测的唯一数据源。
/// </para>
/// </summary>
public interface ILotteryDataService
{
    /// <summary>
    /// 同步开奖历史。sinceDate 为空=全量；非空=增量（遇到更早日期停止翻页）。
    /// 返回真实新增期数（不含仅更新的记录）。
    /// </summary>
    Task<Result<int>> SyncHistoryAsync(
        LotteryType lotteryType,
        DateTime? sinceDate = null,
        CancellationToken ct = default);

    /// <summary>分页获取开奖记录（按开奖日期倒序）。</summary>
    Task<Result<List<LotteryDraw>>> GetDrawListAsync(
        LotteryType lotteryType,
        int page = 1,
        int pageSize = 30,
        CancellationToken ct = default);

    /// <summary>获取开奖记录总数。</summary>
    Task<Result<int>> GetDrawCountAsync(LotteryType lotteryType, CancellationToken ct = default);

    /// <summary>获取最新一期开奖记录；无数据返回 null。</summary>
    Task<Result<LotteryDraw?>> GetLatestDrawAsync(LotteryType lotteryType, CancellationToken ct = default);

    /// <summary>按期号获取开奖记录；不存在返回 null。</summary>
    Task<Result<LotteryDraw?>> GetDrawByNumberAsync(
        LotteryType lotteryType,
        string drawNumber,
        CancellationToken ct = default);

    /// <summary>获取最近 N 期开奖记录（按开奖日期倒序）。</summary>
    Task<Result<List<LotteryDraw>>> GetRecentDrawsAsync(
        LotteryType lotteryType,
        int count = 30,
        CancellationToken ct = default);
}
