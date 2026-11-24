using NewCosmos.Constants;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Domain.UserManagement;

namespace NewCosmos.Server.Infrastructure;

public static class AuthHelpers
{
    public static UserDto ToUserDto(User user) => new(
        user.Id,
        user.Username,
        user.FullName,
        MaskPhone(user.Phone),
        user.OrganizationId,
        user.OrganizationName);

    /// <summary>契约示例为 138****0000；服务端不向前端下发完整手机号。</summary>
    public static string MaskPhone(string? phone)
    {
        var value = phone?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return string.Empty;
        }

        return value.Length >= 7
            ? string.Concat(value.AsSpan(0, 3), "****", value.AsSpan(value.Length - 4))
            : "****";
    }

    /// <summary>数据库 timestamp/timestamptz 映射后统一按 UTC 序列化，输出带 Z 的 ISO 8601。</summary>
    public static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static (int Status, string Code, string Message) MapSessionFailure(
        SessionValidationStatus status, string? endReason)
    {
        var message = SessionConstants.GetEndedMessage(endReason);

        return status switch
        {
            SessionValidationStatus.Replaced =>
                (StatusCodes.Status401Unauthorized, ApiErrorCodes.SessionReplaced, message),
            SessionValidationStatus.Expired =>
                (StatusCodes.Status401Unauthorized, ApiErrorCodes.SessionExpired, message),
            _ => (StatusCodes.Status401Unauthorized, ApiErrorCodes.AuthRequired, message)
        };
    }
}
