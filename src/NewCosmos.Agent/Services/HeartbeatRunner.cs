using NewCosmos.Agent.Configuration;
using NewCosmos.Agent.Models;
using NewCosmos.Agent.Security;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services;

/// <summary>心跳：默认 30 秒一次，Ctrl+C 退出；--heartbeat-once 单次执行。</summary>
public static class HeartbeatRunner
{
    public static async Task<int> RunAsync(
        AgentSettings settings,
        string configFilePath,
        IAgentClient client,
        bool once,
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

        var interval = TimeSpan.FromSeconds(Math.Clamp(settings.HeartbeatSeconds <= 0 ? 30 : settings.HeartbeatSeconds, 5, 3600));
        AgentConsole.Info($"心跳目标: {settings.ServerBaseUrl}；机器名: {settings.MachineName}；间隔: {interval.TotalSeconds:F0}s"
                          + (once ? "（单次）" : "；按 Ctrl+C 退出"));

        var first = true;
        while (!ct.IsCancellationRequested)
        {
            // 每轮重新探测打印机：现场可能临时接入/移除打印机
            var printers = PrinterDiscovery.GetInstalledPrinters(logger);
            var request = new AgentHeartbeatRequest
            {
                MachineName = settings.MachineName,
                AgentVersion = AgentRuntime.Version,
                Printers = printers,
                Capabilities = ResolveCapabilities(logger).ToList()
            };

            try
            {
                await client.HeartbeatAsync(settings.ServerBaseUrl, deviceKey, request, ct).ConfigureAwait(false);

                if (first)
                {
                    AgentConsole.Success($"心跳成功；上报打印机 {printers.Count} 台");
                    logger.Info($"[Heartbeat] 首次心跳成功: Machine={settings.MachineName}, Printers={printers.Count}");
                    first = false;
                }
                else
                {
                    AgentConsole.Info($"心跳成功 ({DateTime.Now:HH:mm:ss})；打印机 {printers.Count} 台");
                }

                if (once)
                    return 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (TaskCanceledException ex)
            {
                AgentConsole.Error($"心跳超时: {ex.Message}");
                logger.Warn($"[Heartbeat] 心跳请求超时: {ex.Message}");
                if (once) return 3;
            }
            catch (HttpRequestException ex)
            {
                AgentConsole.Error($"无法连接服务端 {settings.ServerBaseUrl}: {ex.Message}");
                logger.Warn($"[Heartbeat] 无法连接服务端 {settings.ServerBaseUrl}: {ex.Message}");
                if (once) return 3;
            }
            catch (AgentApiException ex)
            {
                var trace = string.IsNullOrWhiteSpace(ex.TraceId) ? string.Empty : $", traceId={ex.TraceId}";
                AgentConsole.Error($"心跳失败 [{ex.Code}] HTTP {(ex.StatusCode == 0 ? "-" : ex.StatusCode)}{trace}: {ex.Message}");
                logger.Warn($"[Heartbeat] 心跳失败 [{ex.Code}] HTTP {ex.StatusCode}{trace}: {ex.Message}");

                if (ex.IsUnauthorized)
                {
                    AgentConsole.Error("设备不存在或被禁用，停止心跳。请确认服务端设备状态；如需重新绑定，请清理本地设备密钥后使用网页绑定码重新注册。");
                    return 3;
                }

                if (once)
                    return 3;
            }
            catch (Exception ex)
            {
                AgentConsole.Error($"心跳异常: {ex.Message}");
                logger.LogError(ex, "[Heartbeat] 心跳异常");
                if (once)
                    return 1;
            }

            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        AgentConsole.Info("心跳已停止");
        return 0;
    }

    /// <summary>
    /// 本机操作能力（LocalOp 通道）上报清单：真实探测 + 5 分钟缓存
    /// （font = 随包公文字体目录存在且可写；zerotier = zerotier-cli 存在）。
    /// 心跳默认 30 秒一轮，重活不能每轮都做；探测失败不上报该能力。
    /// </summary>
    private static IReadOnlyList<string> ResolveCapabilities(ILoggerService logger)
        => LocalOpCapabilityProbe.Resolve(logger);
}
