using NewCosmos.Server.Contracts;

namespace NewCosmos.Server.Infrastructure;

public static class HttpContextItemKeys
{
    public const string UserId = "nc.user_id";
    public const string SessionId = "nc.session_id";
    public const string SessionExpiresAt = "nc.session_expires_at";
    public const string DeviceId = "nc.device_id";
    public const string User = "nc.user";
}

public static class HttpContextExtensions
{
    public static int? GetUserId(this HttpContext context)
        => context.Items.TryGetValue(HttpContextItemKeys.UserId, out var value) && value is int userId
            ? userId
            : null;

    public static Guid? GetSessionId(this HttpContext context)
        => context.Items.TryGetValue(HttpContextItemKeys.SessionId, out var value) && value is Guid sessionId
            ? sessionId
            : null;

    public static UserDto? GetCurrentUser(this HttpContext context)
        => context.Items.TryGetValue(HttpContextItemKeys.User, out var value) ? value as UserDto : null;
}
