using NewCosmos.Constants;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 业务申请工作流只读接口：五阶段计数 + 分页列表。
/// 入口需要 INCOME_VIEW；资产核查两个 Tab 额外需要 ASSET_VIEW。
/// </summary>
public static class ApplicationWorkflowEndpoints
{
    public static IEndpointRouteBuilder MapApplicationWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/social-assistance/workflow/summary", GetSummaryAsync);
        app.MapGet("/api/social-assistance/workflow", ListAsync);
        return app;
    }

    private static async Task<IResult> GetSummaryAsync(
        HttpContext context,
        INewPermissionService permissions,
        ApplicationWorkflowQueryService workflow,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var includeAsset = await permissions.HasPermissionAsync(
            userId, PermissionCodes.ASSET_VIEW, ct);
        var summary = await workflow.GetSummaryAsync(userId, includeAsset, ct);
        return Results.Json(summary);
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        INewPermissionService permissions,
        ApplicationWorkflowQueryService workflow,
        CancellationToken ct,
        int? tab,
        int? page,
        int? pageSize,
        string? keyword)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var tabIndex = tab ?? 0;
        if (tabIndex is < 0 or > 4)
        {
            return ApiErrors.Write(
                context,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                "工作流阶段无效");
        }

        if (tabIndex is 0 or 1)
        {
            var assetDenied = await EndpointSecurity.RequirePermissionAsync(
                context, permissions, PermissionCodes.ASSET_VIEW, ct);
            if (assetDenied is not null)
            {
                return assetDenied;
            }
        }

        var result = await workflow.ListAsync(
            userId,
            tabIndex,
            page ?? 1,
            pageSize ?? ApplicationWorkflowQueryService.DefaultPageSize,
            keyword,
            ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context,
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired,
            "请先登录");
}
