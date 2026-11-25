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
