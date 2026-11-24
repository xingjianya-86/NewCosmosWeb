using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Server.Infrastructure;

/// <summary>统一异常出口，所有错误响应均为 {code,message,traceId}。</summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILoggerService logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 客户端已断开，无需再写响应。
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                logger.LogError(exception, "响应已开始，无法输出统一错误");
                throw;
            }

            var mapped = ApiErrors.MapException(exception);
            if (mapped.Status >= StatusCodes.Status500InternalServerError)
            {
                logger.LogError(exception, $"{context.Request.Method} {context.Request.Path}");
            }
            else
            {
                logger.Warn($"请求被拒绝: {context.Request.Method} {context.Request.Path} {mapped.Code}");
            }

            // 不调用 Response.Clear()：保留 CORS / 安全响应头；此时响应尚未开始，未写出任何内容。
            context.Response.StatusCode = mapped.Status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";

            var message = mapped.Message;
            if (environment.IsDevelopment() && mapped.Status >= StatusCodes.Status500InternalServerError)
            {
                // 仅开发环境附带异常摘要，生产环境绝不泄漏内部细节。
                message = $"{message}: {exception.GetType().Name}";
            }

            await context.Response.WriteAsJsonAsync(
                new Contracts.ApiErrorResponse(mapped.Code, message, ApiErrors.GetTraceId(context)),
                context.RequestAborted);
        }
    }
}
