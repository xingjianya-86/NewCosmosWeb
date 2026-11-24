using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services;

/// <summary>
/// TODO(P0+): 服务端任务队列认领契约。
/// 服务端 P0 尚未提供 /api/agent/jobs/claim、/api/agent/jobs/{id}/complete、/fail，
/// 因此这里只定义接口，不实现网络调用，也不在 Program 中启动。
///
/// 计划流程（与 docs/API_CONTRACT.md §3 的 deviceKey 认证一致）：
/// 1. Agent 每 2–5 秒带 X-NC-Device-Key 认领一台本机绑定打印机的任务；
/// 2. 服务端只返回固化到本设备/本账号/本打印机的任务；
/// 3. Agent 调 PrintJobExecutor（复用 PrintExecuteService / PhotoPrintService）执行；
/// 4. 回写 Completed/Failed 与错误摘要，失败任务由服务端决定是否重试。
/// </summary>
public interface IAgentJobClient
{
    Task<AgentJob?> ClaimNextAsync(string serverBaseUrl, string deviceKey, CancellationToken ct = default);

    Task CompleteAsync(string serverBaseUrl, string deviceKey, long jobId, CancellationToken ct = default);

    Task FailAsync(string serverBaseUrl, string deviceKey, long jobId, string errorMessage, CancellationToken ct = default);
}

/// <summary>服务端下发的打印任务（P0+ 契约占位，字段以服务端最终 API 为准）。</summary>
public sealed class AgentJob
{
    public long Id { get; set; }

    public string JobNo { get; set; } = string.Empty;

    public string JobKind { get; set; } = "Template";

    public long? TemplateId { get; set; }

    public string? PrinterName { get; set; }
}

/// <summary>Agent 后台任务执行器。</summary>
public interface IJobWorker
{
    Task RunAsync(CancellationToken ct);
}

/// <summary>
/// 任务执行器骨架。当前服务端 P0 未提供任务认领接口，RunAsync 只记录一次说明并返回，
/// 避免误以为"已在执行队列"。接线点与 TODO 见 <see cref="IAgentJobClient"/>。
/// </summary>
public sealed class JobWorker : IJobWorker
{
    private readonly ILoggerService _logger;

    public JobWorker(ILoggerService logger)
    {
        _logger = logger;
    }

    public Task RunAsync(CancellationToken ct)
    {
        _logger.Warn("[JobWorker] 任务队列认领尚未实现：服务端 P0 未提供 /api/agent/jobs/*；当前仅运行注册与心跳。");
        return Task.CompletedTask;
    }
}
