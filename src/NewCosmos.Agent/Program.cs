using NewCosmos.Agent.Configuration;
using NewCosmos.Agent.Services;
using NewCosmos.Agent.Services.Update;
using NewCosmos.Helpers;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent;

internal static class Program
{
    /// <summary>
    /// 入口保持 STA（WinForms 托盘/通知气泡要求）。
    /// 托盘模式下 Application.Run 之前不做任何 await，确保消息循环跑在 STA 线程上；
    /// CLI 模式同步等待异步命令完成，输出由 <see cref="ConsoleHost"/> 负责。
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        PdfSharpLicenseBootstrap.EnsureLoaded();
        ILoggerService? logger = null;

        try
        {
            if (!CliOptions.TryParse(args, out var options, out var parseError))
            {
                ConsoleHost.Initialize(cliMode: true);
                AgentConsole.Error(parseError ?? "参数解析失败");
                CliOptions.PrintUsage();
                return 2;
            }

            if (options.Help)
            {
                ConsoleHost.Initialize(cliMode: true);
                CliOptions.PrintUsage();
                return 0;
            }

            if (options.ShowVersion)
            {
                ConsoleHost.Initialize(cliMode: true);
                Console.WriteLine($"NewCosmos.Agent {AgentRuntime.Version}");
                return 0;
            }

            if (options.Command is "install-autostart" or "uninstall-autostart")
            {
                ConsoleHost.Initialize(cliMode: true);
                return RunAutoStartCommand(options);
            }

            var isTray = options.Command == "tray";
            ConsoleHost.Initialize(cliMode: !isTray);

            var configPath = AgentConfigStore.ResolvePath(options.ConfigPath);
            logger = AgentRuntime.CreateLogger();
            using var loggerScope = logger as IDisposable;

            try
            {
                var settings = AgentConfigStore.Load(configPath, logger);
                ApplyOverrides(settings, options);
                settings.Normalize(configPath);

                using var cts = new CancellationTokenSource();

                // 只有真正附着控制台时才注册 Ctrl+C；托盘/双击模式没有控制台信号。
                if (ConsoleHost.HasConsole)
                {
                    Console.CancelKeyPress += (_, eventArgs) =>
                    {
                        eventArgs.Cancel = true;
                        AgentConsole.Warn("收到 Ctrl+C，正在停止...");
                        try { cts.Cancel(); } catch { /* 忽略 */ }
                    };
                }

                using var client = new HttpAgentClient(AgentRuntime.Version);

                if (isTray)
                {
                    // 缺省模式：托盘 + 心跳 + 任务轮询（托盘退出时取消后台循环）。
                    System.Windows.Forms.Application.EnableVisualStyles();
                    using var tray = new TrayApplicationContext(settings, configPath, client, logger, cts);
                    System.Windows.Forms.Application.Run(tray);

                    var exitCode = 0;
                    try
                    {
                        exitCode = tray.Completion.ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        // 用户退出：后台循环被取消属正常
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[Agent] 托盘后台循环异常退出");
                        exitCode = 1;
                    }

                    return exitCode;
                }

                return RunCliCommand(options, settings, configPath, client, logger, cts);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Agent] 未处理异常");
                ReportFailure($"Agent 运行异常: {ex.Message}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            try
            {
                TryLogFailure(logger, ex);
            }
            catch (Exception)
            {
                // 日志都不可用时继续走 MessageBox/控制台兜底。
            }

            ReportFailure($"Agent 启动失败: {ex.Message}");
            return 1;
        }
        finally
        {
            // CLI 从资源管理器双击时，用 MessageBox 展示暂存的输出（含 --help / 参数错误）。
            ConsoleHost.CompleteCli();
            ConsoleHost.Detach();
        }
    }

    /// <summary>执行非托盘 CLI 命令；错误由外层统一记录并回写退出码。</summary>
    private static int RunCliCommand(
        CliOptions options,
        AgentSettings settings,
        string configPath,
        HttpAgentClient client,
        ILoggerService logger,
        CancellationTokenSource cts)
    {
        switch (options.Command)
        {
            case "register":
                return RegistrationRunner
                    .RunAsync(settings, configPath, client, logger, cts.Token)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            case "heartbeat-once":
                return HeartbeatRunner
                    .RunAsync(settings, configPath, client, once: true, logger, cts.Token)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            case "heartbeat":
                // 控制台并行模式：心跳与任务轮询并行；任一退出即全部停止。
                return AgentServiceRunner
                    .RunHeartbeatAndWorkerAsync(settings, configPath, client, client, logger, cts)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            case "worker":
                return WorkerRunner
                    .RunAsync(settings, configPath, client, logger, cts.Token)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            case "printers":
                return PrintPrinters(logger);

            case "bind-code":
                return PrinterBindRunner
                    .RunCliAsync(settings, configPath, client, options.BindCode, logger, cts.Token)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            case "probe-office":
                return ProbeOffice(logger);

            case "check-update":
                return RunCheckUpdateCommand(settings, logger, cts.Token);

            case "render":
                return TemplateRenderService
                    .RunAsync(
                        options.Template ?? string.Empty,
                        options.Data ?? string.Empty,
                        options.Out ?? string.Empty,
                        logger,
                        cts.Token)
                    .ConfigureAwait(false).GetAwaiter().GetResult();

            default:
                AgentConsole.Error($"未知命令: {options.Command}");
                CliOptions.PrintUsage();
                return 2;
        }
    }

    /// <summary>异常兜底：文件日志 +（托盘模式）MessageBox；CLI 无控制台由暂存输出统一弹出。</summary>
    private static void ReportFailure(string message)
    {
        AgentConsole.Error(message);

        if (ConsoleHost.IsTrayMode)
            ConsoleHost.ReportError("NewCosmos Agent", message);
    }

    private static void TryLogFailure(ILoggerService? logger, Exception ex)
    {
        var target = logger ?? AgentRuntime.CreateLogger();
        target.LogError(ex, "[Agent] 未处理异常");
        if (!ReferenceEquals(target, logger))
            (target as IDisposable)?.Dispose();
    }

    private static int RunAutoStartCommand(CliOptions options)
    {
        if (!OperatingSystem.IsWindows())
        {
            AgentConsole.Error("开机自启只支持 Windows。");
            return 2;
        }

        if (options.Command == "uninstall-autostart")
        {
            if (AutoStartService.Uninstall(out var removeError))
                return 0;

            AgentConsole.Error($"删除开机自启失败: {removeError}");
            return 1;
        }

        if (AutoStartService.Install(options.AutostartMode, out var installError))
            return 0;

        AgentConsole.Error($"设置开机自启失败: {installError}");
        return 1;
    }

    private static void ApplyOverrides(AgentSettings settings, CliOptions options)
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

    private static int PrintPrinters(ILoggerService logger)
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

    private static int ProbeOffice(ILoggerService logger)
    {
        var info = OfficeProbe.Detect(logger);
        AgentConsole.Info(OfficeProbe.Describe(info));

        if (!info.HasWord || !info.HasExcel)
            AgentConsole.Warn("Word/Excel 至少一项不可用；文书渲染与 PDF 导出会失败。");

        return 0;
    }

    /// <summary>
    /// --check-update：部署机手工验证自动升级链路（检查清单 → 下载 → SHA256/Authenticode 校验
    /// → 空闲门控 → 静默安装 → 退出）。只跑一次就返回；发现新版本时安装器已启动，
    /// 本进程随后自然退出，新版由安装脚本 [Run] 段拉起。
    /// </summary>
    private static int RunCheckUpdateCommand(AgentSettings settings, ILoggerService logger, CancellationToken ct)
    {
        var service = new AgentUpdateService(
            settings,
            logger,
            new AgentUpdateGate(), // CLI 没有任务循环，门控只是占位（不会有 claim 发生）
            ProcessAgentInstaller.Launch,
            notify: (level, message) =>
            {
                if (level == UpdateNotifyLevel.Warning)
                    AgentConsole.Warn(message);
                else
                    AgentConsole.Info(message);
            },
            requestExit: null); // CLI 命令返回后进程即退出，无需托盘式让位

        var result = service.CheckOnceAsync(ct).ConfigureAwait(false).GetAwaiter().GetResult();
        if (result.IsFailure)
        {
            AgentConsole.Error($"检查更新失败: {result.Message}");
            return 3;
        }

        AgentConsole.Success(result.Message);
        return 0;
    }
}
