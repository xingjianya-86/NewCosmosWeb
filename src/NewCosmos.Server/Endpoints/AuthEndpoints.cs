using System.Net;
using NewCosmos.Constants;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Services.Domain.UserManagement;

namespace NewCosmos.Server.Endpoints;

public static class AuthEndpoints
{
    private static readonly HashSet<string> AllowedDeviceKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "web", "mobile-web", "desktop", "android"
    };

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapPost("/login", LoginAsync);
        group.MapPost("/heartbeat", HeartbeatAsync);
        group.MapPost("/logout", LogoutAsync);
        group.MapGet("/me", MeAsync);
        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest? request,
        HttpContext context,
        IUserService users,
        IUserSessionService sessions,
        CancellationToken ct)
    {
        if (request is null)
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "请求体不能为空");
        }

        var username = request.Username?.Trim();
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(request.Password))
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "用户名和密码不能为空");
        }

        var deviceId = request.DeviceId?.Trim();
        if (string.IsNullOrEmpty(deviceId) || deviceId.Length > 200)
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "deviceId 不能为空且不能超过 200 个字符");
        }

        var deviceKind = request.DeviceKind?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(deviceKind) || !AllowedDeviceKinds.Contains(deviceKind))
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "deviceKind 无效");
        }

        var deviceName = Limit(request.DeviceName?.Trim(), 200, "Web");
        var appVersion = Limit(request.AppVersion?.Trim(), 50, "web");

        var authentication = await users.AuthenticateAsync(username, request.Password, ct);
        if (authentication.IsFailure)
        {
            if (ApiErrors.IsDatabaseErrorCode(authentication.ErrorCode))
            {
                return ApiErrors.Write(context, StatusCodes.Status500InternalServerError,
                    ApiErrorCodes.DbError, "数据库错误");
            }

            // 契约要求不区分“用户不存在/密码错误”。
            return ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthInvalid, "用户名或密码错误");
        }

        var user = authentication.Value;
        if (user is null)
        {
            return ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthInvalid, "用户名或密码错误");
        }

        var created = await sessions.CreateAsync(
            user.Id,
            deviceId,
            deviceKind,
            deviceName,
            appVersion,
            GetClientIp(context),
            ct);

        if (created.IsFailure)
        {
            var mapped = ApiErrors.Map(created.ErrorCode, created.Message);
            return ApiErrors.Write(context, mapped.Status, mapped.Code, mapped.Message);
        }

        var session = created.Value;
        if (session is null)
        {
            return ApiErrors.Write(context, StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError, "创建登录会话失败");
        }

        return Results.Json(new LoginResponse(
            session.SessionId.ToString(),
            AuthHelpers.AsUtc(session.ExpiresAt),
            AuthHelpers.ToUserDto(user)));
    }

    private static async Task<IResult> HeartbeatAsync(
        HttpContext context,
        IUserSessionService sessions,
        CancellationToken ct)
    {
        var sessionId = context.GetSessionId();
        if (sessionId is null)
        {
            return ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired, "请先登录");
        }

        var touch = await sessions.TouchAsync(sessionId.Value, ct);
        if (touch.IsFailure)
        {
            var mapped = ApiErrors.Map(touch.ErrorCode, touch.Message);
            return ApiErrors.Write(context, mapped.Status, mapped.Code, mapped.Message);
        }

        var value = touch.Value;
        if (value is null || !value.IsActive)
        {
            var status = value?.Status ?? SessionValidationStatus.NotFound;
            var failure = AuthHelpers.MapSessionFailure(status, value?.EndReason);
            return ApiErrors.Write(context, failure.Status, failure.Code, failure.Message);
        }

        var expiresAt = context.Items.TryGetValue(HttpContextItemKeys.SessionExpiresAt, out var raw)
                        && raw is DateTime stored
            ? stored
            : DateTime.UtcNow;

        return Results.Json(new HeartbeatResponse(true, AuthHelpers.AsUtc(expiresAt)));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IUserSessionService sessions,
        CancellationToken ct)
    {
        var sessionId = context.GetSessionId();
        var userId = context.GetUserId();
        if (sessionId is null)
        {
            return ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired, "请先登录");
        }

        var result = await sessions.RevokeAsync(
            sessionId.Value,
            SessionConstants.EndReasonUserLogout,
            userId,
            ct);

        context.RequestServices.GetRequiredService<NewCosmos.Services.Core.ISessionGuard>().Disarm();

        if (result.IsFailure)
        {
            var mapped = ApiErrors.Map(result.ErrorCode, result.Message);
            return ApiErrors.Write(context, mapped.Status, mapped.Code, mapped.Message);
        }

        return Results.NoContent();
    }

    private static IResult MeAsync(HttpContext context)
    {
        var user = context.GetCurrentUser();
        return user is null
            ? ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired, "请先登录")
            : Results.Json(user);
    }

    private static string Limit(string? value, int maxLength, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string? GetClientIp(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.Equals(IPAddress.IPv6Loopback) ? "127.0.0.1" : address.ToString();
    }
}
