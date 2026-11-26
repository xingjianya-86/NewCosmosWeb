using System.Globalization;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.Lottery;

/// <summary>
/// 彩票开奖数据服务实现（替代 legacy Python 抓取脚本，Server 直连官方接口）。
/// <para>
/// 双色球：<c>https://www.cwl.gov.cn/cwl_admin/front/cwlkj/search/kjxx/findDrawNotice</c>；
/// 大乐透：<c>https://webapi.sporttery.cn/gateway/lottery/getHistoryPageListV1.qry</c>。
/// 分页 100，按开奖日期倒序；增量模式遇到更早日期即停止。upsert 保留已有的非空奖级明细。
/// </para>
/// </summary>
public sealed class LotteryDataService : ILotteryDataService
{
    private const string SsqApiUrl =
        "https://www.cwl.gov.cn/cwl_admin/front/cwlkj/search/kjxx/findDrawNotice";

    private const string DltApiUrl =
        "https://webapi.sporttery.cn/gateway/lottery/getHistoryPageListV1.qry";

    private const string SsqReferer = "https://www.cwl.gov.cn/ygkj/wqkjgg/ssq/";
    private const string DltReferer = "https://www.lottery.gov.cn/";
    private const int PageSize = 100;
    private const int MaxPages = 1000;
    private const int UpsertChunkSize = 200;

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private static readonly HttpClient Http = CreateHttpClient();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IDatabaseService _db;
    private readonly ILoggerService _logger;

    public LotteryDataService(IDatabaseService db, ILoggerService logger)
    {
        _db = db;
        _logger = logger;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = global::System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        })
        {
            Timeout = TimeSpan.FromSeconds(90)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/javascript, */*; q=0.01");
        return client;
    }

    // ───────────────────────────── 同步 ─────────────────────────────

    public async Task<Result<int>> SyncHistoryAsync(
        LotteryType lotteryType,
        DateTime? sinceDate = null,
        CancellationToken ct = default)
    {
        try
        {
            var scope = sinceDate.HasValue
                ? $"增量（开奖日期 >= {sinceDate.Value:yyyy-MM-dd}）"
                : "全量";
            _logger.Info($"开始{scope}同步 {lotteryType.GetDisplayName()} 开奖数据");

            var collected = new List<LotteryDraw>();
            var total = -1;

            for (var page = 1; page <= MaxPages; page++)
            {
                ct.ThrowIfCancellationRequested();

                var (draws, rawCount, pageTotal) = lotteryType == LotteryType.SSQ
                    ? await FetchSsqPageAsync(page, PageSize, ct)
                    : await FetchDltPageAsync(page, PageSize, ct);

                if (pageTotal >= 0 && total < 0)
                    total = pageTotal;

                if (rawCount == 0)
                    break;

                var reachedOld = false;
                foreach (var draw in draws)
                {
                    if (sinceDate.HasValue && draw.DrawDate.Date < sinceDate.Value.Date)
                    {
                        reachedOld = true;
                        break;
                    }

                    collected.Add(draw);
                }

                if (reachedOld)
                    break;

                if (rawCount < PageSize)
                    break;

                if (total >= 0 && page * PageSize >= total)
                    break;

                // 官方接口限频：翻页间留出间隔。
                await Task.Delay(150, ct);
            }

            if (collected.Count == 0)
            {
                _logger.Info($"同步 {lotteryType.GetDisplayName()} 完成：无新增/更新记录");
                return Result.Success(0);
            }

            var existingResult = await _db.QueryAsync<string>(
                "SELECT draw_number FROM nc_lottery_draws WHERE lottery_type = $1 AND draw_number = ANY($2)",
                ct,
                lotteryType.ToString(),
                collected.Select(d => d.DrawNumber).Distinct(StringComparer.Ordinal).ToArray());

            if (existingResult.IsFailure)
            {
                _logger.Error($"同步开奖数据：查询已存在期号失败: {existingResult.ErrorCode} {existingResult.Message}");
                return Result.Failure<int>(existingResult.ErrorCode!, existingResult.Message!);
            }

            var existing = new HashSet<string>(existingResult.Value, StringComparer.Ordinal);
            var newCount = collected
                .Select(d => d.DrawNumber)
                .Distinct(StringComparer.Ordinal)
                .Count(number => !existing.Contains(number));

            var writeResult = await BatchUpsertDrawsAsync(lotteryType, collected, ct);
            if (writeResult.IsFailure)
                return Result.Failure<int>(writeResult.ErrorCode!, writeResult.Message!);

            _logger.LogBusiness("彩票开奖同步",
                ("LotteryType", lotteryType.ToString()),
                ("SinceDate", sinceDate?.ToString("yyyy-MM-dd") ?? "全量"),
                ("Fetched", collected.Count),
                ("NewCount", newCount),
                ("Affected", writeResult.Value));

            return Result.Success(newCount);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.Error($"同步 {lotteryType.GetDisplayName()} 开奖数据超时");
            return Result.Failure<int>(ErrorCodes.LOTTERY_SYNC_FAILED, "官方接口请求超时，请稍后重试");
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<int>(ErrorCodes.CANCELLED, "操作已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"同步 {lotteryType.GetDisplayName()} 开奖数据失败");
            return Result.Failure<int>(
                ErrorCodes.LOTTERY_SYNC_FAILED,
                $"开奖数据同步失败: {ex.Message}");
        }
    }

    private async Task<(List<LotteryDraw> Draws, int RawCount, int Total)> FetchSsqPageAsync(
        int page, int pageSize, CancellationToken ct)
    {
        var url = $"{SsqApiUrl}?name=ssq&issueCount=&issueStart=&issueEnd=&dayStart=&dayEnd=" +
                  $"&pageNo={page}&pageSize={pageSize}&week=&systemType=PC";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri(SsqReferer);

        var body = await SendForJsonAsync(request, ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("双色球接口返回格式无效");

        if (TryGetInt(root, "state", out var state) && state != 0)
        {
            var message = GetString(root, "message") ?? "官方接口返回错误";
            throw new InvalidOperationException($"双色球接口错误: {message}");
        }

        var total = TryGetInt(root, "total", out var totalValue) ? totalValue : -1;
        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            return (new List<LotteryDraw>(), 0, total);

        var draws = new List<LotteryDraw>();
        var rawCount = 0;
        foreach (var item in result.EnumerateArray())
        {
            rawCount++;
            var draw = ParseSsqDraw(item);
            if (draw is not null)
                draws.Add(draw);
        }

        return (draws, rawCount, total);
    }

    private async Task<(List<LotteryDraw> Draws, int RawCount, int Total)> FetchDltPageAsync(
        int page, int pageSize, CancellationToken ct)
    {
        var url = $"{DltApiUrl}?gameNo=85&provinceId=0&pageSize={pageSize}&isVerify=1&pageNo={page}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri(DltReferer);

        var body = await SendForJsonAsync(request, ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("大乐透接口返回格式无效");

        if (root.TryGetProperty("success", out var success)
            && success.ValueKind == JsonValueKind.False)
        {
            var message = GetString(root, "message") ?? GetString(root, "errorMessage") ?? "官方接口返回错误";
            throw new InvalidOperationException($"大乐透接口错误: {message}");
        }

        if (!root.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object)
            return (new List<LotteryDraw>(), 0, -1);

        var total = TryGetInt(value, "total", out var totalValue) ? totalValue : -1;
        if (!value.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array)
            return (new List<LotteryDraw>(), 0, total);

        var draws = new List<LotteryDraw>();
        var rawCount = 0;
        foreach (var item in list.EnumerateArray())
        {
            rawCount++;
            var draw = ParseDltDraw(item);
            if (draw is not null)
                draws.Add(draw);
        }

        return (draws, rawCount, total);
    }

    private static async Task<string> SendForJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);

        using var response = await Http.SendAsync(request, cts.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"官方接口 HTTP {(int)response.StatusCode}");
        }

        return await response.Content.ReadAsStringAsync(cts.Token);
    }

    private async Task<Result<int>> BatchUpsertDrawsAsync(
        LotteryType lotteryType,
        List<LotteryDraw> draws,
        CancellationToken ct)
    {
        var affected = 0;

        foreach (var chunk in draws.Chunk(UpsertChunkSize))
        {
            ct.ThrowIfCancellationRequested();

            var valueRows = new List<string>(chunk.Length);
            var parameters = new List<object?>(chunk.Length * 9);
            var parameterIndex = 1;

            foreach (var draw in chunk)
            {
                valueRows.Add(
                    $"(${parameterIndex++}, ${parameterIndex++}, ${parameterIndex++}, " +
                    $"${parameterIndex++}, ${parameterIndex++}, ${parameterIndex++}, " +
                    $"${parameterIndex++}, ${parameterIndex++}, ${parameterIndex++})");

                parameters.Add(lotteryType.ToString());
                parameters.Add(draw.DrawNumber);
                parameters.Add(draw.DrawDate.Date);
                parameters.Add(draw.RedNumbersJson);
                parameters.Add(draw.BlueNumbersJson);
                parameters.Add(draw.SalesAmount);
                parameters.Add(draw.PoolMoney);
                parameters.Add(draw.PrizeGradesJson);
                parameters.Add(draw.PrizeDescription ?? string.Empty);
            }

            var sql = $@"
                INSERT INTO nc_lottery_draws
                    (lottery_type, draw_number, draw_date, red_numbers_json, blue_numbers_json,
                     sales_amount, pool_money, prize_grades_json, prize_description)
                VALUES {string.Join(", ", valueRows)}
                ON CONFLICT (lottery_type, draw_number) DO UPDATE
                    SET draw_date = EXCLUDED.draw_date,
                        red_numbers_json = EXCLUDED.red_numbers_json,
                        blue_numbers_json = EXCLUDED.blue_numbers_json,
                        sales_amount = EXCLUDED.sales_amount,
                        pool_money = EXCLUDED.pool_money,
                        prize_grades_json = CASE
                            WHEN EXCLUDED.prize_grades_json IS NOT NULL
                                 AND EXCLUDED.prize_grades_json <> '[]'
                                THEN EXCLUDED.prize_grades_json
                            ELSE nc_lottery_draws.prize_grades_json
                        END,
                        prize_description = CASE
                            WHEN EXCLUDED.prize_description IS NOT NULL
                                 AND EXCLUDED.prize_description <> ''
                                THEN EXCLUDED.prize_description
                            ELSE nc_lottery_draws.prize_description
                        END";

            var result = await _db.ExecuteNonQueryAsync(sql, ct, parameters.ToArray());
            if (result.IsFailure)
            {
                _logger.Error($"开奖数据入库失败: {result.ErrorCode} {result.Message}");
                return Result.Failure<int>(result.ErrorCode!, result.Message!);
            }

            affected += result.Value;
        }

        return Result.Success(affected);
    }

    // ───────────────────────────── 解析 ─────────────────────────────

    private static LotteryDraw? ParseSsqDraw(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;

        var drawNumber = GetString(item, "code")?.Trim();
        if (string.IsNullOrWhiteSpace(drawNumber))
            return null;

        var drawDate = ParseDrawDate(GetString(item, "date"));
        if (drawDate is null)
            return null;

        var reds = ParseNumberList(GetString(item, "red"));
        var blues = ParseNumberList(GetString(item, "blue"));
        if (reds.Length == 0 && blues.Length == 0)
            return null;

        Array.Sort(reds);
        Array.Sort(blues);

        var prizeGradesJson = "[]";
        if (item.TryGetProperty("prizegrades", out var grades)
            && grades.ValueKind == JsonValueKind.Array
            && grades.GetArrayLength() > 0)
        {
            prizeGradesJson = grades.GetRawText();
        }

        var draw = new LotteryDraw
        {
            LotteryType = LotteryType.SSQ,
            DrawNumber = drawNumber,
            DrawDate = drawDate.Value,
            SalesAmount = ParseAmount(item, "sales"),
            PoolMoney = ParseAmount(item, "poolmoney"),
            PrizeGradesJson = prizeGradesJson,
            PrizeDescription = GetString(item, "content") ?? string.Empty
        };
        draw.SetRedNumbers(reds);
        draw.SetBlueNumbers(blues);
        return draw;
    }

    private static LotteryDraw? ParseDltDraw(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;

        var drawNumber = GetString(item, "lotteryDrawNum")?.Trim();
        if (string.IsNullOrWhiteSpace(drawNumber))
            return null;

        var drawDate = ParseDrawDate(GetString(item, "lotteryDrawTime"));
        if (drawDate is null)
            return null;

        var (front, back) = ParseDltNumbers(GetString(item, "lotteryDrawResult"));
        if (front.Length == 0 && back.Length == 0)
            return null;

        var prizeGradesJson = "[]";
        if (item.TryGetProperty("prizeLevelList", out var levels)
            && levels.ValueKind == JsonValueKind.Array
            && levels.GetArrayLength() > 0)
        {
            prizeGradesJson = NormalizeDltPrizeGrades(levels);
        }

        var draw = new LotteryDraw
        {
            LotteryType = LotteryType.DLT,
            DrawNumber = drawNumber,
            DrawDate = drawDate.Value,
            SalesAmount = ParseAmountFirst(item, "totalSaleAmount", "lotterySaleAmount"),
            PoolMoney = ParseAmountFirst(
                item, "poolBalanceAfterdraw", "lotteryPoolBalanceAfterDraw", "poolBalance"),
            PrizeGradesJson = prizeGradesJson,
            PrizeDescription = string.Empty
        };
        draw.SetRedNumbers(front);
        draw.SetBlueNumbers(back);
        return draw;
    }

    /// <summary>
    /// 大乐透 prizeLevelList 归一化为 [{type,typenum,typemoney}]（与双色球 prizegrades 一致）。
    /// 跳过“追加”条目；无人中奖（-1）记 0。
    /// </summary>
    private static string NormalizeDltPrizeGrades(JsonElement levels)
    {
        var grades = new List<Dictionary<string, object?>>();

        foreach (var item in levels.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var name = GetString(item, "prizeLevel")?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name) || name.Contains("追加", StringComparison.Ordinal))
                continue;

            var level = name switch
            {
                "一等奖" => 1,
                "二等奖" => 2,
                "三等奖" => 3,
                "四等奖" => 4,
                "五等奖" => 5,
                "六等奖" => 6,
                "七等奖" => 7,
                "八等奖" => 8,
                "九等奖" => 9,
                _ => 0
            };
            if (level == 0)
                continue;

            var amount = ParseAmount(item, "stakeAmountFormat");
            if (amount < 0)
                amount = 0;

            grades.Add(new Dictionary<string, object?>
            {
                ["type"] = level,
                ["typenum"] = GetString(item, "stakeCount") ?? string.Empty,
                ["typemoney"] = decimal.Truncate(amount).ToString("0", CultureInfo.InvariantCulture)
            });
        }

        grades.Sort((a, b) => Convert.ToInt32(a["type"]).CompareTo(Convert.ToInt32(b["type"])));
        return JsonSerializer.Serialize(grades, JsonOptions);
    }

    private static (int[] Front, int[] Back) ParseDltNumbers(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (Array.Empty<int>(), Array.Empty<int>());

        var text = raw.Trim();
        if (text.Contains('+'))
        {
            var parts = text.Split('+', 2);
            var front = ParseNumberList(parts[0]);
            var back = ParseNumberList(parts[1]);
            Array.Sort(front);
            Array.Sort(back);
            return (front, back);
        }

        var all = ParseNumberList(text);
        if (all.Length < 7)
        {
            var front = all.Take(5).ToArray();
            var back = all.Skip(5).Take(2).ToArray();
            Array.Sort(front);
            Array.Sort(back);
            return (front, back);
        }

        var frontNumbers = all.Take(5).ToArray();
        var backNumbers = all.Skip(5).Take(2).ToArray();
        Array.Sort(frontNumbers);
        Array.Sort(backNumbers);
        return (frontNumbers, backNumbers);
    }

    private static DateTime? ParseDrawDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = raw.Trim();
        var cut = text.IndexOfAny(['(', '（', ' ']);
        if (cut > 0)
            text = text[..cut];

        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static int[] ParseNumberList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<int>();

        var numbers = new List<int>();
        foreach (var token in raw.Split([',', '，', ' ', '\t', '\n', '\r', '\u3000', '+', '|', '、'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                numbers.Add(value);
        }

        return numbers.ToArray();
    }

    // ───────────────────────────── 查询 ─────────────────────────────

    public async Task<Result<List<LotteryDraw>>> GetDrawListAsync(
        LotteryType lotteryType,
        int page = 1,
        int pageSize = 30,
        CancellationToken ct = default)
    {
        try
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            const string sql = @"
                SELECT id, lottery_type, draw_number, draw_date,
                       red_numbers_json, blue_numbers_json,
                       sales_amount, pool_money, prize_grades_json,
                       prize_description, created_at
                FROM nc_lottery_draws
                WHERE lottery_type = $1
                ORDER BY draw_date DESC, draw_number DESC
                LIMIT $2 OFFSET $3";

            var offset = (page - 1) * pageSize;
            var result = await _db.QueryAsync<LotteryDrawRow>(
                sql, ct, lotteryType.ToString(), pageSize, offset);

            if (result.IsFailure)
                return Result.Failure<List<LotteryDraw>>(result.ErrorCode!, result.Message!);

            return Result.Success(result.Value.Select(row => row.ToModel()).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取开奖记录列表失败");
            return Result.FromException<List<LotteryDraw>>(ex);
        }
    }

    public async Task<Result<int>> GetDrawCountAsync(LotteryType lotteryType, CancellationToken ct = default)
    {
        try
        {
            var result = await _db.ExecuteScalarAsync<int?>(
                "SELECT COUNT(*) FROM nc_lottery_draws WHERE lottery_type = $1",
                ct, lotteryType.ToString());

            return result.IsFailure
                ? Result.Failure<int>(result.ErrorCode!, result.Message!)
                : Result.Success(result.Value ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取开奖记录总数失败");
            return Result.FromException<int>(ex);
        }
    }

    public async Task<Result<LotteryDraw?>> GetLatestDrawAsync(
        LotteryType lotteryType,
        CancellationToken ct = default)
    {
        try
        {
            const string sql = @"
                SELECT id, lottery_type, draw_number, draw_date,
                       red_numbers_json, blue_numbers_json,
                       sales_amount, pool_money, prize_grades_json,
                       prize_description, created_at
                FROM nc_lottery_draws
                WHERE lottery_type = $1
                ORDER BY draw_date DESC, draw_number DESC
                LIMIT 1";

            var result = await _db.QuerySingleAsync<LotteryDrawRow>(sql, ct, lotteryType.ToString());
            if (result.IsFailure)
                return Result.Failure<LotteryDraw?>(result.ErrorCode!, result.Message!);

            return Result.Success<LotteryDraw?>(result.Value?.ToModel());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取最新开奖记录失败");
            return Result.FromException<LotteryDraw?>(ex);
        }
    }

    public async Task<Result<LotteryDraw?>> GetDrawByNumberAsync(
        LotteryType lotteryType,
        string drawNumber,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(drawNumber))
                return Result.Failure<LotteryDraw?>(ErrorCodes.VALIDATION_FAILED, "期号不能为空");

            const string sql = @"
                SELECT id, lottery_type, draw_number, draw_date,
                       red_numbers_json, blue_numbers_json,
                       sales_amount, pool_money, prize_grades_json,
                       prize_description, created_at
                FROM nc_lottery_draws
                WHERE lottery_type = $1 AND draw_number = $2
                LIMIT 1";

            var result = await _db.QuerySingleAsync<LotteryDrawRow>(
                sql, ct, lotteryType.ToString(), drawNumber.Trim());

            if (result.IsFailure)
                return Result.Failure<LotteryDraw?>(result.ErrorCode!, result.Message!);

            return Result.Success<LotteryDraw?>(result.Value?.ToModel());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取开奖记录失败");
            return Result.FromException<LotteryDraw?>(ex);
        }
    }

    public async Task<Result<List<LotteryDraw>>> GetRecentDrawsAsync(
        LotteryType lotteryType,
        int count = 30,
        CancellationToken ct = default)
    {
        try
        {
            count = Math.Clamp(count, 1, 100);

            const string sql = @"
                SELECT id, lottery_type, draw_number, draw_date,
                       red_numbers_json, blue_numbers_json,
                       sales_amount, pool_money, prize_grades_json,
                       prize_description, created_at
                FROM nc_lottery_draws
                WHERE lottery_type = $1
                ORDER BY draw_date DESC, draw_number DESC
                LIMIT $2";

            var result = await _db.QueryAsync<LotteryDrawRow>(sql, ct, lotteryType.ToString(), count);
            if (result.IsFailure)
                return Result.Failure<List<LotteryDraw>>(result.ErrorCode!, result.Message!);

            return Result.Success(result.Value.Select(row => row.ToModel()).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取最近开奖记录失败");
            return Result.FromException<List<LotteryDraw>>(ex);
        }
    }

    // ───────────────────────────── JSON 辅助 ─────────────────────────────

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property))
            return false;

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(property.GetString(), out value),
            _ => false
        };
    }

    private static decimal ParseAmount(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0m;

        var raw = property.ValueKind switch
        {
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.String => property.GetString(),
            _ => null
        };

        if (string.IsNullOrWhiteSpace(raw))
            return 0m;

        var cleaned = raw
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("，", string.Empty, StringComparison.Ordinal)
            .Replace("¥", string.Empty, StringComparison.Ordinal)
            .Replace("￥", string.Empty, StringComparison.Ordinal)
            .Trim();

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : 0m;
    }

    /// <summary>按字段名顺序取第一个非空金额（官方接口改过字段名，需兼容新旧）。</summary>
    private static decimal ParseAmountFirst(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            var value = ParseAmount(element, name);
            if (value > 0)
            {
                return value;
            }
        }

        return 0m;
    }

    /// <summary>数据库查询结果行 → <see cref="LotteryDraw"/>（枚举列需手工转换）。</summary>
    private sealed class LotteryDrawRow
    {
        public int Id { get; set; }
        public string LotteryType { get; set; } = "SSQ";
        public string DrawNumber { get; set; } = string.Empty;
        public DateTime DrawDate { get; set; }
        public string RedNumbersJson { get; set; } = "[]";
        public string BlueNumbersJson { get; set; } = "[]";
        public decimal SalesAmount { get; set; }
        public decimal PoolMoney { get; set; }
        public string PrizeGradesJson { get; set; } = "[]";
        public string? PrizeDescription { get; set; }
        public DateTime CreatedAt { get; set; }

        public LotteryDraw ToModel() => new()
        {
            Id = Id,
            LotteryType = Enum.TryParse<LotteryType>(LotteryType, true, out var type)
                ? type
                : global::NewCosmos.Models.Lottery.LotteryType.SSQ,
            DrawNumber = DrawNumber,
            DrawDate = DrawDate,
            RedNumbersJson = RedNumbersJson,
            BlueNumbersJson = BlueNumbersJson,
            SalesAmount = SalesAmount,
            PoolMoney = PoolMoney,
            PrizeGradesJson = PrizeGradesJson,
            PrizeDescription = PrizeDescription,
            CreatedAt = CreatedAt
        };
    }
}
