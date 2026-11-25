using System.Text;
using NewCosmos.Agent;
using NewCosmos.Agent.Configuration;
using NewCosmos.Agent.Services;

namespace NewCosmos.Agent;

internal static class Program
{
    /// <summary>
    /// 入口保持 STA（WinForms 托盘/通知气泡要求）：
    /// 托盘模式下 Application.Run 之前不做任何 await，确保消息循环跑在 STA 线程上。
    /// </summary>
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
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

        if (options.Command is "install-autostart" or "uninstall-autostart")
        {
            return RunAutoStartCommand(options);
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

            if (options.Command == "tray")
            {
                // 缺省模式：托盘 + 心跳 + 任务轮询（托盘退出时取消后台循环）。
                // 注意：此处不能先 await，否则 Application.Run 可能离开 STA 线程。
                System.Windows.Forms.Application.EnableVisualStyles();
                using var tray = new TrayApplicationContext(settings, configPath, client, logger, cts);
                System.Windows.Forms.Application.Run(tray);

                var exitCode = 0;
                try
                {
                    exitCode = await tray.Completion.ConfigureAwait(false);
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

            switch (options.Command)
            {
                case "register":
                    return await RegistrationRunner.RunAsync(settings, configPath, client, logger, cts.Token);

                case "heartbeat-once":
                    return await HeartbeatRunner.RunAsync(settings, configPath, client, once: true, logger, cts.Token);

                case "heartbeat":
                    // 控制台并行模式：心跳与任务轮询并行；任一退出即全部停止。
                    return await AgentServiceRunner.RunHeartbeatAndWorkerAsync(
                        settings, configPath, client, client, logger, cts);

                case "worker":
                    return await WorkerRunner.RunAsync(settings, configPath, client, logger, cts.Token);

                case "printers":
                    return PrintPrinters(logger);

                case "bind-code":
                    return await PrinterBindRunner.RunCliAsync(
                        settings, configPath, client, options.BindCode, logger, cts.Token);

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

    private static int PrintPrinters(NewCosmos.Services.Core.ILoggerService logger)
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

    private static int ProbeOffice(NewCosmos.Services.Core.ILoggerService logger)
    {
        var info = OfficeProbe.Detect(logger);
        AgentConsole.Info(OfficeProbe.Describe(info));

        if (!info.HasWord || !info.HasExcel)
            AgentConsole.Warn("Word/Excel 至少一项不可用；文书渲染与 PDF 导出会失败。");

        return 0;
    }
}
