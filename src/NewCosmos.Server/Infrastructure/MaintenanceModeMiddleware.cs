using NewCosmos.Server.Configuration;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services.DataCenter;

namespace NewCosmos.Server.Infrastructure;

/// <summary>
/// 数据库还原期间的全站维护拦截：除健康检查和数据中心任务轮询外一律返回 503，
/// 避免还原过程中其它业务请求继续读写正在被替换的数据库。
/// </summary>
public sealed class MaintenanceModeMiddleware(RequestDelegate next, MaintenanceState state)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!state.IsActive)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        if (DataCenterOptions.MaintenanceAllowedPrefixes.Any(prefix =>
                path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var startedAt = state.StartedAt?.ToLocalTime().ToString("HH:mm:ss");
        ApiErrors.Write(context, StatusCodes.Status503ServiceUnavailable,
            "MAINTENANCE",
            $"系统正在还原数据库，暂时不可用（开始于 {startedAt}）。请稍后刷新重试。");
    }
}
