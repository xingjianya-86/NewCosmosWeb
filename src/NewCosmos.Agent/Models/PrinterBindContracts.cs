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
