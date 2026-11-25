using NewCosmos.Agent.Configuration;
using NewCosmos.Agent.Models;
using NewCosmos.Agent.Security;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services;

/// <summary>绑定失败的分类，便于命令行映射退出码、托盘选择提示文案。</summary>
public enum PrinterBindFailure
{
    None,
    InvalidCode,
    MissingServerUrl,

    /// <summary>保留给旧调用方；绑定流程已改为自动注册，不再因缺少设备密钥失败。</summary>
    MissingDeviceKey,
    NoPrinters,
    Unauthorized,
    CodeExpired,
    CodeUsed,
    RateLimited,
    NotFound,
    ApiError,
    Network,
    Timeout,
    Canceled,
    Unexpected
}

/// <summary>绑定结果（含面向用户的中文消息，不包含绑定码/设备密钥）。</summary>
public sealed class PrinterBindResult
{
    public PrinterBindFailure Failure { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<PrinterBinding> Bindings { get; init; } = Array.Empty<PrinterBinding>();

    public int ExitCode { get; init; }

    /// <summary>本次是否走「绑定码自动注册」分支（首次使用没有 deviceKey）。</summary>
    public bool AutoRegistered { get; init; }

    public bool Success => Failure == PrinterBindFailure.None;

    public static PrinterBindResult Fail(PrinterBindFailure failure, string message, int exitCode) =>
        new() { Failure = failure, Message = message, ExitCode = exitCode };
}

/// <summary>
/// 「绑定本机打印机」共用逻辑：校验 6 位码 → 枚举本机打印机 →
/// 有 deviceKey 走 POST /api/printers/bind-by-code；
/// 无 deviceKey 则用 POST /api/agent/register-by-code 自动注册并绑定。
/// 托盘与命令行都走这里，避免两份实现。
/// </summary>
public static class PrinterBindRunner
{
    public static bool TryNormalizeCode(string? input, out string code)
    {
        code = (input ?? string.Empty).Trim();
        if (code.Length != 6)
            return false;

        foreach (var c in code)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return true;
    }

    /// <summary>托盘入口：不打印控制台，返回结构化结果供窗体展示。</summary>
    public static async Task<PrinterBindResult> BindAsync(
        AgentSettings settings,
        string configFilePath,
        HttpAgentClient client,
        string? rawCode,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (!TryNormalizeCode(rawCode, out var code))
        {
            return PrinterBindResult.Fail(
                PrinterBindFailure.InvalidCode, "绑定码格式错误：必须是 6 位数字。", 2);
        }

        if (string.IsNullOrWhiteSpace(settings.ServerBaseUrl))
        {
            return PrinterBindResult.Fail(
                PrinterBindFailure.MissingServerUrl,
                "缺少 ServerBaseUrl。请先配置 agent.json，或使用 --server https://...。", 2);
        }

        var printers = PrinterDiscovery.GetInstalledPrinters(logger);
        if (printers.Count == 0)
        {
            return PrinterBindResult.Fail(
                PrinterBindFailure.NoPrinters,
                "未发现本机打印机。请确认已安装打印机驱动且 Windows 打印服务正常。", 2);
        }

        var serverBaseUrl = settings.ServerBaseUrl.Trim();
        var machineName = string.IsNullOrWhiteSpace(settings.MachineName)
            ? Environment.MachineName
            : settings.MachineName.Trim();

        var deviceKey = DeviceKeyStore.Load(settings, configFilePath, logger);
        var autoRegister = string.IsNullOrWhiteSpace(deviceKey);

        try
        {
            if (autoRegister)
                return await AutoRegisterAndBindAsync(
                    settings, configFilePath, client, serverBaseUrl, code, machineName,
                    printers, logger, ct).ConfigureAwait(false);

            logger.Info($"[PrinterBind] 开始绑定打印机: 本机 {printers.Count} 台, 服务端 {serverBaseUrl}");
            var response = await client
                .BindPrintersByCodeAsync(serverBaseUrl, deviceKey!, code, printers, ct)
                .ConfigureAwait(false);

            var bindings = response.Bindings ?? new List<PrinterBinding>();
            logger.Info($"[PrinterBind] 绑定成功: {bindings.Count} 台");

            return new PrinterBindResult
            {
                Failure = PrinterBindFailure.None,
                Message = $"已绑定 {bindings.Count} 台打印机",
                Bindings = bindings,
                ExitCode = 0
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return PrinterBindResult.Fail(PrinterBindFailure.Canceled, "绑定已取消。", 130);
        }
        catch (TaskCanceledException ex)
        {
            logger.Warn($"[PrinterBind] 请求超时: {ex.Message}");
            return PrinterBindResult.Fail(
                PrinterBindFailure.Timeout,
                autoRegister
                    ? "自动注册请求超时，请检查网络后重试。"
                    : "绑定请求超时，请检查网络后重试。",
                3);
        }
        catch (HttpRequestException ex)
        {
            logger.Warn($"[PrinterBind] 无法连接服务端 {serverBaseUrl}: {ex.Message}");
            return PrinterBindResult.Fail(
                PrinterBindFailure.Network, $"无法连接服务端 {serverBaseUrl}: {ex.Message}", 3);
        }
        catch (AgentApiException ex)
        {
            return MapApiFailure(ex, autoRegister, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, autoRegister ? "[PrinterBind] 自动注册绑定异常" : "[PrinterBind] 绑定异常");
            return PrinterBindResult.Fail(
                PrinterBindFailure.Unexpected,
                autoRegister ? $"自动注册并绑定异常: {ex.Message}" : $"绑定异常: {ex.Message}",
                1);
        }
    }

    /// <summary>无 deviceKey：用网页绑定码一次性完成注册、保存密钥并写入打印机绑定。</summary>
    private static async Task<PrinterBindResult> AutoRegisterAndBindAsync(
        AgentSettings settings,
        string configFilePath,
        HttpAgentClient client,
        string serverBaseUrl,
        string code,
        string machineName,
        IReadOnlyList<AgentPrinterInfo> printers,
        ILoggerService logger,
        CancellationToken ct)
    {
        logger.Info($"[PrinterBind] 未找到设备密钥，按绑定码自动注册: 本机 {printers.Count} 台, 服务端 {serverBaseUrl}");

        var response = await client
            .RegisterByCodeAsync(serverBaseUrl, code, machineName, AgentRuntime.Version, printers, ct)
            .ConfigureAwait(false);

        var bindings = response.Bindings ?? new List<PrinterBinding>();

        settings.DeviceId = response.DeviceId;
        if (response.HeartbeatSeconds > 0)
            settings.HeartbeatSeconds = Math.Clamp(response.HeartbeatSeconds, 5, 3600);
        settings.MachineName = machineName;

        try
        {
            // DeviceKeyStore.Save 内部按现有 DPAPI 方式写入 deviceKeyProtected，
            // 并调用 AgentConfigStore.Save 落盘（含上面的 deviceId / machineName / heartbeatSeconds）。
            DeviceKeyStore.Save(settings, configFilePath, response.DeviceKey, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[PrinterBind] 自动注册成功但保存配置失败");
            return PrinterBindResult.Fail(
                PrinterBindFailure.Unexpected,
                $"已自动注册（deviceId={response.DeviceId}），但保存设备密钥/配置失败：{ex.Message}。" +
                $"绑定码可能已使用，请检查 {configFilePath} 的写入权限后，在网页端重新生成绑定码重试。",
                1);
        }

        logger.Info($"[PrinterBind] 自动注册并绑定成功: DeviceId={response.DeviceId}, 打印机={bindings.Count} 台");
        return new PrinterBindResult
        {
            Failure = PrinterBindFailure.None,
            Message = $"已自动注册并绑定 {bindings.Count} 台打印机",
            Bindings = bindings,
            ExitCode = 0,
            AutoRegistered = true
        };
    }

    private static PrinterBindResult MapApiFailure(
        AgentApiException ex,
        bool autoRegister,
        ILoggerService logger)
    {
        var trace = string.IsNullOrWhiteSpace(ex.TraceId) ? string.Empty : $"，traceId={ex.TraceId}";
        var status = ex.StatusCode == 0 ? "-" : ex.StatusCode.ToString();

        if (ex.IsUnauthorized)
        {
            logger.Warn($"[PrinterBind] 未授权 [{ex.Code}] HTTP {status}{trace}: {ex.Message}");
            return PrinterBindResult.Fail(
                PrinterBindFailure.Unauthorized,
                autoRegister
                    ? $"服务端拒绝自动注册（HTTP {status}）：{ex.Message}{trace}。请确认服务端已启用绑定码自动注册。"
                    : $"设备未注册或已失效（HTTP {status}）：{ex.Message}{trace}。" +
                      "请确认服务端设备状态；必要时清理本地配置后重新绑定。",
                3);
        }

        if (ex.StatusCode == 429)
        {
            logger.Warn($"[PrinterBind] 触发频率限制 [{ex.Code}] HTTP 429{trace}: {ex.Message}");
            return PrinterBindResult.Fail(
                PrinterBindFailure.RateLimited,
                $"服务端请求过于频繁（HTTP 429）{trace}，请稍后再试。",
                3);
        }

        if (ex.StatusCode == 404)
        {
            logger.Warn($"[PrinterBind] 绑定码不可用 [{ex.Code}] HTTP 404{trace}: {ex.Message}");
            return PrinterBindResult.Fail(
                autoRegister ? PrinterBindFailure.CodeUsed : PrinterBindFailure.NotFound,
                autoRegister
                    ? $"绑定码不存在或已被使用：{ex.Message}{trace}。请在网页端重新生成绑定码后重试。"
                    : $"绑定码不存在或已过期：{ex.Message}{trace}。请在网页端重新生成绑定码后重试。",
                3);
        }

        if (autoRegister && ex.StatusCode == 400)
        {
            if (ex.Message.Contains("过期", StringComparison.Ordinal))
            {
                logger.Warn($"[PrinterBind] 绑定码已过期 [{ex.Code}] HTTP 400{trace}: {ex.Message}");
                return PrinterBindResult.Fail(
                    PrinterBindFailure.CodeExpired,
                    $"绑定码已过期：{ex.Message}{trace}。请在网页端重新生成绑定码后重试。",
                    3);
            }

            logger.Warn($"[PrinterBind] 自动注册参数被拒绝 [{ex.Code}] HTTP 400{trace}: {ex.Message}");
            return PrinterBindResult.Fail(
                PrinterBindFailure.InvalidCode,
                $"自动注册失败：{ex.Message}{trace}。请确认使用的是网页端最新生成的 6 位绑定码。",
                3);
        }

        logger.Warn($"[PrinterBind] 请求失败 [{ex.Code}] HTTP {status}{trace}: {ex.Message}");
        return PrinterBindResult.Fail(
            PrinterBindFailure.ApiError,
            $"{(autoRegister ? "自动注册" : "绑定")}失败 [{ex.Code}] HTTP {status}{trace}: {ex.Message}",
            ex.StatusCode == 0 ? 2 : 3);
    }

    /// <summary>命令行入口：无托盘环境也能绑定，成功后打印服务端返回的打印机名称。</summary>
    public static async Task<int> RunCliAsync(
        AgentSettings settings,
        string configFilePath,
        HttpAgentClient client,
        string? rawCode,
        ILoggerService logger,
        CancellationToken ct)
    {
        AgentConsole.Info($"服务端: {settings.ServerBaseUrl}");

        var result = await BindAsync(settings, configFilePath, client, rawCode, logger, ct)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            AgentConsole.Error(result.Message);
            return result.ExitCode;
        }

        if (result.Bindings.Count == 0)
        {
            AgentConsole.Warn(result.Message + "（服务端未返回绑定明细，请刷新页面确认）");
            return 0;
        }

        AgentConsole.Success($"{result.Message}：");
        foreach (var binding in result.Bindings)
        {
            var flags = (binding.IsDefault ? " [默认]" : string.Empty)
                        + (binding.Online ? " [在线]" : " [离线]");
            var device = string.IsNullOrWhiteSpace(binding.DeviceName)
                ? string.Empty
                : $"（设备: {binding.DeviceName}）";
            Console.WriteLine($"  {binding.PrinterName}{flags}{device}");
        }

        return 0;
    }
}
