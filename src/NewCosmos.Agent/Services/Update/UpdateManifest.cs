using System.Text.Json;
using NewCosmos.Models.Results;

namespace NewCosmos.Agent.Services.Update;

/// <summary>
/// Agent 在线更新清单：{ version, url, sha256, notes, publishedAt }。
/// 由 <c>deploy/scripts/write-update-manifest.ps1</c> 生成，发布到
/// <c>downloads/windows/agent-latest.json</c>（与 Shell 的 latest.json 分开，互不影响）。
/// 清单来自网络，解析失败属于正常业务分支，因此一律返回 <see cref="Result{T}"/> 而不是抛异常。
/// </summary>
public sealed record AgentUpdateManifest
{
    public const string ErrorCodeInvalidJson = "AGENT_UPDATE_INVALID_JSON";
    public const string ErrorCodeMissingField = "AGENT_UPDATE_MISSING_FIELD";
    public const string ErrorCodeInvalidUrl = "AGENT_UPDATE_INVALID_URL";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // 发布脚本用 PowerShell ConvertTo-Json 输出小写字段名；大小写不敏感便于手工编辑清单。
        PropertyNameCaseInsensitive = true
    };

    public string Version { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public string PublishedAt { get; init; } = string.Empty;

    /// <summary>解析清单；非法 JSON / 缺 version|url|sha256 / url 非 http(s) 绝对地址 → 失败。</summary>
    public static Result<AgentUpdateManifest> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Result.Failure<AgentUpdateManifest>(ErrorCodeInvalidJson, "更新清单为空");

        AgentUpdateManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<AgentUpdateManifest>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Result.Failure<AgentUpdateManifest>(ErrorCodeInvalidJson, $"更新清单不是合法 JSON: {ex.Message}");
        }

        if (manifest is null)
            return Result.Failure<AgentUpdateManifest>(ErrorCodeInvalidJson, "更新清单为空");

        // notes/publishedAt 是可选字段，但显式 null 会覆盖默认值，这里统一兜底成空串。
        manifest = manifest with
        {
            Version = manifest.Version?.Trim() ?? string.Empty,
            Url = manifest.Url?.Trim() ?? string.Empty,
            Sha256 = manifest.Sha256?.Trim() ?? string.Empty,
            Notes = manifest.Notes?.Trim() ?? string.Empty,
            PublishedAt = manifest.PublishedAt?.Trim() ?? string.Empty
        };

        if (manifest.Version.Length == 0)
            return Result.Failure<AgentUpdateManifest>(ErrorCodeMissingField, "更新清单缺少 version 字段");
        if (manifest.Url.Length == 0)
            return Result.Failure<AgentUpdateManifest>(ErrorCodeMissingField, "更新清单缺少 url 字段");
        if (manifest.Sha256.Length == 0)
            return Result.Failure<AgentUpdateManifest>(ErrorCodeMissingField, "更新清单缺少 sha256 字段");

        // 只接受 http/https 绝对地址：file:/// 之类会把本地文件当安装包执行。
        if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Result.Failure<AgentUpdateManifest>(ErrorCodeInvalidUrl, "更新清单 url 不是有效的 http/https 绝对地址");
        }

        return Result.Success(manifest);
    }

    /// <summary>
    /// 版本比较：按 <c>.</c> 分段，段内双方都能解析成 long 就数值比，否则按序字符串比；
    /// 段数不等时缺失段补 0（<c>2.0.1 &gt; 2.0</c>）；null/空串视为最小（任何非空版本都比它大）。
    /// 返回值语义同 <see cref="string.CompareOrdinal(string, string)"/>：负/零/正。
    /// </summary>
    public static int CompareVersion(string? candidate, string? current)
    {
        var left = Split(candidate);
        var right = Split(current);
        var count = Math.Max(left.Length, right.Length);

        for (var i = 0; i < count; i++)
        {
            // 段数不等补 0：把 "2.0" 当作 "2.0.0" 与 "2.0.1" 比，避免缺段反而更大的错序。
            var l = i < left.Length ? left[i] : "0";
            var r = i < right.Length ? right[i] : "0";

            int compare;
            if (long.TryParse(l, out var ln) && long.TryParse(r, out var rn))
                compare = ln.CompareTo(rn);
            else
                compare = string.CompareOrdinal(l, r);

            if (compare != 0)
                return compare;
        }

        return 0;
    }

    /// <summary>清单版本是否比当前版本新（<see cref="CompareVersion"/> &gt; 0 的语义封装）。</summary>
    public bool IsNewerThan(string? currentVersion) => CompareVersion(Version, currentVersion) > 0;

    private static string[] Split(string? value)
    {
        // null/空 → 单个空串段；它比任何数字段都小（long 解析失败走字符串比较）。
        if (string.IsNullOrWhiteSpace(value))
            return new[] { string.Empty };

        return value.Trim().Split('.', StringSplitOptions.None)
            .Select(segment => segment.Trim())
            .ToArray();
    }
}
