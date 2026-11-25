using System.Net.Http.Json;
using NewCosmos.Agent.Models;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 打印机绑定 HTTP 调用（partial <see cref="HttpAgentClient"/>）：
/// 复用注册/心跳的 HttpClient、设备密钥请求头与统一错误处理。
/// </summary>
public sealed partial class HttpAgentClient
{
    private const string PrinterBindByCodePath = "/api/printers/bind-by-code";
    private const string RegisterByCodePath = "/api/agent/register-by-code";

    /// <summary>
    /// 未注册设备凭 6 位网页绑定码自动注册并绑定本机打印机。
    /// 匿名端点：不发送 X-NC-Device-Key，用户身份只由服务端从绑定码解析。
    /// </summary>
    public async Task<AgentRegisterByCodeResponse> RegisterByCodeAsync(
        string serverBaseUrl,
        string code,
        string machineName,
        string agentVersion,
        IReadOnlyList<AgentPrinterInfo> printers,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new AgentApiException(0, "AGENT_BIND_CODE_MISSING", "缺少绑定码");

        var request = new AgentRegisterByCodeRequest
        {
            Code = code.Trim(),
            MachineName = string.IsNullOrWhiteSpace(machineName)
                ? Environment.MachineName
                : machineName.Trim(),
            AgentVersion = agentVersion ?? string.Empty,
            Printers = printers is { Count: > 0 }
                ? printers.ToList()
                : new List<AgentPrinterInfo>()
        };

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            CombineUrl(serverBaseUrl, RegisterByCodePath))
        {
            Content = CreateJsonContent(request)
        };

        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        var result = await response.Content
                         .ReadFromJsonAsync<AgentRegisterByCodeResponse>(JsonOptions, ct)
                         .ConfigureAwait(false)
                     ?? throw new AgentApiException(
                         (int)response.StatusCode, "AGENT_REGISTER_BY_CODE_EMPTY", "自动注册成功但响应体为空");

        if (string.IsNullOrWhiteSpace(result.DeviceKey))
            throw new AgentApiException(
                (int)response.StatusCode, "AGENT_REGISTER_BY_CODE_NO_KEY", "自动注册响应缺少 deviceKey");

        return result;
    }

    /// <summary>用 6 位绑定码把本机打印机绑定到当前设备。</summary>
    public async Task<PrinterBindByCodeResponse> BindPrintersByCodeAsync(
        string serverBaseUrl,
        string deviceKey,
        string code,
        IReadOnlyList<AgentPrinterInfo> printers,
        CancellationToken ct = default)
    {
        EnsureDeviceKey(deviceKey);

        var request = new PrinterBindByCodeRequest
        {
            Code = code,
            Printers = printers is { Count: > 0 }
                ? printers.ToList()
                : new List<AgentPrinterInfo>()
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, CombineUrl(serverBaseUrl, PrinterBindByCodePath))
        {
            Content = CreateJsonContent(request)
        };
        message.Headers.Add("X-NC-Device-Key", deviceKey);

        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        return await response.Content
                   .ReadFromJsonAsync<PrinterBindByCodeResponse>(JsonOptions, ct)
                   .ConfigureAwait(false)
               ?? throw new AgentApiException(
                   (int)response.StatusCode, "PRINTER_BIND_EMPTY", "绑定成功但响应体为空");
    }
}
