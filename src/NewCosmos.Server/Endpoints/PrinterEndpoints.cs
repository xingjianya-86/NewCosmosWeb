using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;

namespace NewCosmos.Server.Endpoints;

public static class PrinterEndpoints
{
    public static IEndpointRouteBuilder MapPrinterEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/printers", ListAsync);
        app.MapGet("/api/printers/devices", ListDevicesAsync);
        app.MapPost("/api/printers/bind", BindAsync);
        app.MapDelete("/api/printers/{id}", DeleteAsync);
        app.MapPost("/api/printers/{id}/default", SetDefaultAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        PrinterBindingService printers,
        CancellationToken ct)
    {
        var userId = context.GetUserId();
        if (userId is null)
        {
            return Unauthorized(context);
        }

        var result = await printers.ListAsync(userId.Value, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ListDevicesAsync(
        HttpContext context,
        PrinterBindingService printers,
        CancellationToken ct)
    {
        var userId = context.GetUserId();
        if (userId is null)
        {
            return Unauthorized(context);
        }

        var result = await printers.ListDevicesAsync(ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> BindAsync(
        BindPrinterRequest? request,
        HttpContext context,
        PrinterBindingService printers,
        CancellationToken ct)
    {
        var userId = context.GetUserId();
        if (userId is null)
        {
            return Unauthorized(context);
        }

        if (request is null)
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "请求体不能为空");
        }

        var result = await printers.BindPrinterAsync(userId.Value, request, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> DeleteAsync(
        string id,
        HttpContext context,
        PrinterBindingService printers,
        CancellationToken ct)
    {
        var userId = context.GetUserId();
        if (userId is null)
        {
            return Unauthorized(context);
        }

        if (!long.TryParse(id, out var bindingId) || bindingId <= 0)
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "绑定 ID 无效");
        }

        var result = await printers.DeleteAsync(userId.Value, bindingId, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.NoContent();
    }

    private static async Task<IResult> SetDefaultAsync(
        string id,
        HttpContext context,
        PrinterBindingService printers,
        CancellationToken ct)
    {
        var userId = context.GetUserId();
        if (userId is null)
        {
            return Unauthorized(context);
        }

        if (!long.TryParse(id, out var bindingId) || bindingId <= 0)
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "绑定 ID 无效");
        }

        var result = await printers.SetDefaultAsync(userId.Value, bindingId, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired, "请先登录");
}
