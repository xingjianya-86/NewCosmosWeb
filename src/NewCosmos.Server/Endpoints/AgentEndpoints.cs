using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using NewCosmos.Server.Configuration;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Endpoints;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/agent/register", RegisterAsync);
        app.MapPost("/api/agent/register-by-code", RegisterByCodeAsync);
        app.MapPost("/api/agent/heartbeat", HeartbeatAsync);
        return app;
    }

    private static async Task<IResult> RegisterAsync(
        AgentRegisterRequest? request,
        HttpContext context,
        ServerEnvironmentOptions options,
        AgentDeviceService devices,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(options.AgentRegisterToken))
        {
            logger.Warn("Agent 注册被拒绝：未配置 NC_AGENT_REGISTER_TOKEN");
            return ApiErrors.Write(context, StatusCodes.Status503ServiceUnavailable,
                ApiErrorCodes.AgentRegisterDisabled, "Agent 注册未启用");
        }

        var providedToken = context.Request.Headers["X-NC-Agent-Register"].ToString().Trim();
        if (!FixedTimeEquals(providedToken, options.AgentRegisterToken))
        {
            return ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired, "Agent 注册密钥无效");
        }

        if (request is null)
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "请求体不能为空");
        }

        var machineName = request.MachineName?.Trim();
        if (string.IsNullOrEmpty(machineName) || machineName.Length > 200)
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "machineName 不能为空且不能超过 200 个字符");
        }

        if (!AgentPrinterNormalizer.TryNormalize(request.Printers, out var printers, out var printerError))
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, printerError!);
        }

        var agentVersion = request.AgentVersion?.Trim() ?? string.Empty;
        if (agentVersion.Length > 50)
        {
            agentVersion = agentVersion[..50];
        }

        var deviceKey = Guid.NewGuid();
        var result = await devices.RegisterAsync(deviceKey, machineName, agentVersion, printers, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        logger.Info($"Agent 注册成功: DeviceId={result.Value}, Printers={printers.Count}");
        return Results.Json(new AgentRegisterResponse(
            result.Value,
            deviceKey.ToString(),
            options.AgentHeartbeatSeconds));
    }

    /// <summary>
    /// POST /api/agent/register-by-code —— 未注册 Agent 凭网页绑定码注册设备并绑定打印机。
    /// 匿名端点：用户身份只来自绑定码，请求体中的 userId 不会被信任。
    /// </summary>
    private static async Task<IResult> RegisterByCodeAsync(
        AgentRegisterByCodeRequest? request,
        HttpContext context,
        ServerEnvironmentOptions options,
        IDatabaseService database,
        AgentDeviceService devices,
        PrinterBindingService printers,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (!TryAcquireRegisterAttempt(context))
        {
            return ApiErrors.Write(context, StatusCodes.Status429TooManyRequests,
                "RATE_LIMITED", "请求过于频繁，请稍后再试");
        }

        if (request is null)
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "请求体不能为空");
        }

        var code = request.Code?.Trim();
        if (string.IsNullOrEmpty(code)
            || code.Length != PrinterBindingService.BindCodeLength
            || !code.All(char.IsAsciiDigit))
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "绑定码必须是 6 位数字");
        }

        var machineName = request.MachineName?.Trim();
        if (string.IsNullOrEmpty(machineName) || machineName.Length > 200)
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "machineName 不能为空且不能超过 200 个字符");
        }

        if (!AgentPrinterNormalizer.TryNormalize(request.Printers, out var normalizedPrinters, out var printerError))
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, printerError!);
        }

        if (normalizedPrinters.Count == 0)
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "请至少上报一台要绑定的打印机");
        }

        var agentVersion = request.AgentVersion?.Trim() ?? string.Empty;
        if (agentVersion.Length > 50)
        {
            agentVersion = agentVersion[..50];
        }

        await using var transaction = await database.BeginTransactionScopeAsync(ct);

        // FOR UPDATE：同一个码只允许一个 Agent 成功兑换；用户身份只取自这里。
        var codeResult = await database.QuerySingleAsync<PrinterBindingService.BindCodeRow>(
            @"SELECT id, user_id, expires_at, used_at, (expires_at > now()) AS not_expired
              FROM nc_sys_printer_bind_codes
              WHERE code = $1 AND used_at IS NULL
              ORDER BY id DESC
              LIMIT 1
              FOR UPDATE",
            ct,
            code);
        if (codeResult.IsFailure)
        {
            return ApiErrors.FromResult(context, codeResult);
        }

        var bindCode = codeResult.Value;
        if (bindCode is null)
        {
            return ApiErrors.Write(context, StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound, "绑定码无效或已被使用，请在网页端重新生成");
        }

        if (!bindCode.NotExpired)
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "绑定码已过期，请在网页端重新生成");
        }

        var deviceKey = Guid.NewGuid();
        var deviceResult = await devices.RegisterAsync(deviceKey, machineName, agentVersion, normalizedPrinters, ct);
        if (deviceResult.IsFailure)
        {
            return ApiErrors.FromResult(context, deviceResult);
        }

        var deviceId = deviceResult.Value;
        var bindResult = await printers.BindPrintersForDeviceAsync(bindCode.UserId, deviceId, normalizedPrinters, ct);
        if (bindResult.IsFailure)
        {
            return ApiErrors.FromResult(context, bindResult);
        }

        var markUsedResult = await database.ExecuteNonQueryAsync(
            @"UPDATE nc_sys_printer_bind_codes
              SET used_at = now()
              WHERE id = $1 AND used_at IS NULL",
            ct,
            bindCode.Id);
        if (markUsedResult.IsFailure)
        {
            return ApiErrors.FromResult(context, markUsedResult);
        }

        await transaction.CommitAsync(ct);
        logger.Info(
            $"Agent 按码自动注册: UserId={bindCode.UserId}, DeviceId={deviceId}, Printers={normalizedPrinters.Count}");

        return Results.Json(new AgentRegisterByCodeResponse(
            deviceId,
            deviceKey.ToString(),
            options.AgentHeartbeatSeconds,
            bindResult.Value.Bindings));
    }

    private static async Task<IResult> HeartbeatAsync(
        AgentHeartbeatRequest? request,
        HttpContext context,
        AgentDeviceService devices,
        CancellationToken ct)
    {
        if (!Guid.TryParse(context.Request.Headers["X-NC-Device-Key"].ToString().Trim(), out var deviceKey))
        {
            return ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired, "设备不存在或已失效");
        }

        if (!AgentPrinterNormalizer.TryNormalize(request?.Printers, out var printers, out var printerError))
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, printerError!);
        }

        var agentVersion = request?.AgentVersion?.Trim() ?? string.Empty;
        if (agentVersion.Length > 50)
        {
            agentVersion = agentVersion[..50];
        }

        var result = await devices.HeartbeatAsync(deviceKey, agentVersion, printers, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        if (result.Value <= 0)
        {
            return ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired, "设备不存在或已失效");
        }

        return Results.NoContent();
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }

    // ───────────────────────── 按码注册限流（进程内） ─────────────────────────

    private const int RegisterMaxAttemptsPerMinute = 10;
    private const int RegisterRateWindowSeconds = 60;

    private static readonly ConcurrentDictionary<string, (long WindowStart, int Count)> RegisterAttempts = new();

    /// <summary>同一 IP 每分钟最多 10 次按码注册尝试；超限返回 false。</summary>
    private static bool TryAcquireRegisterAttempt(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var attempt = RegisterAttempts.AddOrUpdate(
            ip,
            _ => (now, 1),
            (_, current) => now - current.WindowStart >= RegisterRateWindowSeconds
                ? (now, 1)
                : (current.WindowStart, current.Count + 1));

        // 仅在字典变大时清理过期窗口，避免无界增长。
        if (RegisterAttempts.Count > 10_000)
        {
            foreach (var entry in RegisterAttempts)
            {
                if (now - entry.Value.WindowStart >= RegisterRateWindowSeconds)
                {
                    RegisterAttempts.TryRemove(entry);
                }
            }
        }

        return attempt.Count <= RegisterMaxAttemptsPerMinute;
    }
}
