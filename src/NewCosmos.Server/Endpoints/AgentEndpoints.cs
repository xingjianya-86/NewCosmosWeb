using System.Security.Cryptography;
using System.Text;
using NewCosmos.Server.Configuration;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;

namespace NewCosmos.Server.Endpoints;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/agent/register", RegisterAsync);
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
}
