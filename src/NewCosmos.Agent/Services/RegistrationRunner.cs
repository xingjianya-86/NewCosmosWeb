using NewCosmos.Agent.Configuration;
using NewCosmos.Agent.Models;
using NewCosmos.Agent.Security;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services;

/// <summary>设备注册（一次性）：POST /api/agent/register。</summary>
public static class RegistrationRunner
{
    public static async Task<int> RunAsync(
        AgentSettings settings,
        string configFilePath,
        IAgentClient client,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerBaseUrl))
        {
            AgentConsole.Error("缺少 ServerBaseUrl。请在 agent.json 配置，或使用 --server https://... 指定。");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(settings.RegisterToken))
        {
            AgentConsole.Error(
                "缺少注册令牌。请设置环境变量 NC_AGENT_REGISTER_TOKEN，或在 agent.json 配置 registerToken，" +
                "或使用 --register-token <token>（令牌不落盘到命令行历史之外）。");
            return 2;
        }

        var printers = PrinterDiscovery.GetInstalledPrinters(logger);
        AgentConsole.Info($"机器名: {settings.MachineName}");
        AgentConsole.Info($"服务端: {settings.ServerBaseUrl}");
        AgentConsole.Info($"本机打印机: {(printers.Count == 0 ? "（未发现）" : string.Join(" | ", printers.Select(p => p.IsDefault ? $"{p.Name} [默认]" : p.Name)))}");

        var request = new AgentRegisterRequest
        {
            MachineName = settings.MachineName,
            AgentVersion = AgentRuntime.Version,
            Printers = printers
        };

        try
        {
            AgentConsole.Info("正在注册设备...");
            var response = await client.RegisterAsync(
                settings.ServerBaseUrl, settings.RegisterToken, request, ct).ConfigureAwait(false);

            settings.DeviceId = response.DeviceId;
            if (response.HeartbeatSeconds > 0)
                settings.HeartbeatSeconds = Math.Clamp(response.HeartbeatSeconds, 5, 3600);

            // 注册令牌是一次性凭据：注册成功后不落盘（配置里如果原本有，也一并清除）。
            settings.RegisterToken = string.Empty;

            DeviceKeyStore.Save(settings, configFilePath, response.DeviceKey, logger);

            AgentConsole.Success(
                $"注册成功: deviceId={response.DeviceId}, heartbeat={settings.HeartbeatSeconds}s, 配置={configFilePath}");
            AgentConsole.Info("设备密钥已按 DPAPI(CurrentUser) 保存；如果日志提示降级，请确认该 Windows 账户受控。");
            logger.Info($"[Register] 设备注册成功: DeviceId={response.DeviceId}, Machine={settings.MachineName}");
            return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            AgentConsole.Warn("注册已取消");
            return 130;
        }
        catch (TaskCanceledException ex)
        {
            AgentConsole.Error($"注册超时（无法在超时时间内连接服务端）: {ex.Message}");
            logger.Warn($"[Register] 注册请求超时: {ex.Message}");
            return 3;
        }
        catch (HttpRequestException ex)
        {
            AgentConsole.Error($"无法连接服务端 {settings.ServerBaseUrl}: {ex.Message}");
            logger.Warn($"[Register] 无法连接服务端 {settings.ServerBaseUrl}: {ex.Message}");
            return 3;
        }
        catch (AgentApiException ex)
        {
            var trace = string.IsNullOrWhiteSpace(ex.TraceId) ? string.Empty : $", traceId={ex.TraceId}";
            AgentConsole.Error($"注册失败 [{ex.Code}] HTTP {(ex.StatusCode == 0 ? "-" : ex.StatusCode)}{trace}: {ex.Message}");
            logger.Error($"[Register] 注册失败 [{ex.Code}] HTTP {ex.StatusCode}{trace}: {ex.Message}");
            return ex.StatusCode == 0 ? 2 : 3;
        }
        catch (Exception ex)
        {
            AgentConsole.Error($"注册异常: {ex.Message}");
            logger.LogError(ex, "[Register] 注册异常");
            return 1;
        }
    }
}
