using System.Text.Json;

namespace NewCosmos.Server.Services;

/// <summary>
/// Agent 心跳上报的本机操作能力码（font / zerotier …）的校验、序列化与读取。
/// 能力码只用于前端展示「该设备是否支持某类本机操作」，不做权限判定，
/// 因此只做长度/条数约束与去重，不白名单枚举。
/// </summary>
public static class AgentCapabilityNormalizer
{
    /// <summary>单次心跳最多上报的能力码条数。</summary>
    public const int MaxCapabilities = 32;

    /// <summary>单个能力码最大长度（与 printers_json 的名称约束同一量级即可）。</summary>
    public const int MaxCodeLength = 64;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 清洗上报的能力码：去空白、去空值、去重（忽略大小写）、截断超长、限制条数。
    /// null（老 Agent 未传）返回空列表，调用方按「保持列默认值」处理。
    /// </summary>
    public static List<string> Normalize(IReadOnlyCollection<string>? source)
    {
        var result = new List<string>();
        if (source is null || source.Count == 0)
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in source)
        {
            var code = raw?.Trim();
            if (string.IsNullOrEmpty(code))
            {
                continue;
            }

            if (code.Length > MaxCodeLength)
            {
                code = code[..MaxCodeLength];
            }

            if (!seen.Add(code))
            {
                continue;
            }

            result.Add(code);
            if (result.Count >= MaxCapabilities)
            {
                break;
            }
        }

        return result;
    }

    public static string Serialize(IReadOnlyCollection<string> capabilities)
        => JsonSerializer.Serialize(capabilities, JsonOptions);

    /// <summary>
    /// 读取已落库的能力码数组。列默认值是 JSONB 数组，但历史/手工数据可能写成对象或标量，
    /// 这里对非数组一律按空列表处理，不抛异常。
    /// </summary>
    public static List<string> Read(string? capabilitiesJson)
    {
        if (string.IsNullOrWhiteSpace(capabilitiesJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(capabilitiesJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new List<string>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var code = item.GetString()?.Trim();
                if (!string.IsNullOrEmpty(code))
                {
                    result.Add(code);
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
