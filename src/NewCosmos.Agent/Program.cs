using System.Text;
using NewCosmos.Agent;
using NewCosmos.Agent.Configuration;
using NewCosmos.Agent.Services;

try
{
    Console.OutputEncoding = Encoding.UTF8;
}
catch (Exception)
{
    // 某些重定向/无控制台环境不支持切换编码——继续用默认编码
}

if (!CliOptions.TryParse(args, out var options, out var parseError))
{
    AgentConsole.Error(parseError ?? "参数解析失败");
    CliOptions.PrintUsage();
    return 2;
}

if (options.Help)
{
    CliOptions.PrintUsage();
    return 0;
}

if (options.ShowVersion)
{
    Console.WriteLine($"NewCosmos.Agent {AgentRuntime.Version}");
    return 0;
}

var configPath = AgentConfigStore.ResolvePath(options.ConfigPath);
var logger = AgentRuntime.CreateLogger();
using var loggerScope = logger as IDisposable;

try
{
    var settings = AgentConfigStore.Load(configPath, logger);
    ApplyOverrides(settings, options);
    settings.Normalize(configPath);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        AgentConsole.Warn("收到 Ctrl+C，正在停止...");
        try { cts.Cancel(); } catch { /* 忽略 */ }
    };

    using var client = new HttpAgentClient(AgentRuntime.Version);

    switch (options.Command)
    {
        case "register":
            return await RegistrationRunner.RunAsync(settings, configPath, client, logger, cts.Token);

        case "heartbeat-once":
            return await HeartbeatRunner.RunAsync(settings, configPath, client, once: true, logger, cts.Token);

        case "heartbeat":
            // 缺省命令：心跳与任务轮询并行；任一退出即全部停止。
            return await AgentServiceRunner.RunHeartbeatAndWorkerAsync(
                settings, configPath, client, client, logger, cts);

        case "worker":
            return await WorkerRunner.RunAsync(settings, configPath, client, logger, cts.Token);

        case "printers":
            return PrintPrinters(logger);

        case "probe-office":
            return ProbeOffice(logger);

        case "render":
            return await TemplateRenderService.RunAsync(
                options.Template ?? string.Empty,
                options.Data ?? string.Empty,
                options.Out ?? string.Empty,
                logger,
                cts.Token);

        default:
            AgentConsole.Error($"未知命令: {options.Command}");
            CliOptions.PrintUsage();
            return 2;
    }
}
catch (Exception ex)
{
    AgentConsole.Error($"Agent 运行异常: {ex.Message}");
    logger.LogError(ex, "[Agent] 未处理异常");
    return 1;
}

static void ApplyOverrides(AgentSettings settings, CliOptions options)
{
    if (!string.IsNullOrWhiteSpace(options.Server))
        settings.ServerBaseUrl = options.Server.Trim().TrimEnd('/');

    if (!string.IsNullOrWhiteSpace(options.RegisterToken))
        settings.RegisterToken = options.RegisterToken.Trim();

    if (!string.IsNullOrWhiteSpace(options.MachineName))
        settings.MachineName = options.MachineName.Trim();

    if (options.HeartbeatSeconds is { } seconds)
        settings.HeartbeatSeconds = seconds;

    if (options.JobPollSeconds is { } pollSeconds)
        settings.JobPollSeconds = pollSeconds;

    if (!string.IsNullOrWhiteSpace(options.PrintJobsDirectory))
        settings.PrintJobsDirectory = options.PrintJobsDirectory.Trim();
}

static int PrintPrinters(NewCosmos.Services.Core.ILoggerService logger)
{
    var printers = PrinterDiscovery.GetInstalledPrinters(logger);
    if (printers.Count == 0)
    {
        AgentConsole.Warn("未发现本机打印机（不视为崩溃；请检查 Windows 打印服务/驱动）。");
        return 0;
    }

    AgentConsole.Info($"本机打印机 {printers.Count} 台：");
    foreach (var printer in printers)
        Console.WriteLine($"  {(printer.IsDefault ? "[默认] " : "       ")}{printer.Name}");

    return 0;
}

static int ProbeOffice(NewCosmos.Services.Core.ILoggerService logger)
{
    var info = OfficeProbe.Detect(logger);
    AgentConsole.Info(OfficeProbe.Describe(info));

    if (!info.HasWord || !info.HasExcel)
        AgentConsole.Warn("Word/Excel 至少一项不可用；文书渲染与 PDF 导出会失败。");

    return 0;
}
