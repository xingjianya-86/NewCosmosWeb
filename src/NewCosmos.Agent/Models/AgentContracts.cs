using System.Text.Json.Serialization;

namespace NewCosmos.Agent.Models;

/// <summary>Agent 上报的打印机条目（API_CONTRACT §3）。</summary>
public sealed class AgentPrinterInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }
}

/// <summary>POST /api/agent/register 请求体。</summary>
public sealed class AgentRegisterRequest
{
    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = string.Empty;

    [JsonPropertyName("agentVersion")]
    public string AgentVersion { get; set; } = string.Empty;

    [JsonPropertyName("printers")]
    public List<AgentPrinterInfo> Printers { get; set; } = new();
}

/// <summary>POST /api/agent/register 响应体（deviceKey 只返回一次）。</summary>
public sealed class AgentRegisterResponse
{
    [JsonPropertyName("deviceId")]
    public int DeviceId { get; set; }

    [JsonPropertyName("deviceKey")]
    public string DeviceKey { get; set; } = string.Empty;

    [JsonPropertyName("heartbeatSeconds")]
    public int HeartbeatSeconds { get; set; }
}

/// <summary>
/// POST /api/agent/heartbeat 请求体。
/// 契约字段为 agentVersion/printers；machineName 为附加诊断字段（服务端可忽略）。
/// </summary>
public sealed class AgentHeartbeatRequest
{
    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = string.Empty;

    [JsonPropertyName("agentVersion")]
    public string AgentVersion { get; set; } = string.Empty;

    [JsonPropertyName("printers")]
    public List<AgentPrinterInfo> Printers { get; set; } = new();
}

/// <summary>服务端统一错误体（API_CONTRACT §0）。</summary>
public sealed class AgentApiErrorBody
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }
}
