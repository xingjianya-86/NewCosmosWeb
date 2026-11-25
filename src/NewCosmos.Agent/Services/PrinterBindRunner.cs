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
    MissingDeviceKey,
    NoPrinters,
    Unauthorized,
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

    public bool Success => Failure == PrinterBindFailure.None;

    public static PrinterBindResult Fail(PrinterBindFailure failure, string message, int exitCode) =>
        new() { Failure = failure, Message = message, ExitCode = exitCode };
}

/// <summary>
/// 「绑定本机打印机」共用逻辑：校验 6 位码 → 读取 deviceKey → 枚举本机打印机 →
/// POST /api/printers/bind-by-code。托盘与命令行都走这里，避免两份实现。
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

        var deviceKey = DeviceKeyStore.Load(settings, configFilePath, logger);
        if (string.IsNullOrWhiteSpace(deviceKey))
        {
            return PrinterBindResult.Fail(
                PrinterBindFailure.MissingDeviceKey,
                $"未找到设备密钥（{settings.DeviceKeyFile}）。请先运行: NewCosmos.Agent.exe --register", 2);
        }

        var printers = PrinterDiscovery.GetInstalledPrinters(logger);
        if (printers.Count == 0)
        {
            return PrinterBindResult.Fail(
                PrinterBindFailure.NoPrinters,
                "未发现本机打印机。请确认已安装打印机驱动且 Windows 打印服务正常。", 2);
        }

        try
        {
            logger.Info($"[PrinterBind] 开始绑定打印机: 本机 {printers.Count} 台, 服务端 {settings.ServerBaseUrl}");
            var response = await client
                .BindPrintersByCodeAsync(settings.ServerBaseUrl, deviceKey, code, printers, ct)
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
            logger.Warn($"[PrinterBind] 绑定请求超时: {ex.Message}");
            return PrinterBindResult.Fail(
                PrinterBindFailure.Timeout, "绑定请求超时，请检查网络后重试。", 3);
        }
        catch (HttpRequestException ex)
        {
            logger.Warn($"[PrinterBind] 无法连接服务端 {settings.ServerBaseUrl}: {ex.Message}");
            return PrinterBindResult.Fail(
                PrinterBindFailure.Network, $"无法连接服务端 {settings.ServerBaseUrl}: {ex.Message}", 3);
        }
        catch (AgentApiException ex)
        {
            var trace = string.IsNullOrWhiteSpace(ex.TraceId) ? string.Empty : $"，traceId={ex.TraceId}";

            if (ex.IsUnauthorized)
            {
                logger.Warn($"[PrinterBind] 设备未授权 [{ex.Code}] HTTP {ex.StatusCode}{trace}: {ex.Message}");
                return PrinterBindResult.Fail(
                    PrinterBindFailure.Unauthorized,
                    $"设备未注册或已失效（HTTP {ex.StatusCode}）：{ex.Message}{trace}。请确认服务端设备状态，必要时重新运行 --register。",
                    3);
            }

            if (ex.StatusCode == 404)
            {
                logger.Warn($"[PrinterBind] 绑定码无效 [{ex.Code}] HTTP 404{trace}: {ex.Message}");
                return PrinterBindResult.Fail(
                    PrinterBindFailure.NotFound,
                    $"绑定码不存在或已过期：{ex.Message}{trace}。请在服务端重新生成绑定码后重试。",
                    3);
            }

            logger.Warn($"[PrinterBind] 绑定失败 [{ex.Code}] HTTP {ex.StatusCode}{trace}: {ex.Message}");
            return PrinterBindResult.Fail(
                PrinterBindFailure.ApiError,
                $"绑定失败 [{ex.Code}] HTTP {(ex.StatusCode == 0 ? "-" : ex.StatusCode)}{trace}: {ex.Message}",
                ex.StatusCode == 0 ? 2 : 3);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[PrinterBind] 绑定异常");
            return PrinterBindResult.Fail(PrinterBindFailure.Unexpected, $"绑定异常: {ex.Message}", 1);
        }
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
