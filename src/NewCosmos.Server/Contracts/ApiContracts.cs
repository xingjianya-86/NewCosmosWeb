namespace NewCosmos.Server.Contracts;

#region 认证

public sealed record LoginRequest(
    string? Username,
    string? Password,
    string? DeviceId,
    string? DeviceKind,
    string? DeviceName,
    string? AppVersion);

public sealed record LoginResponse(
    string SessionId,
    DateTime ExpiresAt,
    UserDto User);

public sealed record UserDto(
    int Id,
    string Username,
    string? FullName,
    string? Phone,
    int? OrganizationId,
    string? OrganizationName);

public sealed record HeartbeatResponse(bool Active, DateTime ExpiresAt);

#endregion

#region 打印机

public sealed record BindPrinterRequest(
    long? DeviceId,
    string? PrinterName,
    bool? IsDefault);

/// <summary>POST /api/printers/bind-code 响应：6 位一次性绑定码 + 过期时间。</summary>
public sealed record PrinterBindCodeResponse(string Code, DateTime ExpiresAt);

/// <summary>
/// POST /api/printers/bind-by-code 请求体（Agent 用设备密钥调用）。
/// printers 省略或为空时使用设备最近一次上报的 printers_json。
/// </summary>
public sealed record BindPrinterByCodeRequest(
    string? Code,
    List<AgentPrinterReport>? Printers);

/// <summary>POST /api/printers/bind-by-code 响应：本次写入/更新的绑定。</summary>
public sealed record PrinterBindingsResponse(IReadOnlyList<PrinterBindingDto> Bindings);

/// <summary>可选设备：只暴露公开 ID/机器名/上报打印机，不暴露 deviceKey。</summary>
public sealed record PrinterDeviceDto(
    long Id,
    string MachineName,
    bool Online,
    DateTime? LastSeenAt,
    IReadOnlyList<string> Printers);

/// <summary>打印机绑定输出模型。属性名保持 PascalCase，Core 的反射映射按列名/蛇形列名匹配。</summary>
public sealed class PrinterBindingDto
{
    public long Id { get; set; }
    public long DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string PrinterName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public bool Online { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

#endregion

#region Agent

public sealed record AgentPrinterReport(string? Name, bool IsDefault);

public sealed record AgentRegisterRequest(
    string? MachineName,
    string? AgentVersion,
    List<AgentPrinterReport>? Printers);

public sealed record AgentRegisterResponse(
    long DeviceId,
    string DeviceKey,
    int HeartbeatSeconds);

public sealed record AgentHeartbeatRequest(
    string? AgentVersion,
    List<AgentPrinterReport>? Printers);

#endregion

#region 系统

public sealed record HealthResponse(string Status, DateTime Time);

public sealed record InfoResponse(string Service, string Version, string Database, string Mode);

public sealed record ApiErrorResponse(string Code, string Message, string TraceId);

#endregion
