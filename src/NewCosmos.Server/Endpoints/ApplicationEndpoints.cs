using NewCosmos.Constants;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// P1 申请只读接口。登录会话 + INCOME_VIEW 权限码，并按当前用户数据范围过滤
/// （数据范围由服务端解析，不信任前端传入的 userId/orgId）。
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
        DataScopeFilterBuilder dataScope,
        INewPermissionService permissions,
        CancellationToken ct,
        int? page,
        int? pageSize,
        string? keyword,
        string? status)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var userId = context.GetUserId();
        if (userId is null)
        {
            return Unauthorized(context);
        }

        // 基础筛选参数个数决定数据范围占位符起点（见 CountFilterParameters）。
        var scope = await dataScope.BuildAsync(
            userId.Value,
            DataScopeTarget.Applications,
            "a",
            ApplicationQueryService.CountFilterParameters(status, keyword) + 1,
            ct);

        var result = await applications.ListAsync(
            page ?? 1,
            pageSize ?? ApplicationQueryService.DefaultPageSize,
            keyword,
            status,
            scope,
            ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetDetailAsync(
        string id,
        HttpContext context,
        ApplicationQueryService applications,
        DataScopeFilterBuilder dataScope,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var userId = context.GetUserId();
        if (userId is null)
        {
            return Unauthorized(context);
        }

        if (!long.TryParse(id, out var applicationId) || applicationId <= 0)
        {
            return ApiErrors.Write(context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, "申请 ID 无效");
        }

        // 详情 id 固定 $1，数据范围参数从 $2 开始，防止按 id 越权读取其它乡镇档案。
        var scope = await dataScope.BuildAsync(
            userId.Value, DataScopeTarget.Applications, "a", 2, ct);

        var result = await applications.GetDetailAsync(applicationId, scope, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired, "请先登录");
}
