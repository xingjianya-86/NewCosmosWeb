using NewCosmos.Agent.Configuration;
using NewCosmos.Agent.Security;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services;

/// <summary>worker 命令装配：校验配置/设备密钥 → 构造任务客户端与执行器 → 轮询。</summary>
public static class WorkerRunner
{
    public static async Task<int> RunAsync(
        AgentSettings settings,
        string configFilePath,
        IAgentJobClient client,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerBaseUrl))
        {
            AgentConsole.Error("缺少 ServerBaseUrl。请先配置 agent.json，或使用 --server https://...。");
            return 2;
        }

        var deviceKey = DeviceKeyStore.Load(settings, configFilePath, logger);
        if (string.IsNullOrWhiteSpace(deviceKey))
        {
            AgentConsole.Error(
                $"未找到设备密钥（{settings.DeviceKeyFile}）。请先在网页端生成绑定码，再运行: " +
                "NewCosmos.Agent.exe --bind-code <绑定码>（首次使用会自动注册本机）。");
            return 2;
        }

        if (!OperatingSystem.IsWindows())
        {
            AgentConsole.Error("任务渲染/打印依赖 Windows + Microsoft Office，当前平台不受支持。");
            return 2;
        }

        JobFileStore files;
        try
        {
            files = new JobFileStore(settings.PrintJobsDirectory);
            files.EnsureRoot();
        }
        catch (Exception ex)
        {
            AgentConsole.Error($"无法创建打印任务目录 {settings.PrintJobsDirectory}: {ex.Message}");
            logger.Error($"[JobWorker] 创建打印任务目录失败: {settings.PrintJobsDirectory}, {ex.Message}");
            return 2;
        }

        var renderer = new TemplateJobRenderer(logger);
        var printer = new JobPrinter(renderer, logger);
        var templates = new TemplateCache(logger);
        var executor = new JobTaskExecutor(
            renderer, printer, files, templates, logger, settings.PreviewKeepCount, settings.OutputRoot,
            client, settings.ServerBaseUrl, deviceKey);

        var worker = new JobWorker(
            client,
            executor,
            logger,
            settings.ServerBaseUrl,
            deviceKey,
            TimeSpan.FromSeconds(settings.JobPollSeconds),
            templates);

        AgentConsole.Info($"任务目录: {settings.PrintJobsDirectory}");
        AgentConsole.Info($"输出目录: {settings.OutputRoot}（批量保存按 分类\\期间 组织）");
        AgentConsole.Info($"模板缓存: {templates.Root}（≥200MB 自动清理最旧）");
        AgentConsole.Info($"预览保留最近 {settings.PreviewKeepCount} 个 PDF");

        return await worker.RunAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>
/// 默认运行模式：心跳 + 任务轮询并行。任一循环退出（例如设备被禁用）即取消另一个，
/// 避免出现“心跳停了但还在认领任务”的半死状态。
/// </summary>
public static class AgentServiceRunner
{
    public static async Task<int> RunHeartbeatAndWorkerAsync(
        AgentSettings settings,
        string configFilePath,
        IAgentClient agentClient,
        IAgentJobClient jobClient,
        ILoggerService logger,
        CancellationTokenSource cts)
    {
        var ct = cts.Token;

        var heartbeatTask = HeartbeatRunner.RunAsync(settings, configFilePath, agentClient, once: false, logger, ct);
        var workerTask = WorkerRunner.RunAsync(settings, configFilePath, jobClient, logger, ct);

        var first = await Task.WhenAny(heartbeatTask, workerTask).ConfigureAwait(false);

        var exitCode = 0;
        try
        {
            exitCode = await first.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C 正常停止
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Agent] 后台循环异常退出");
            AgentConsole.Error($"后台循环异常: {ex.Message}");
            exitCode = 1;
        }

        if (!cts.IsCancellationRequested)
        {
            try { cts.Cancel(); } catch { /* 忽略 */ }
        }

        try
        {
            await Task.WhenAll(heartbeatTask, workerTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 预期：另一个循环被取消
        }
        catch (Exception ex)
        {
            logger.Warn($"[Agent] 并行循环退出异常: {ex.Message}");
        }

        return exitCode;
    }
}
