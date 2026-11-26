namespace NewCosmos.Server.Contracts;

/// <summary>当前登录用户修改密码。</summary>
public sealed record ChangePasswordRequest(string? OldPassword, string? NewPassword);

/// <summary>会话策略（只读展示）。</summary>
public sealed record SessionPolicyDto(
    int Mode,
    int MaxDesktop,
    int MaxMobile,
    int HeartbeatDesktopSeconds,
    int HeartbeatMobileSeconds,
    int OnlineWindowSeconds,
    int AbsoluteDays,
    int GraceSeconds);
