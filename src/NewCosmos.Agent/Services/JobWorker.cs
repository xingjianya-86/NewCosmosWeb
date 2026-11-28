using NewCosmos.Agent.Models;
using NewCosmos.Agent.Services.Update;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 服务端任务队列契约（Agent 侧）：
/// POST /api/agent/jobs/claim、GET /payload、POST /complete、POST /fail。
/// 所有请求都带 X-NC-Device-Key；实现见 <see cref="HttpAgentClient"/> 的 partial。
/// </summary>
public interface IAgentJobClient
{
    Task<IReadOnlyList<AgentJob>> ClaimAsync(
        string serverBaseUrl,
        string deviceKey,
        int max,
        IReadOnlyList<AgentCachedTemplateRef>? cachedTemplates = null,
        CancellationToken ct = default);

    Task<AgentJobPayload> GetPayloadAsync(
        string serverBaseUrl,
        string deviceKey,
        long jobId,
        string? cachedTemplateHash = null,
        CancellationToken ct = default);

    Task CompleteAsync(
        string serverBaseUrl,
        string deviceKey,
        long jobId,
        string? message = null,
        CancellationToken ct = default);

    Task FailAsync(
        string serverBaseUrl,
        string deviceKey,
        long jobId,
        string errorMessage,
        CancellationToken ct = default);

    /// <summary>
    /// 打印/保存成功后回传实际生成文件（写入服务端 print_records，供原样补打）。
    /// 失败由调用方降级为 Warn，不影响任务完成。
    /// </summary>
    Task UploadPrintRecordsAsync(
        string serverBaseUrl,
        string deviceKey,
        long jobId,
        IReadOnlyList<AgentPrintRecord> records,
        CancellationToken ct = default);
}

/// <summary>单个任务的执行器：下载负载之后的渲染/预览/打印。</summary>
public interface IJobTaskExecutor
{
    /// <summary>
    /// 执行成功后返回 warning 文案（如降级打印，可为 null）；失败必须抛出异常（由 <see cref="JobWorker"/> 回写 fail）。
    /// </summary>
    Task<string?> ExecuteAsync(AgentJob job, AgentJobPayload payload, CancellationToken ct);
}

/// <summary>Agent 后台任务执行器。</summary>
public interface IJobWorker
{
    /// <summary>持续轮询直到取消；返回进程退出码（0=正常，3=设备未授权）。</summary>
    Task<int> RunAsync(CancellationToken ct);
}

/// <summary>
/// 任务队列轮询：每 <c>JobPollSeconds</c> 秒 claim 一次（max=1），
/// 下载 payload → 交给 <see cref="IJobTaskExecutor"/> → complete / fail。
/// 单个任务失败只回写失败并继续轮询，不会中断整个循环。
/// </summary>
public sealed class JobWorker : IJobWorker
{
    private const int MaxJobsPerClaim = 1;

    private readonly IAgentJobClient _client;
    private readonly IJobTaskExecutor _executor;
    private readonly TemplateCache? _templates;
    private readonly ILoggerService _logger;
    private readonly string _serverBaseUrl;
    private readonly string _deviceKey;
    private readonly TimeSpan _pollInterval;
    private readonly AgentUpdateGate? _gate;

    public JobWorker(
        IAgentJobClient client,
        IJobTaskExecutor executor,
        ILoggerService logger,
        string serverBaseUrl,
        string deviceKey,
        TimeSpan pollInterval,
        TemplateCache? templates = null,
        AgentUpdateGate? gate = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _templates = templates;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serverBaseUrl = serverBaseUrl ?? string.Empty;
        _deviceKey = deviceKey ?? string.Empty;
        _pollInterval = pollInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(3) : pollInterval;
        // 可选注入：不传门控行为完全不变（零回归），接入自动升级的宿主才传入共享实例。
        _gate = gate;
    }

    public async Task<int> RunAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_serverBaseUrl) || string.IsNullOrWhiteSpace(_deviceKey))
        {
            _logger.Error("[JobWorker] 缺少 ServerBaseUrl 或 deviceKey，任务轮询未启动");
            return 2;
        }

        AgentConsole.Info($"任务轮询已启动: {_serverBaseUrl}；间隔 {_pollInterval.TotalSeconds:F0}s；Ctrl+C 退出");
        _logger.Info($"[JobWorker] 任务轮询启动: PollSeconds={_pollInterval.TotalSeconds:F0}");

        var lastClaimErrorAt = DateTime.MinValue;

        while (!ct.IsCancellationRequested)
        {
            // 自动升级空闲门控：暂停期间不再 claim 新任务，只短延时等待。
            // 已在执行的任务不受影响（由 gate 的 in-flight 计数等待归零），下轮升级据此判断能否安全安装。
            if (_gate is { IsPauseRequested: true })
            {
                try
                {
                    await Task.Delay(AgentUpdateDefaults.PausePollMilliseconds, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            var claimed = false;

            try
            {
                var jobs = await _client
                    .ClaimAsync(
                        _serverBaseUrl,
                        _deviceKey,
                        MaxJobsPerClaim,
                        _templates?.GetRecentEntries(),
                        ct)
                    .ConfigureAwait(false);

                foreach (var job in jobs)
                {
                    if (ct.IsCancellationRequested) break;
                    claimed = true;
                    await ProcessJobAsync(job, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (AgentApiException ex)
            {
                var trace = string.IsNullOrWhiteSpace(ex.TraceId) ? string.Empty : $", traceId={ex.TraceId}";
                if (ShouldLogClaimError(ref lastClaimErrorAt))
                {
                    _logger.Warn($"[JobWorker] 认领任务失败 [{ex.Code}] HTTP {ex.StatusCode}{trace}: {ex.Message}");
                    AgentConsole.Warn($"认领任务失败 [{ex.Code}]: {ex.Message}（同类错误 30 秒内不再重复提示）");
                }

                if (ex.IsUnauthorized)
                {
                    AgentConsole.Error("设备不存在或被禁用，停止任务轮询。请确认服务端设备状态，必要时重新注册。");
                    _logger.Error("[JobWorker] 设备未授权，任务轮询停止");
                    return 3;
                }
            }
            catch (TaskCanceledException ex)
            {
                if (ShouldLogClaimError(ref lastClaimErrorAt))
                    _logger.Warn($"[JobWorker] 认领任务超时: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                if (ShouldLogClaimError(ref lastClaimErrorAt))
                    _logger.Warn($"[JobWorker] 无法连接服务端 {_serverBaseUrl}: {ex.Message}");
            }
            catch (Exception ex)
            {
                if (ShouldLogClaimError(ref lastClaimErrorAt))
                {
                    _logger.LogError(ex, "[JobWorker] 任务轮询异常");
                    AgentConsole.Error($"任务轮询异常: {ex.Message}");
                }
            }

            // 有任务时立即继续认领，避免空闲等待；无任务按配置间隔轮询。
            if (claimed) continue;

            try
            {
                await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        AgentConsole.Info("任务轮询已停止");
        _logger.Info("[JobWorker] 任务轮询停止");
        return 0;
    }

    /// <summary>
    /// 任务执行入口：in-flight 计数必须覆盖整个任务生命周期（含失败回写），
    /// 否则升级方会误判"已空闲"而覆盖正在执行任务的程序目录。
    /// </summary>
    private async Task ProcessJobAsync(AgentJob job, CancellationToken ct)
    {
        _gate?.BeginJob();
        try
        {
            await ProcessJobCoreAsync(job, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate?.EndJob();
        }
    }

    private async Task ProcessJobCoreAsync(AgentJob job, CancellationToken ct)
    {
        var jobNo = string.IsNullOrWhiteSpace(job.JobNo) ? $"job-{job.Id}" : job.JobNo.Trim();

        AgentConsole.Info($"认领任务 #{job.Id} {jobNo}（{job.JobKind}）");
        _logger.Info($"[JobWorker] 认领任务: Id={job.Id}, No={jobNo}, Kind={job.JobKind}, Template={job.TemplateName ?? "-"}");

        var startedAt = DateTime.UtcNow;
        try
        {
            // payload 带上本地缓存 hash：服务端命中时不重复下发模板 Base64（老服务端忽略该参数）。
            var payload = await _client
                .GetPayloadAsync(
                    _serverBaseUrl,
                    _deviceKey,
                    job.Id,
                    _templates?.FindCachedHash(job.TemplateId),
                    ct)
                .ConfigureAwait(false);

            var warning = await _executor.ExecuteAsync(job, payload, ct).ConfigureAwait(false);

            var stateWritten = false;
            try
            {
                await _client.CompleteAsync(_serverBaseUrl, _deviceKey, job.Id, warning, ct).ConfigureAwait(false);
                stateWritten = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 任务已实际完成，回写失败只能留痕（服务端可能按超时重试，重复打印风险由服务端幂等/人工处理）。
                _logger.Error($"[JobWorker] 任务完成回写失败: Id={job.Id}, No={jobNo}, {ex.Message}");
                AgentConsole.Warn($"任务 {jobNo} 完成回写失败: {ex.Message}");
            }

            var elapsed = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
            if (stateWritten)
            {
                AgentConsole.Success($"任务完成 #{job.Id} {jobNo}（{elapsed} ms）");
                _logger.Info($"[JobWorker] 任务完成: Id={job.Id}, No={jobNo}, ElapsedMs={elapsed}, Warning={(string.IsNullOrWhiteSpace(warning) ? "-" : warning)}");
                if (!string.IsNullOrWhiteSpace(warning))
                {
                    AgentConsole.Warn($"任务 {jobNo} 完成但有警告: {warning}");
                }
            }
            else
            {
                AgentConsole.Warn($"任务已执行但完成状态未回写 #{job.Id} {jobNo}（{elapsed} ms），请检查服务端任务状态");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Agent 停止导致任务中断：尽力回写失败，交给服务端重试；[CT 豁免] 状态回写必须完成。
            await TryFailAsync(job.Id, jobNo, "Agent 停止，任务中断，请重试", CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            var message = SummarizeError(ex);
            _logger.Error($"[JobWorker] 任务失败: Id={job.Id}, No={jobNo}, Error={message}");
            AgentConsole.Error($"任务失败 #{job.Id} {jobNo}: {message}");
            await TryFailAsync(job.Id, jobNo, message, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task TryFailAsync(long jobId, string jobNo, string message, CancellationToken ct)
    {
        try
        {
            await _client.FailAsync(_serverBaseUrl, _deviceKey, jobId, message, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[JobWorker] 任务失败回写失败: Id={jobId}, No={jobNo}, {ex.Message}");
            AgentConsole.Warn($"任务 {jobNo} 失败回写失败: {ex.Message}");
        }
    }

    /// <summary>错误日志节流：轮询间隔很短，同类认领错误 30 秒内只提示一次。</summary>
    private static bool ShouldLogClaimError(ref DateTime lastLoggedAt)
    {
        var now = DateTime.UtcNow;
        if (now - lastLoggedAt < TimeSpan.FromSeconds(30))
            return false;

        lastLoggedAt = now;
        return true;
    }

    /// <summary>错误摘要：截断 + 去除换行，避免把整段 COM/堆栈写进服务端错误字段。</summary>
    internal static string SummarizeError(Exception ex)
    {
        var message = ex is BusinessException be && !string.IsNullOrWhiteSpace(be.ErrorCode)
            ? $"[{be.ErrorCode}] {be.Message}"
            : ex.Message;

        if (string.IsNullOrWhiteSpace(message))
            message = ex.GetType().Name;

        message = message.Replace("\r", " ").Replace("\n", " ").Trim();
        return HttpAgentClient.Truncate(message, 500);
    }
}
