using System.Text.Json;
using NewCosmos.Helpers;
using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.Lottery;

/// <summary>
/// 用户购彩记录服务实现（替代 legacy UserPurchaseService）。
/// 奖级规则/文案与桌面端保持完全一致，奖金统一由 <see cref="LotteryPrizeResolver"/> 解析。
/// </summary>
public sealed class LotteryPurchaseService : ILotteryPurchaseService
{
    private const int SaveChunkSize = 200;
    private const int VerifyReadLimit = 5000;
    private const int VerifyUpdateChunkSize = 200;
    private const int CandidateDrawLimit = 10000;

    private readonly IDatabaseService _db;
    private readonly ILoggerService _logger;

    public LotteryPurchaseService(IDatabaseService db, ILoggerService logger)
    {
        _db = db;
        _logger = logger;
    }

    // ───────────────────────────── 入库 ─────────────────────────────

    public async Task<Result<int>> SaveFromPredictionsAsync(
        LotteryType lotteryType,
        IReadOnlyList<PredictionResult> predictions,
        string algorithm,
        string? sourceJobId = null,
        CancellationToken ct = default)
    {
        if (predictions is null || predictions.Count == 0)
            return Result.Success(0);

        try
        {
            var valid = new List<(PredictionResult Prediction, string? SourceKey)>();
            for (var index = 0; index < predictions.Count; index++)
            {
                var prediction = predictions[index];
                var reds = prediction.GetPredictedReds();
                var blues = prediction.GetPredictedBlues();

                if (!LotteryNumberGenerator.IsValidTicket(lotteryType, reds, blues))
                {
                    _logger.Warn(
                        $"购彩记录入库：第 {index + 1} 注号码不符合 {lotteryType} 规则，已跳过");
                    continue;
                }

                valid.Add((
                    prediction,
                    string.IsNullOrWhiteSpace(sourceJobId)
                        ? null
                        : DeriveSourceJobKey(sourceJobId, index + 1)));
            }

            if (valid.Count == 0)
                return Result.Success(0);

            var algorithmName = string.IsNullOrWhiteSpace(algorithm) ? "未知算法" : algorithm.Trim();
            if (algorithmName.Length > 50)
                algorithmName = algorithmName[..50];

            var inserted = 0;
            foreach (var chunk in valid.Chunk(SaveChunkSize))
            {
                ct.ThrowIfCancellationRequested();

                var valueRows = new List<string>(chunk.Length);
                var parameters = new List<object?>(chunk.Length * 6);
                var parameterIndex = 1;

                foreach (var (prediction, sourceKey) in chunk)
                {
                    valueRows.Add(
                        $"(${parameterIndex++}, ${parameterIndex++}, ${parameterIndex++}, " +
                        $"${parameterIndex++}, CURRENT_DATE, ${parameterIndex++}::varchar)");

                    parameters.Add(lotteryType.ToString());
                    parameters.Add(JsonSerializer.Serialize(prediction.GetPredictedReds()));
                    parameters.Add(JsonSerializer.Serialize(prediction.GetPredictedBlues()));
                    parameters.Add(algorithmName);
                    parameters.Add((object?)sourceKey ?? DBNull.Value);
                }

                var sql = $@"
                    INSERT INTO nc_lottery_user_purchases
                        (lottery_type, red_numbers, blue_numbers, algorithm, purchase_date, source_job_id)
                    VALUES {string.Join(", ", valueRows)}
                    ON CONFLICT (source_job_id) WHERE source_job_id IS NOT NULL DO NOTHING";

                var result = await _db.ExecuteNonQueryAsync(sql, ct, parameters.ToArray());
                if (result.IsFailure)
                {
                    _logger.Error($"购彩记录入库失败: {result.ErrorCode} {result.Message}");
                    return Result.Failure<int>(result.ErrorCode!, result.Message!);
                }

                inserted += result.Value;
            }

            if (inserted > 0)
            {
                _logger.LogBusiness("彩票购彩入库",
                    ("LotteryType", lotteryType.ToString()),
                    ("Algorithm", algorithmName),
                    ("Count", inserted),
                    ("SourceJobId", sourceJobId ?? "-"));
            }

            return Result.Success(inserted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存购彩记录失败");
            return Result.FromException<int>(ex);
        }
    }

    /// <summary>
    /// 派生幂等键。唯一部分索引只允许一个非空 source_job_id，
    /// 因此一个任务的第一注直接用 jobId，后续注记 <c>jobId#序号</c>；
    /// 重复轮询得到相同键 → DO NOTHING。
    /// </summary>
    private static string DeriveSourceJobKey(string sourceJobId, int index)
    {
        var key = sourceJobId.Trim();
        if (index <= 1)
            return key.Length > 64 ? key[..64] : key;

        if (key.Length > 58)
            key = key[..58];
        return $"{key}#{index}";
    }

    // ───────────────────────────── 验证派奖 ─────────────────────────────

    public async Task<Result<VerificationSummary>> VerifyAndRewardAsync(
        LotteryType lotteryType,
        CancellationToken ct = default)
    {
        try
        {
            var pendingResult = await _db.QueryAsync<PurchaseRow>(@"
                SELECT id, lottery_type, draw_number, red_numbers, blue_numbers, algorithm,
                       purchase_date, is_verified, is_hit, hit_red_count, hit_blue_count,
                       prize_level, prize_amount, created_at
                FROM nc_lottery_user_purchases
                WHERE lottery_type = $1 AND is_verified = FALSE
                ORDER BY purchase_date ASC
                LIMIT $2",
                ct, lotteryType.ToString(), VerifyReadLimit);

            if (pendingResult.IsFailure)
                return Result.Failure<VerificationSummary>(
                    pendingResult.ErrorCode!, pendingResult.Message!);

            var pendingRows = pendingResult.Value;
            var summary = new VerificationSummary();

            if (pendingRows.Count == 0)
            {
                summary.EncouragementMessage = "暂无待验证记录，生成预测后开奖即可自动验证";
                return Result.Success(summary);
            }

            // 购彩日期当天及之后的开奖记录一次性载入，内存按期匹配，避免逐注查库。
            var candidateResult = await _db.QueryAsync<CandidateDraw>(@"
                SELECT draw_number, draw_date, red_numbers_json, blue_numbers_json, prize_grades_json
                FROM nc_lottery_draws
                WHERE lottery_type = $1 AND draw_date >= $2
                ORDER BY draw_date ASC
                LIMIT $3",
                ct,
                lotteryType.ToString(),
                pendingRows.Min(row => row.PurchaseDate),
                CandidateDrawLimit);

            if (candidateResult.IsFailure)
                return Result.Failure<VerificationSummary>(
                    candidateResult.ErrorCode!, candidateResult.Message!);

            var candidates = candidateResult.Value;
            var updates = new List<VerificationUpdate>();

            foreach (var row in pendingRows)
            {
                var draw = ResolveDraw(row, candidates);
                if (draw is null)
                    continue;

                var predictedReds = DeserializeNumbers(row.RedNumbers);
                var predictedBlues = DeserializeNumbers(row.BlueNumbers);
                var actualReds = DeserializeNumbers(draw.RedNumbersJson);
                var actualBlues = DeserializeNumbers(draw.BlueNumbersJson);

                var redHits = predictedReds.Intersect(actualReds).Count();
                var blueHits = predictedBlues.Intersect(actualBlues).Count();
                var prizeLevel = LotteryPrizeRules.DeterminePrizeLevel(
                    lotteryType, redHits, blueHits, draw.DrawNumber, draw.DrawDate);
                var isHit = prizeLevel > 0;
                var prizeAmount = isHit
                    ? LotteryPrizeResolver.ResolvePrizeAmount(draw.PrizeGradesJson, prizeLevel)
                    : 0m;

                updates.Add(new VerificationUpdate
                {
                    Id = row.Id,
                    DrawNumber = draw.DrawNumber,
                    IsHit = isHit,
                    HitRedCount = redHits,
                    HitBlueCount = blueHits,
                    PrizeLevel = prizeLevel,
                    PrizeAmount = prizeAmount
                });

                summary.TotalVerified++;

                if (!isHit)
                    continue;

                summary.HitCount++;
                summary.TotalPrizeAmount += prizeAmount;
                summary.HitDetails.Add(new HitDetail
                {
                    DrawNumber = draw.DrawNumber,
                    RedHits = redHits,
                    BlueHits = blueHits,
                    PrizeLevel = prizeLevel,
                    PrizeAmount = prizeAmount,
                    Numbers = $"{FormatNumbers(predictedReds)} + {FormatNumbers(predictedBlues)}"
                });

                if (summary.BestPrizeLevel == 0 || prizeLevel < summary.BestPrizeLevel)
                {
                    summary.BestPrizeLevel = prizeLevel;
                    summary.BestPrizeDescription =
                        LotteryPrizeRules.GetPrizeDescription(lotteryType, prizeLevel);
                }
            }

            summary.PendingCount = pendingRows.Count - summary.TotalVerified;

            if (updates.Count > 0)
            {
                await using var tx = await _db.BeginTransactionScopeAsync(ct);

                foreach (var chunk in updates.Chunk(VerifyUpdateChunkSize))
                {
                    var updateResult = await ExecuteBatchUpdateAsync(chunk, ct);
                    if (updateResult.IsFailure)
                    {
                        // 未 Commit：作用域 Dispose 自动回滚。
                        _logger.Error($"购彩验证批量更新失败: {updateResult.ErrorCode} {updateResult.Message}");
                        return Result.Failure<VerificationSummary>(
                            updateResult.ErrorCode!, updateResult.Message!);
                    }
                }

                await tx.CommitAsync(ct);
            }

            summary.EncouragementMessage = GenerateEncouragement(summary);

            _logger.LogBusiness("彩票购彩验证",
                ("LotteryType", lotteryType.ToString()),
                ("Verified", summary.TotalVerified),
                ("Hit", summary.HitCount),
                ("Pending", summary.PendingCount),
                ("PrizeAmount", summary.TotalPrizeAmount));

            return Result.Success(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证购彩记录失败");
            return Result.FromException<VerificationSummary>(ex);
        }
    }

    private async Task<Result<int>> ExecuteBatchUpdateAsync(
        VerificationUpdate[] updates,
        CancellationToken ct)
    {
        var valueRows = new List<string>(updates.Length);
        var parameters = new List<object?>(updates.Length * 7);
        var parameterIndex = 1;

        foreach (var update in updates)
        {
            valueRows.Add(
                $"(${parameterIndex++}::int, ${parameterIndex++}::varchar, ${parameterIndex++}::bool, " +
                $"${parameterIndex++}::int, ${parameterIndex++}::int, ${parameterIndex++}::int, " +
                $"${parameterIndex++}::numeric)");

            parameters.Add(update.Id);
            parameters.Add(update.DrawNumber);
            parameters.Add(update.IsHit);
            parameters.Add(update.HitRedCount);
            parameters.Add(update.HitBlueCount);
            parameters.Add(update.PrizeLevel);
            parameters.Add(update.PrizeAmount);
        }

        var sql = $@"
            UPDATE nc_lottery_user_purchases AS t
            SET draw_number = v.draw_number,
                is_verified = TRUE,
                is_hit = v.is_hit,
                hit_red_count = v.hit_red_count,
                hit_blue_count = v.hit_blue_count,
                prize_level = v.prize_level,
                prize_amount = v.prize_amount
            FROM (VALUES {string.Join(", ", valueRows)})
                AS v(id, draw_number, is_hit, hit_red_count, hit_blue_count, prize_level, prize_amount)
            WHERE t.id = v.id";

        return await _db.ExecuteNonQueryAsync(sql, ct, parameters.ToArray());
    }

    private static CandidateDraw? ResolveDraw(PurchaseRow row, List<CandidateDraw> candidates)
    {
        if (!string.IsNullOrWhiteSpace(row.DrawNumber))
        {
            return candidates.FirstOrDefault(candidate =>
                string.Equals(candidate.DrawNumber, row.DrawNumber, StringComparison.OrdinalIgnoreCase));
        }

        // candidates 已按 draw_date 升序，取第一个不早于购彩日期的期号。
        return candidates.FirstOrDefault(candidate => candidate.DrawDate >= row.PurchaseDate);
    }

    // ───────────────────────────── 查询 ─────────────────────────────

    public async Task<Result<List<UserPurchaseRecord>>> GetPurchasesAsync(
        LotteryType lotteryType,
        LotteryPurchaseFilter filter = LotteryPurchaseFilter.All,
        int page = 1,
        int pageSize = 30,
        CancellationToken ct = default)
    {
        try
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var offset = (page - 1) * pageSize;
            var filterClause = BuildFilterClause(filter);

            var sql = $@"
                SELECT p.id, p.lottery_type, p.draw_number, p.red_numbers, p.blue_numbers,
                       p.algorithm, p.purchase_date, p.is_verified, p.is_hit,
                       p.hit_red_count, p.hit_blue_count, p.prize_level, p.prize_amount,
                       p.created_at,
                       d.red_numbers_json AS actual_red_numbers,
                       d.blue_numbers_json AS actual_blue_numbers
                FROM nc_lottery_user_purchases p
                LEFT JOIN nc_lottery_draws d
                       ON d.lottery_type = p.lottery_type AND d.draw_number = p.draw_number
                WHERE p.lottery_type = $1{filterClause}
                ORDER BY p.created_at DESC, p.id DESC
                LIMIT $2 OFFSET $3";

            var result = await _db.QueryAsync<PurchaseRow>(
                sql, ct, lotteryType.ToString(), pageSize, offset);

            if (result.IsFailure)
                return Result.Failure<List<UserPurchaseRecord>>(result.ErrorCode!, result.Message!);

            return Result.Success(result.Value.Select(row => ToRecord(row, lotteryType)).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取购彩记录失败");
            return Result.FromException<List<UserPurchaseRecord>>(ex);
        }
    }

    public async Task<Result<int>> GetPurchaseCountAsync(
        LotteryType lotteryType,
        LotteryPurchaseFilter filter = LotteryPurchaseFilter.All,
        CancellationToken ct = default)
    {
        try
        {
            var filterClause = BuildFilterClause(filter);
            var sql = $"SELECT COUNT(*) FROM nc_lottery_user_purchases WHERE lottery_type = $1{filterClause}";

            var result = await _db.ExecuteScalarAsync<int?>(sql, ct, lotteryType.ToString());
            return result.IsFailure
                ? Result.Failure<int>(result.ErrorCode!, result.Message!)
                : Result.Success(result.Value ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取购彩记录总数失败");
            return Result.FromException<int>(ex);
        }
    }

    public async Task<Result<LotteryPurchaseSummary>> GetPurchaseSummaryAsync(
        LotteryType lotteryType,
        CancellationToken ct = default)
    {
        try
        {
            const string sql = @"
                SELECT COUNT(*) AS total_count,
                       COUNT(*) FILTER (WHERE is_verified) AS verified_count,
                       COUNT(*) FILTER (WHERE is_hit) AS hit_count,
                       COALESCE(SUM(CASE WHEN is_hit THEN prize_amount ELSE 0 END), 0) AS total_prize,
                       MIN(CASE WHEN is_hit THEN prize_level END) AS best_prize_level
                FROM nc_lottery_user_purchases
                WHERE lottery_type = $1";

            var result = await _db.QuerySingleAsync<PurchaseSummaryRow>(sql, ct, lotteryType.ToString());
            if (result.IsFailure)
                return Result.Failure<LotteryPurchaseSummary>(result.ErrorCode!, result.Message!);

            var row = result.Value;
            if (row is null)
                return Result.Success(new LotteryPurchaseSummary());

            var summary = new LotteryPurchaseSummary
            {
                TotalCount = row.TotalCount,
                VerifiedCount = row.VerifiedCount,
                PendingCount = row.TotalCount - row.VerifiedCount,
                HitCount = row.HitCount,
                TotalPrizeAmount = row.TotalPrize,
                BestPrizeLevel = row.BestPrizeLevel ?? 0
            };
            summary.BestPrizeDescription = summary.BestPrizeLevel > 0
                ? LotteryPrizeRules.GetPrizeDescription(lotteryType, summary.BestPrizeLevel)
                : string.Empty;

            return Result.Success(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取购彩汇总失败");
            return Result.FromException<LotteryPurchaseSummary>(ex);
        }
    }

    // ───────────────────────────── 辅助 ─────────────────────────────

    private static string BuildFilterClause(LotteryPurchaseFilter filter) => filter switch
    {
        LotteryPurchaseFilter.Pending => " AND is_verified = FALSE",
        LotteryPurchaseFilter.Hit => " AND is_verified = TRUE AND is_hit = TRUE",
        LotteryPurchaseFilter.Miss => " AND is_verified = TRUE AND is_hit = FALSE",
        _ => string.Empty
    };

    private static UserPurchaseRecord ToRecord(PurchaseRow row, LotteryType lotteryType) => new()
    {
        Id = row.Id,
        LotteryType = lotteryType,
        DrawNumber = row.DrawNumber ?? string.Empty,
        RedNumbersJson = row.RedNumbers,
        BlueNumbersJson = row.BlueNumbers,
        ActualRedNumbersJson = row.ActualRedNumbers ?? "[]",
        ActualBlueNumbersJson = row.ActualBlueNumbers ?? "[]",
        Algorithm = row.Algorithm ?? string.Empty,
        PurchaseDate = row.PurchaseDate,
        IsVerified = row.IsVerified,
        IsHit = row.IsHit,
        HitRedCount = row.HitRedCount,
        HitBlueCount = row.HitBlueCount,
        PrizeLevel = row.PrizeLevel,
        PrizeAmount = row.PrizeAmount,
        CreatedAt = row.CreatedAt
    };

    private static int[] DeserializeNumbers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<int>();

        try
        {
            return JsonSerializer.Deserialize<int[]>(json) ?? Array.Empty<int>();
        }
        catch (JsonException ex)
        {
            Serilog.Log.Warning(ex, "[LotteryPurchase] 号码 JSON 解析失败，按空号码处理");
            return Array.Empty<int>();
        }
    }

    private static string FormatNumbers(int[] numbers) =>
        string.Join(",", numbers.Select(n => n.ToString("D2")));

    private static string GenerateEncouragement(VerificationSummary summary)
    {
        if (summary.TotalVerified == 0 && summary.PendingCount == 0)
            return "暂无待验证记录，生成预测后开奖即可自动验证";

        if (summary.TotalVerified == 0)
            return $"暂无可验证记录，有 {summary.PendingCount} 注待开奖，开奖后同步数据即可验证";

        var prizeText = summary.TotalPrizeAmount > 0
            ? $"，合计奖金 ¥{summary.TotalPrizeAmount:N2}"
            : string.Empty;

        if (summary.HitCount == 0)
            return $"本次验证 {summary.TotalVerified} 注，暂未命中。\n继续加油！每一注都是对未来的投资！🎯";

        if (summary.BestPrizeLevel == 1)
            return $"🎉🎉🎉 恭喜！在 {summary.TotalVerified} 注中命中 {summary.HitCount} 注{prizeText}！\n含一等奖 {summary.BestPrizeDescription}！！！";

        if (summary.BestPrizeLevel <= 3)
            return $"🎉 恭喜！在 {summary.TotalVerified} 注中命中 {summary.HitCount} 注{prizeText}！\n最佳成绩：{summary.BestPrizeDescription}！继续保持！";

        return $"在 {summary.TotalVerified} 注中命中 {summary.HitCount} 注{prizeText}！\n最佳成绩：{summary.BestPrizeDescription}，距离大奖越来越近了！💪";
    }

    // ───────────────────────────── 数据行 DTO ─────────────────────────────

    private sealed class PurchaseRow
    {
        public int Id { get; set; }
        public string LotteryType { get; set; } = string.Empty;
        public string? DrawNumber { get; set; }
        public string RedNumbers { get; set; } = "[]";
        public string BlueNumbers { get; set; } = "[]";
        public string Algorithm { get; set; } = string.Empty;
        public DateTime PurchaseDate { get; set; }
        public bool IsVerified { get; set; }
        public bool IsHit { get; set; }
        public int HitRedCount { get; set; }
        public int HitBlueCount { get; set; }
        public int PrizeLevel { get; set; }
        public decimal PrizeAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? ActualRedNumbers { get; set; }
        public string? ActualBlueNumbers { get; set; }
    }

    private sealed class CandidateDraw
    {
        public string DrawNumber { get; set; } = string.Empty;
        public DateTime DrawDate { get; set; }
        public string RedNumbersJson { get; set; } = "[]";
        public string BlueNumbersJson { get; set; } = "[]";
        public string PrizeGradesJson { get; set; } = "[]";
    }

    private sealed class PurchaseSummaryRow
    {
        public int TotalCount { get; set; }
        public int VerifiedCount { get; set; }
        public int HitCount { get; set; }
        public decimal TotalPrize { get; set; }
        public int? BestPrizeLevel { get; set; }
    }

    private sealed class VerificationUpdate
    {
        public int Id { get; set; }
        public string DrawNumber { get; set; } = string.Empty;
        public bool IsHit { get; set; }
        public int HitRedCount { get; set; }
        public int HitBlueCount { get; set; }
        public int PrizeLevel { get; set; }
        public decimal PrizeAmount { get; set; }
    }
}
