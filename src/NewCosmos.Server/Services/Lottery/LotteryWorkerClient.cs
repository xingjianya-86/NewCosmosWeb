using System.Net;
using System.Text;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Server.Services.Lottery;

/// <summary>
/// 彩票 Worker（deploy/lottery，FastAPI）客户端。
/// <para>
/// 基址来自环境变量 <c>NC_LOTTERY_BASE_URL</c>，默认 <c>http://newcosmos-lottery:8790</c>；
/// 兼容旧变量 <c>NC_LOTTERY_WORKER_URL</c>。如 Worker 启用鉴权，可通过
/// <c>NC_LOTTERY_TOKEN</c> / <c>NC_LOTTERY_AUTH_HEADER</c> 注入请求头，不硬编码凭据。
/// 超时：health/models 5s、任务提交 10s、查询/取消 5s；训练可能数小时，
/// 绝不在 HTTP 请求内等待完成。失败统一映射为 <see cref="Result"/>，不伪造任务。
/// 本客户端只代理 JSON 接口，不代理文件上传。
/// </para>
/// </summary>
public sealed class LotteryWorkerClient
{
    public const string BaseUrlEnvironmentVariable = "NC_LOTTERY_BASE_URL";

    /// <summary>旧变量名，仅作兼容读取；新部署统一使用 <c>NC_LOTTERY_BASE_URL</c>。</summary>
    public const string LegacyBaseUrlEnvironmentVariable = "NC_LOTTERY_WORKER_URL";

    /// <summary>可选 Worker 令牌；为空时不发送鉴权头。</summary>
    public const string TokenEnvironmentVariable = "NC_LOTTERY_TOKEN";

    /// <summary>可选鉴权头名称，默认 Authorization。</summary>
    public const string AuthHeaderEnvironmentVariable = "NC_LOTTERY_AUTH_HEADER";

    private const string DefaultBaseUrl = "http://newcosmos-lottery:8790";
    private const string DefaultAuthHeader = "Authorization";

    // 契约：health/models 5s，任务提交 10s，查询/取消 5s。
    private static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan EnqueueTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly ILoggerService _logger;

    public LotteryWorkerClient(ILoggerService logger)
    {
        _logger = logger;
    }

    /// <summary>当前生效的 Worker 基址（去掉末尾斜杠）。</summary>
    public string BaseUrl => ResolveBaseUrl();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(5)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NewCosmos-Server-Lottery/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private static string ResolveBaseUrl()
    {
        var raw = Environment.GetEnvironmentVariable(BaseUrlEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            raw = Environment.GetEnvironmentVariable(LegacyBaseUrlEnvironmentVariable);

        return string.IsNullOrWhiteSpace(raw)
            ? DefaultBaseUrl
            : raw.Trim().TrimEnd('/');
    }

    /// <summary>创建 Worker 请求，并按配置附加可选鉴权头。</summary>
    private static HttpRequestMessage CreateRequest(HttpMethod method, string pathAndQuery)
    {
        var request = new HttpRequestMessage(method, $"{ResolveBaseUrl()}{pathAndQuery}");

        var token = Environment.GetEnvironmentVariable(TokenEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(token))
        {
            var headerName = Environment.GetEnvironmentVariable(AuthHeaderEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(headerName))
                headerName = DefaultAuthHeader;

            request.Headers.TryAddWithoutValidation(headerName.Trim(), token.Trim());
        }

        return request;
    }

    // ───────────────────────────── 健康 / 模型 ─────────────────────────────

    /// <summary>健康检查（GET /health，5 秒超时），返回 Worker 原始 JSON。</summary>
    public Task<Result<JsonElement>> GetHealthAsync(CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "/health");
        return ExecuteAsync<JsonElement>(request, HealthTimeout, ct);
    }

    /// <summary>模型清单原始响应（GET /models，5 秒超时）。</summary>
    public Task<Result<JsonElement>> GetModelsRawAsync(CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "/models");
        return ExecuteAsync<JsonElement>(request, HealthTimeout, ct);
    }

    // ───────────────────────────── 任务 ─────────────────────────────

    /// <summary>入队训练任务（POST /jobs/train）。</summary>
    public Task<Result<LotteryWorkerJob>> EnqueueTrainAsync(
        LotteryType lotteryType,
        int window,
        CancellationToken ct = default)
    {
        var body = new { lotteryType = ToWorkerType(lotteryType), window };
        return PostJobAsync("/jobs/train", body, ct);
    }

    /// <summary>入队预测任务（POST /jobs/predict）。</summary>
    public Task<Result<LotteryWorkerJob>> EnqueuePredictAsync(
        LotteryType lotteryType,
        int count,
        int window,
        CancellationToken ct = default)
    {
        var body = new { lotteryType = ToWorkerType(lotteryType), window, count };
        return PostJobAsync("/jobs/predict", body, ct);
    }

    /// <summary>查询单个任务（GET /jobs/{jobId}）。</summary>
    public async Task<Result<LotteryWorkerJob>> GetJobAsync(
        string jobId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return Result.Failure<LotteryWorkerJob>(ErrorCodes.VALIDATION_FAILED, "任务 ID 不能为空");

        using var request = CreateRequest(
            HttpMethod.Get, $"/jobs/{Uri.EscapeDataString(jobId.Trim())}");

        var result = await ExecuteAsync<WorkerJobPayload>(request, ReadTimeout, ct);
        return result.IsFailure
            ? Result.Failure<LotteryWorkerJob>(result.ErrorCode!, result.Message!)
            : Result.Success(ToJob(result.Value));
    }

    /// <summary>任务列表（GET /jobs?status=&amp;limit=）。</summary>
    public async Task<Result<List<LotteryWorkerJob>>> ListJobsAsync(
        string? status = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        var query = $"?limit={limit}";
        if (!string.IsNullOrWhiteSpace(status))
            query += $"&status={Uri.EscapeDataString(status.Trim())}";

        using var request = CreateRequest(HttpMethod.Get, $"/jobs{query}");
        var result = await ExecuteAsync<WorkerJobsPayload>(request, ReadTimeout, ct);
        if (result.IsFailure)
            return Result.Failure<List<LotteryWorkerJob>>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value.Items.Select(ToJob).ToList());
    }

    /// <summary>取消任务（POST /jobs/{jobId}/cancel）。</summary>
    public async Task<Result> CancelJobAsync(string jobId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "任务 ID 不能为空");

        var result = await CancelJobRawAsync(jobId, ct);
        return result.IsFailure
            ? Result.Failure(result.ErrorCode!, result.Message!)
            : Result.Success();
    }

    /// <summary>入队训练/预测任务，请求体原样转发（10 秒超时）。</summary>
    public Task<Result<JsonElement>> PostJobRawAsync(
        string path,
        JsonElement body,
        CancellationToken ct = default)
    {
        var json = body.ValueKind == JsonValueKind.Undefined ? "{}" : body.GetRawText();
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return ExecuteAsync<JsonElement>(request, EnqueueTimeout, ct);
    }

    /// <summary>任务列表原始响应（GET /jobs?status=&amp;limit=，5 秒超时）。</summary>
    public Task<Result<JsonElement>> ListJobsRawAsync(
        int limit = 50,
        string? status = null,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        var query = $"?limit={limit}";
        if (!string.IsNullOrWhiteSpace(status))
            query += $"&status={Uri.EscapeDataString(status.Trim())}";

        using var request = CreateRequest(HttpMethod.Get, $"/jobs{query}");
        return ExecuteAsync<JsonElement>(request, ReadTimeout, ct);
    }

    /// <summary>单个任务原始响应（GET /jobs/{jobId}，5 秒超时）。</summary>
    public Task<Result<JsonElement>> GetJobRawAsync(
        string jobId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return Task.FromResult(Result.Failure<JsonElement>(
                ErrorCodes.VALIDATION_FAILED, "任务 ID 不能为空"));
        }

        using var request = CreateRequest(
            HttpMethod.Get, $"/jobs/{Uri.EscapeDataString(jobId.Trim())}");
        return ExecuteAsync<JsonElement>(request, ReadTimeout, ct);
    }

    /// <summary>取消任务原始响应（POST /jobs/{jobId}/cancel，5 秒超时）。</summary>
    public Task<Result<JsonElement>> CancelJobRawAsync(
        string jobId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return Task.FromResult(Result.Failure<JsonElement>(
                ErrorCodes.VALIDATION_FAILED, "任务 ID 不能为空"));
        }

        using var request = CreateRequest(
            HttpMethod.Post, $"/jobs/{Uri.EscapeDataString(jobId.Trim())}/cancel");
        return ExecuteAsync<JsonElement>(request, ReadTimeout, ct);
    }

    // ───────────────────────────── 模型 ─────────────────────────────

    /// <summary>模型清单（GET /models）：配合 Worker 新增端点，返回已训练彩种/窗口。</summary>
    public async Task<Result<List<LotteryWorkerModel>>> GetModelsAsync(CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "/models");
        var result = await ExecuteAsync<WorkerModelsPayload>(request, HealthTimeout, ct);
        if (result.IsFailure)
            return Result.Failure<List<LotteryWorkerModel>>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value.Models ?? new List<LotteryWorkerModel>());
    }

    // ───────────────────────────── 内部实现 ─────────────────────────────

    private async Task<Result<LotteryWorkerJob>> PostJobAsync(
        string path,
        object body,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(body);
        using var request = CreateRequest(HttpMethod.Post, path);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var result = await ExecuteAsync<WorkerJobPayload>(request, EnqueueTimeout, ct);
        return result.IsFailure
            ? Result.Failure<LotteryWorkerJob>(result.ErrorCode!, result.Message!)
            : Result.Success(ToJob(result.Value));
    }

    private async Task<Result<T>> ExecuteAsync<T>(
        HttpRequestMessage request,
        TimeSpan timeout,
        CancellationToken ct,
        bool allowEmptyBody = false)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            using var response = await Http.SendAsync(request, cts.Token);
            var body = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                var (workerCode, workerMessage) = ParseWorkerError(body);
                var message = string.IsNullOrWhiteSpace(workerMessage)
                    ? $"彩票 Worker 返回 HTTP {(int)response.StatusCode}"
                    : workerMessage;

                _logger.Warn(
                    $"彩票 Worker 请求失败 HTTP {(int)response.StatusCode} " +
                    $"{request.Method} {request.RequestUri}: {workerCode} {message}");

                return Result.Failure<T>(MapWorkerCode(workerCode), message);
            }

            if (allowEmptyBody && string.IsNullOrWhiteSpace(body))
                return Result.Success(JsonSerializer.Deserialize<T>("{}", JsonOptions)!);

            if (string.IsNullOrWhiteSpace(body))
                return Result.Failure<T>(ErrorCodes.LOTTERY_WORKER_ERROR, "彩票 Worker 返回空响应");

            var payload = JsonSerializer.Deserialize<T>(body, JsonOptions);
            if (payload is null)
                return Result.Failure<T>(ErrorCodes.LOTTERY_WORKER_ERROR, "彩票 Worker 响应解析失败");

            return Result.Success(payload);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.Warn($"彩票 Worker 请求超时: {request.Method} {request.RequestUri}");
            return Result.Failure<T>(
                ErrorCodes.LOTTERY_WORKER_UNREACHABLE,
                "彩票 Worker 响应超时，请确认 Worker 容器运行正常");
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn($"彩票 Worker 不可达: {request.Method} {request.RequestUri} ({ex.Message})");
            return Result.Failure<T>(
                ErrorCodes.LOTTERY_WORKER_UNREACHABLE,
                $"彩票 Worker 不可达（{BaseUrl}），请确认容器已启动");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"彩票 Worker 调用异常: {request.Method} {request.RequestUri}");
            return Result.Failure<T>(ErrorCodes.LOTTERY_WORKER_ERROR, $"彩票 Worker 调用失败: {ex.Message}");
        }
    }

    private static (string Code, string Message) ParseWorkerError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (string.Empty, string.Empty);

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (string.Empty, string.Empty);

            var code = root.TryGetProperty("code", out var codeElement)
                && codeElement.ValueKind == JsonValueKind.String
                    ? codeElement.GetString() ?? string.Empty
                    : string.Empty;
            var message = root.TryGetProperty("message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.String
                    ? messageElement.GetString() ?? string.Empty
                    : string.Empty;

            return (code, message);
        }
        catch (JsonException)
        {
            return (string.Empty, string.Empty);
        }
    }

    private static string MapWorkerCode(string? workerCode) => workerCode?.Trim().ToUpperInvariant() switch
    {
        "VALIDATION_ERROR" => ErrorCodes.VALIDATION_FAILED,
        "JOB_NOT_FOUND" => ErrorCodes.LOTTERY_JOB_NOT_FOUND,
        "MODEL_NOT_FOUND" => ErrorCodes.LOTTERY_MODEL_NOT_FOUND,
        "QUEUE_FULL" => ErrorCodes.LOTTERY_QUEUE_FULL,
        "DEPENDENCY_MISSING" or "DB_NOT_CONFIGURED" => ErrorCodes.LOTTERY_DEPENDENCY_MISSING,
        "JOB_NOT_CANCELLABLE" => ErrorCodes.VALIDATION_FAILED,
        _ => ErrorCodes.LOTTERY_WORKER_ERROR
    };

    private static string ToWorkerType(LotteryType lotteryType)
        => lotteryType == LotteryType.DLT ? "dlt" : "ssq";

    private static LotteryWorkerJob ToJob(WorkerJobPayload payload) => new()
    {
        JobId = payload.JobId ?? string.Empty,
        Type = payload.Type ?? string.Empty,
        Status = payload.Status ?? string.Empty,
        Progress = payload.Progress,
        Message = payload.Message,
        Params = payload.Params,
        Result = payload.Result,
        Error = payload.Error,
        LogTail = payload.LogTail
    };

    // ───────────────────────────── 传输 DTO ─────────────────────────────

    private sealed class WorkerJobPayload
    {
        public string? JobId { get; set; }
        public string? Type { get; set; }
        public string? Status { get; set; }
        public int Progress { get; set; }
        public string? Message { get; set; }
        public JsonElement? Params { get; set; }
        public JsonElement? Result { get; set; }
        public JsonElement? Error { get; set; }
        public List<string>? LogTail { get; set; }
    }

    private sealed class WorkerJobsPayload
    {
        public List<WorkerJobPayload> Items { get; set; } = new();
    }

    private sealed class WorkerModelsPayload
    {
        public List<LotteryWorkerModel>? Models { get; set; }
    }
}

/// <summary>Worker 任务快照。</summary>
public sealed class LotteryWorkerJob
{
    public string JobId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Progress { get; set; }
    public string? Message { get; set; }
    public JsonElement? Params { get; set; }
    public JsonElement? Result { get; set; }
    public JsonElement? Error { get; set; }
    public List<string>? LogTail { get; set; }
}

/// <summary>Worker 已训练模型信息（GET /models 的元素）。</summary>
public sealed class LotteryWorkerModel
{
    public string LotteryType { get; set; } = string.Empty;
    public int Window { get; set; }
    public DateTime? TrainedAt { get; set; }
}
