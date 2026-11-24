using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// P1 申请只读接口。认证按登录会话判断；权限码校验暂未接入（见 README「P1 申请查询」）。
/// </summary>
public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/applications", ListAsync);
        app.MapGet("/api/applications/{id}", GetDetailAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        ApplicationQueryService applications,
        CancellationToken ct,
        int? page,
        int? pageSize,
        string? keyword,
        string? status)
    {
        if (context.GetUserId() is null)
        {
            return Unauthorized(context);
        }

        // TODO(P1): 暂无权限码校验，登录用户均可查询；接入权限中心后按数据范围（乡镇/村）过滤。
        var result = await applications.ListAsync(
            page ?? 1,
            pageSize ?? ApplicationQueryService.DefaultPageSize,
            keyword,
            status,
            ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetDetailAsync(
        string id,
        HttpContext context,
        ApplicationQueryService applications,
        CancellationToken ct)
    {
        if (context.GetUserId() is null)
        {
            return Unauthorized(context);
        }

        if (!long.TryParse(id, out var applicationId) || applicationId <= 0)
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "申请 ID 无效");
        }

        // TODO(P1): 暂无权限码校验，登录用户均可查看详情（403 待权限码接入后补齐）。
        var result = await applications.GetDetailAsync(applicationId, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired, "请先登录");
}
