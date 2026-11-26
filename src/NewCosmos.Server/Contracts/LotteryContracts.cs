using System.Text.Json;

namespace NewCosmos.Server.Contracts;

// ───────────────────────────── 请求 ─────────────────────────────

/// <summary>仅带彩种类型的请求体（同步/验证）。Full=true 时全量同步（用于历史数据补齐）。</summary>
public sealed record LotteryTypeRequest(string? Type, bool? Full = null);

/// <summary>机选请求。</summary>
public sealed record LotteryQuickPickRequest(string? Type, int? Count);

/// <summary>训练请求（window 缺省用 LSTM_WINDOW）。</summary>
public sealed record LotteryTrainRequest(string? Type, int? Window);

/// <summary>预测请求。</summary>
public sealed record LotteryPredictRequest(string? Type, int? Count);

// ───────────────────────────── 开奖 ─────────────────────────────

/// <summary>开奖记录。</summary>
public sealed record LotteryDrawDto(
    string DrawNumber,
    DateTime DrawDate,
    int[] RedNumbers,
    int[] BlueNumbers,
    decimal SalesAmount,
    decimal PoolMoney,
    string? PrizeDescription);

/// <summary>开奖记录分页。</summary>
public sealed record LotteryDrawListResponse(
    IReadOnlyList<LotteryDrawDto> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>开奖记录总数。</summary>
public sealed record LotteryCountResponse(int Count);

// ───────────────────────────── 同步 / 验证 ─────────────────────────────

/// <summary>购彩验证摘要。</summary>
public sealed record LotteryVerificationDto(
    int TotalVerified,
    int HitCount,
    int PendingCount,
    decimal TotalPrizeAmount,
    int BestPrizeLevel,
    string BestPrizeDescription,
    string EncouragementMessage);

/// <summary>同步开奖结果（含自动验证摘要）。</summary>
public sealed record LotterySyncResponse(int NewCount, LotteryVerificationDto Verification);

// ───────────────────────────── 预测 ─────────────────────────────

/// <summary>预测/机选号码。</summary>
public sealed record LotteryPredictionDto(
    int[] RedNumbers,
    int[] BlueNumbers,
    decimal Confidence,
    string Algorithm,
    string AlgorithmName);

/// <summary>机选结果。</summary>
public sealed record LotteryQuickPickResponse(IReadOnlyList<LotteryPredictionDto> Predictions);

// ───────────────────────────── 购彩记录 ─────────────────────────────

/// <summary>购彩记录（含实际开奖号码，用于命中着色）。</summary>
public sealed record LotteryPurchaseDto(
    int Id,
    string LotteryType,
    string DrawNumber,
    int[] RedNumbers,
    int[] BlueNumbers,
    int[] ActualRedNumbers,
    int[] ActualBlueNumbers,
    string Algorithm,
    DateTime PurchaseDate,
    bool IsVerified,
    bool IsHit,
    int HitRedCount,
    int HitBlueCount,
    int PrizeLevel,
    string PrizeLevelText,
    decimal PrizeAmount,
    string StatusText);

/// <summary>购彩记录分页。</summary>
public sealed record LotteryPurchaseListResponse(
    IReadOnlyList<LotteryPurchaseDto> Items,
    int Total,
    int Page,
    int PageSize);

/// <summary>购彩汇总。</summary>
public sealed record LotteryPurchaseSummaryResponse(
    int TotalCount,
    int HitCount,
    int PendingCount,
    decimal TotalPrizeAmount,
    int BestPrizeLevel,
    string BestPrizeDescription);

// ───────────────────────────── Worker 任务 ─────────────────────────────

/// <summary>模型状态。</summary>
public sealed record LotteryModelStatusResponse(
    bool Ready,
    int Window,
    DateTime? TrainedAt);

/// <summary>入队结果。</summary>
public sealed record LotteryJobEnqueueResponse(string JobId, string Status);

/// <summary>
/// 任务详情。predict 成功时附带解析后的 predictions 与本次实际入库数（重复轮询 savedCount=0）。
/// </summary>
public sealed record LotteryJobResponse(
    string JobId,
    string Type,
    string Status,
    int Progress,
    string? Message,
    JsonElement? Result,
    JsonElement? Error,
    IReadOnlyList<LotteryPredictionDto>? Predictions,
    int? SavedCount);
