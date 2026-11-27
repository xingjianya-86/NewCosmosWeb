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

/// <summary>
/// 可选设备：只暴露公开 ID/机器名/上报打印机，不暴露 deviceKey。
/// <c>Capabilities</c> 为该设备心跳上报的本机操作能力码（font / zerotier …），
/// 老 Agent 未上报时为空数组，前端据此判断是否展示字体安装 / ZeroTier 入口。
/// </summary>
public sealed record PrinterDeviceDto(
    long Id,
    string MachineName,
    bool Online,
    DateTime? LastSeenAt,
    IReadOnlyList<string> Printers,
    IReadOnlyList<string> Capabilities);

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

#region 本机操作（LocalOp）

/// <summary>POST /api/devices/{deviceId}/local-ops 请求体。</summary>
public sealed record LocalOpRequest(string? Op, object? Args);

/// <summary>POST /api/devices/{deviceId}/local-ops 响应：入队后的任务 ID（结果查询走 GET /api/print/jobs）。</summary>
public sealed record LocalOpCreatedResponse(long JobId, string JobNo);

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

/// <summary>
/// POST /api/agent/register-by-code 请求体：未注册 Agent 仅凭网页绑定码完成注册并绑定打印机。
/// userId 由绑定码在服务端解析，请求体不接受 userId。
/// </summary>
public sealed record AgentRegisterByCodeRequest(
    string? Code,
    string? MachineName,
    string? AgentVersion,
    List<AgentPrinterReport>? Printers);

/// <summary>POST /api/agent/register-by-code 响应：新设备密钥 + 心跳周期 + 本次写入的绑定。</summary>
public sealed record AgentRegisterByCodeResponse(
    long DeviceId,
    string DeviceKey,
    int HeartbeatSeconds,
    IReadOnlyList<PrinterBindingDto> Bindings);

/// <summary>
/// POST /api/agent/heartbeat 请求体。
/// <c>Capabilities</c> 为新增的本机操作能力码列表（font / zerotier …）：
/// 老 Agent 不传，服务端按空处理并保持列默认值。
/// </summary>
public sealed record AgentHeartbeatRequest(
    string? AgentVersion,
    List<AgentPrinterReport>? Printers,
    List<string>? Capabilities = null);

#endregion

#region 系统

public sealed record HealthResponse(string Status, DateTime Time);

public sealed record InfoResponse(string Service, string Version, string Database, string Mode);

public sealed record ApiErrorResponse(string Code, string Message, string TraceId);

#endregion
