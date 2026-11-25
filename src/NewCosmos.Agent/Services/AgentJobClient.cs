using System.Net.Http.Json;
using NewCosmos.Agent.Models;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 任务队列 HTTP 调用（partial <see cref="HttpAgentClient"/>）：
/// claim / payload / complete / fail，统一使用 X-NC-Device-Key 与既有错误处理。
/// </summary>
public sealed partial class HttpAgentClient
{
    private const string JobsPath = "/api/agent/jobs";

    public async Task<IReadOnlyList<AgentJob>> ClaimAsync(
        string serverBaseUrl,
        string deviceKey,
        int max,
        IReadOnlyList<AgentCachedTemplateRef>? cachedTemplates = null,
        CancellationToken ct = default)
    {
        EnsureDeviceKey(deviceKey);

        var request = new AgentJobClaimRequest
        {
            Max = Math.Clamp(max, 1, 10),
            CachedTemplates = NormalizeCachedTemplates(cachedTemplates)
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, CombineUrl(serverBaseUrl, JobsPath + "/claim"))
        {
            Content = CreateJsonContent(request)
        };
        message.Headers.Add("X-NC-Device-Key", deviceKey);

        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        var jobs = await response.Content
            .ReadFromJsonAsync<List<AgentJob>>(JsonOptions, ct)
            .ConfigureAwait(false);

        return jobs ?? new List<AgentJob>();
    }

    public async Task<AgentJobPayload> GetPayloadAsync(
        string serverBaseUrl,
        string deviceKey,
        long jobId,
        string? cachedTemplateHash = null,
        CancellationToken ct = default)
    {
        EnsureDeviceKey(deviceKey);

        var path = $"{JobsPath}/{jobId}/payload";
        var hash = NormalizeHash(cachedTemplateHash);
        if (hash is not null)
            path += $"?cachedTemplateHash={Uri.EscapeDataString(hash)}";

        using var message = new HttpRequestMessage(HttpMethod.Get, CombineUrl(serverBaseUrl, path));
        message.Headers.Add("X-NC-Device-Key", deviceKey);

        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);

        return await response.Content
                   .ReadFromJsonAsync<AgentJobPayload>(JsonOptions, ct)
                   .ConfigureAwait(false)
               ?? throw new AgentApiException((int)response.StatusCode, "AGENT_JOB_PAYLOAD_EMPTY", "任务负载为空");
    }

    public async Task CompleteAsync(
        string serverBaseUrl,
        string deviceKey,
        long jobId,
        string? message = null,
        CancellationToken ct = default)
    {
        EnsureDeviceKey(deviceKey);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CombineUrl(serverBaseUrl, $"{JobsPath}/{jobId}/complete"));
        request.Headers.Add("X-NC-Device-Key", deviceKey);

        // 降级打印等警告：作为可选 body 回写 error_message（任务仍为 Completed）。
        if (!string.IsNullOrWhiteSpace(message))
        {
            request.Content = CreateJsonContent(new AgentJobCompleteRequest
            {
                Message = Truncate(message, 1000)
            });
        }

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    public async Task FailAsync(
        string serverBaseUrl,
        string deviceKey,
        long jobId,
        string errorMessage,
        CancellationToken ct = default)
    {
        EnsureDeviceKey(deviceKey);

        var body = new AgentJobFailRequest
        {
            ErrorMessage = Truncate(errorMessage, 1000)
        };

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            CombineUrl(serverBaseUrl, $"{JobsPath}/{jobId}/fail"))
        {
            Content = CreateJsonContent(body)
        };
        message.Headers.Add("X-NC-Device-Key", deviceKey);

        using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
    }

    private static void EnsureDeviceKey(string deviceKey)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            throw new AgentApiException(0, "AGENT_DEVICE_KEY_MISSING", "缺少设备密钥，请先用网页绑定码运行 --bind-code 自动注册本机");
    }

    /// <summary>清洗 claim 上报的缓存清单：去重、去非法 hash、限制条数（老服务端忽略该字段）。</summary>
    private static List<AgentCachedTemplateRef> NormalizeCachedTemplates(
        IReadOnlyList<AgentCachedTemplateRef>? cachedTemplates)
    {
        var result = new List<AgentCachedTemplateRef>();
        if (cachedTemplates is not { Count: > 0 }) return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in cachedTemplates)
        {
            if (item == null || item.TemplateId <= 0) continue;

            var hash = NormalizeHash(item.TemplateHash);
            if (hash is null) continue;
            if (!seen.Add($"{item.TemplateId}:{hash}")) continue;

            result.Add(new AgentCachedTemplateRef { TemplateId = item.TemplateId, TemplateHash = hash });
            if (result.Count >= AgentJobClaimRequest.MaxCachedTemplates) break;
        }

        return result;
    }

    private static string? NormalizeHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return null;

        var trimmed = hash.Trim().ToLowerInvariant();
        if (trimmed.Length is < 16 or > 128) return null;

        foreach (var c in trimmed)
        {
            if (!char.IsAsciiHexDigitLower(c) && !char.IsAsciiDigit(c)) return null;
        }

        return trimmed;
    }

    internal static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "…";
    }
}
