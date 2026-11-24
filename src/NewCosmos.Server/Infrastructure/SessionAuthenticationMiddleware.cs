using System.Net.Http.Headers;
using NewCosmos.Server.Contracts;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.UserManagement;

namespace NewCosmos.Server.Infrastructure;

/// <summary>
/// Bearer 会话校验：
/// 1. Authorization: Bearer &lt;sessionId&gt; + X-NC-Device 必须同时存在；
/// 2. 会话必须为 active、未过期且设备标识匹配；
/// 3. 通过后设置 CurrentUserContext 快照并 Arm 会话闸门。
/// 登录/健康检查/Agent 注册与心跳走匿名路径。
/// </summary>
public sealed class SessionAuthenticationMiddleware(RequestDelegate next, ILoggerService logger)
{
    private static readonly PathString[] AnonymousPaths =
    [
        new("/api/auth/login"),
        new("/api/agent/register"),
        new("/api/agent/heartbeat"),
        new("/health"),
        new("/info")
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsOptions(context.Request.Method) || IsAnonymousPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (!TryGetBearerSessionId(context.Request, out var sessionId))
        {
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired, "请先登录");
            return;
        }

        var deviceId = context.Request.Headers["X-NC-Device"].ToString().Trim();
        if (string.IsNullOrEmpty(deviceId))
        {
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired, "缺少 X-NC-Device 请求头");
            return;
        }

        var sessions = context.RequestServices.GetRequiredService<IUserSessionService>();
        var guard = context.RequestServices.GetRequiredService<ISessionGuard>();

        var validation = await sessions.ValidateAsync(sessionId, deviceId, context.RequestAborted);
        if (validation.IsFailure)
        {
            var error = ApiErrors.Map(validation.ErrorCode, validation.Message);
            logger.Warn($"会话校验失败: {error.Code}");
            await WriteErrorAsync(context, error.Status, error.Code, error.Message);
            return;
        }

        var validationValue = validation.Value;
        if (validationValue is null || !validationValue.IsActive)
        {
            var status = validationValue?.Status ?? SessionValidationStatus.NotFound;
            var error = AuthHelpers.MapSessionFailure(status, validationValue?.EndReason);
            await WriteErrorAsync(context, error.Status, error.Code, error.Message);
            return;
        }

        // 会话有效后再取用户快照；Join 保证会话被并发删除时不会拿到用户。
        var db = context.RequestServices.GetRequiredService<IDatabaseService>();
        var userResult = await db.QuerySingleAsync<User>(
            @"SELECT u.*, o.name AS organization_name
              FROM nc_sys_user_sessions s
              INNER JOIN nc_sys_users u ON u.id = s.user_id
              LEFT JOIN nc_sys_organizations o ON o.id = u.organization_id
              WHERE s.session_id = $1 AND s.status = 'active' AND s.expires_at > now()",
            context.RequestAborted,
            sessionId);

        if (userResult.IsFailure)
        {
            var error = ApiErrors.Map(userResult.ErrorCode, userResult.Message);
            logger.Warn($"读取当前用户失败: {error.Code}");
            await WriteErrorAsync(context, error.Status, error.Code, error.Message);
            return;
        }

        var user = userResult.Value;
        if (user is null || !user.IsActive)
        {
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired, "账号不存在或已停用");
            return;
        }

        var userDto = AuthHelpers.ToUserDto(user);
        context.Items[HttpContextItemKeys.UserId] = user.Id;
        context.Items[HttpContextItemKeys.SessionId] = sessionId;
        context.Items[HttpContextItemKeys.SessionExpiresAt] = AuthHelpers.AsUtc(validationValue.ExpiresAt);
        context.Items[HttpContextItemKeys.DeviceId] = deviceId;
        context.Items[HttpContextItemKeys.User] = userDto;

        guard.Arm(sessionId);
        try
        {
            using var scope = CurrentUserContext.Begin(new CurrentUserSnapshot(
                user.Id,
                sessionId,
                user.Username,
                user.FullName,
                user.Phone,
                user.OrganizationId));

            await next(context);
        }
        finally
        {
            guard.Disarm();
        }
    }

    private static bool TryGetBearerSessionId(HttpRequest request, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization.ToString(), out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter))
        {
            return false;
        }

        return Guid.TryParse(header.Parameter, out sessionId);
    }

    private static bool IsAnonymousPath(PathString path)
        => AnonymousPaths.Any(anonymous => path.StartsWithSegments(anonymous, StringComparison.OrdinalIgnoreCase));

    private static Task WriteErrorAsync(HttpContext context, int status, string code, string message)
    {
        if (context.RequestAborted.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(
            new ApiErrorResponse(code, message, ApiErrors.GetTraceId(context)),
            context.RequestAborted);
    }
}
