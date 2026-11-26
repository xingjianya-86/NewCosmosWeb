using System.Security.Cryptography;
using NewCosmos.Models.Lottery;

namespace NewCosmos.Services.Domain.Lottery;

/// <summary>
/// 彩票机选：按彩种红/蓝数量与上限生成不重复、升序号码。
/// 使用密码学随机源，避免 <see cref="Random"/> 在并发下产生相关序列。
/// </summary>
public static class LotteryNumberGenerator
{
    /// <summary>机选算法标识（入库 algorithm 字段用展示名，这里保留版本标识）。</summary>
    public const string RandomAlgorithmVersion = "random";

    /// <summary>机选算法展示名。</summary>
    public const string RandomAlgorithmName = "随机机选";

    /// <summary>生成指定数量的机选号码（每注红/蓝均不重复且升序）。</summary>
    public static List<PredictionResult> Generate(LotteryType lotteryType, int count)
    {
        if (count <= 0)
            return new List<PredictionResult>();

        var results = new List<PredictionResult>(count);
        for (var i = 0; i < count; i++)
        {
            var prediction = new PredictionResult
            {
                LotteryType = lotteryType,
                AlgorithmVersion = RandomAlgorithmVersion,
                AlgorithmName = RandomAlgorithmName,
                ConfidenceScore = 0m
            };

            prediction.SetPredictedReds(PickDistinct(lotteryType.GetRedMax(), lotteryType.GetRedCount()));
            prediction.SetPredictedBlues(PickDistinct(lotteryType.GetBlueMax(), lotteryType.GetBlueCount()));
            results.Add(prediction);
        }

        return results;
    }

    /// <summary>
    /// 从 1..max 中无放回抽取 take 个号码并升序返回（Fisher-Yates + 密码学随机）。
    /// </summary>
    public static int[] PickDistinct(int max, int take)
    {
        if (max <= 0 || take <= 0)
            return Array.Empty<int>();
        if (take > max)
            take = max;

        var pool = Enumerable.Range(1, max).ToArray();
        for (var i = 0; i < take; i++)
        {
            var j = RandomNumberGenerator.GetInt32(i, max);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        var picked = pool[..take];
        Array.Sort(picked);
        return picked;
    }

    /// <summary>校验一注号码是否符合彩种数量/范围/不重复要求。</summary>
    public static bool IsValidTicket(LotteryType lotteryType, IReadOnlyCollection<int> reds, IReadOnlyCollection<int> blues)
    {
        return IsValidSet(reds, lotteryType.GetRedCount(), lotteryType.GetRedMax())
            && IsValidSet(blues, lotteryType.GetBlueCount(), lotteryType.GetBlueMax());
    }

    private static bool IsValidSet(IReadOnlyCollection<int> numbers, int expectedCount, int max)
    {
        if (numbers.Count != expectedCount)
            return false;

        var set = new HashSet<int>();
        foreach (var number in numbers)
        {
            if (number < 1 || number > max || !set.Add(number))
                return false;
        }

        return true;
    }
}
