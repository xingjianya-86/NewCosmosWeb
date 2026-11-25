using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NewCosmos.Agent.Models;

namespace NewCosmos.Agent.Services;

/// <summary>服务端 P0 接口：设备注册、心跳。</summary>
public interface IAgentClient
{
    Task<AgentRegisterResponse> RegisterAsync(
        string serverBaseUrl,
        string registerToken,
        AgentRegisterRequest request,
        CancellationToken ct = default);

    Task HeartbeatAsync(
        string serverBaseUrl,
        string deviceKey,
        AgentHeartbeatRequest request,
        CancellationToken ct = default);

}

/// <summary>
/// 基于 HttpClient 的最小实现：注册/心跳 + 任务队列认领/回写。
/// 任务接口在 <c>AgentJobClient.cs</c> 的 partial 中实现，复用同一个 HttpClient 与错误处理。
/// </summary>
public sealed partial class HttpAgentClient : IAgentClient, IAgentJobClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;

    public HttpAgentClient(string agentVersion)
    {
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"NewCosmos.Agent/{agentVersion}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<AgentRegisterResponse> RegisterAsync(
        string serverBaseUrl,
        string registerToken,
        AgentRegisterRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(registerToken))
            throw new AgentApiException(0, "AGENT_REGISTER_TOKEN_MISSING", "缺少注册令牌（RegisterToken / NC_AGENT_REGISTER_TOKEN / --register-token）");

        using var message = new HttpRequestMessage(HttpMethod.Post, CombineUrl(serverBaseUrl, "/api/agent/register"))
        {
            Content = CreateJsonContent(request)
        };
        message.Headers.Add("X-NC-Agent-Register", registerToken);

        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        var result = await response.Content.ReadFromJsonAsync<AgentRegisterResponse>(JsonOptions, ct).ConfigureAwait(false)
            ?? throw new AgentApiException((int)response.StatusCode, "AGENT_REGISTER_EMPTY", "注册成功但响应体为空");

        if (string.IsNullOrWhiteSpace(result.DeviceKey))
            throw new AgentApiException((int)response.StatusCode, "AGENT_REGISTER_NO_KEY", "注册响应缺少 deviceKey");

        return result;
    }

    public async Task HeartbeatAsync(
        string serverBaseUrl,
        string deviceKey,
        AgentHeartbeatRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            throw new AgentApiException(0, "AGENT_DEVICE_KEY_MISSING", "缺少设备密钥，请先用网页绑定码运行 --bind-code 自动注册本机");

        using var message = new HttpRequestMessage(HttpMethod.Post, CombineUrl(serverBaseUrl, "/api/agent/heartbeat"))
        {
            Content = CreateJsonContent(request)
        };
        message.Headers.Add("X-NC-Device-Key", deviceKey);

        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var code = response.StatusCode == HttpStatusCode.Unauthorized ? "UNAUTHORIZED" : "HTTP_ERROR";
        var message = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase ?? "请求失败" : body;
        string? traceId = null;

        try
        {
            var error = JsonSerializer.Deserialize<AgentApiErrorBody>(body, JsonOptions);
            if (error != null && !string.IsNullOrWhiteSpace(error.Code))
            {
                code = error.Code;
                message = string.IsNullOrWhiteSpace(error.Message) ? body : error.Message;
                traceId = error.TraceId;
            }
        }
        catch (JsonException)
        {
            // 非契约错误体，保留原始文本
        }

        throw new AgentApiException((int)response.StatusCode, code, message, traceId);
    }

    /// <summary>显式 UTF-8 JSON + Content-Length，避免部分代理/服务端对 chunked 请求体不兼容。</summary>
    private static StringContent CreateJsonContent<T>(T value) =>
        new(JsonSerializer.Serialize(value, RequestJsonOptions), Encoding.UTF8, "application/json");

    private static string CombineUrl(string baseUrl, string path)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new AgentApiException(0, "AGENT_SERVER_URL_MISSING", "缺少 ServerBaseUrl 配置");

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new AgentApiException(0, "AGENT_SERVER_URL_INVALID", $"ServerBaseUrl 不是有效的 http(s) 地址: {baseUrl}");
        }

        return baseUrl.TrimEnd('/') + path;
    }

    public void Dispose() => _http.Dispose();
}
