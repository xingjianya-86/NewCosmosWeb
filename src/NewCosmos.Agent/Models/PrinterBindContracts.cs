using System.Text.Json.Serialization;

namespace NewCosmos.Agent.Models;

/// <summary>POST /api/printers/bind-by-code 请求体。</summary>
public sealed class PrinterBindByCodeRequest
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("printers")]
    public List<AgentPrinterInfo> Printers { get; set; } = new();
}

/// <summary>POST /api/printers/bind-by-code 响应体。</summary>
public sealed class PrinterBindByCodeResponse
{
    [JsonPropertyName("bindings")]
    public List<PrinterBinding> Bindings { get; set; } = new();
}

/// <summary>
/// POST /api/agent/register-by-code 请求体（匿名）：未注册 Agent 仅凭网页绑定码
/// 完成设备注册并绑定本机打印机。userId 由服务端从绑定码解析，请求体不携带。
/// </summary>
public sealed class AgentRegisterByCodeRequest
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("machineName")]
    public string MachineName { get; set; } = string.Empty;

    [JsonPropertyName("agentVersion")]
    public string AgentVersion { get; set; } = string.Empty;

    [JsonPropertyName("printers")]
    public List<AgentPrinterInfo> Printers { get; set; } = new();
}

/// <summary>POST /api/agent/register-by-code 响应体：新设备密钥 + 心跳周期 + 已写入的绑定。</summary>
public sealed class AgentRegisterByCodeResponse
{
    [JsonPropertyName("deviceId")]
    public int DeviceId { get; set; }

    [JsonPropertyName("deviceKey")]
    public string DeviceKey { get; set; } = string.Empty;

    [JsonPropertyName("heartbeatSeconds")]
    public int HeartbeatSeconds { get; set; }

    [JsonPropertyName("bindings")]
    public List<PrinterBinding> Bindings { get; set; } = new();
}

/// <summary>服务端返回的一条打印机绑定记录。</summary>
public sealed class PrinterBinding
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("deviceId")]
    public int DeviceId { get; set; }

    [JsonPropertyName("deviceName")]
    public string DeviceName { get; set; } = string.Empty;

    [JsonPropertyName("printerName")]
    public string PrinterName { get; set; } = string.Empty;

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("online")]
    public bool Online { get; set; }
}
