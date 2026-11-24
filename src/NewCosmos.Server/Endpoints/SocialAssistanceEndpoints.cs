using NewCosmos.Constants;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 低收入人口救助帮扶模块查询接口（只读）。
/// 模块首页统计与渐退期列表均要求 INCOME_VIEW；列表按当前用户数据范围过滤。
/// </summary>
public static class SocialAssistanceEndpoints
{
    public static IEndpointRouteBuilder MapSocialAssistanceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/social-assistance/summary", GetSummaryAsync);
        app.MapGet("/api/social-assistance/grace-periods", ListGracePeriodsAsync);
        return app;
    }

    private static async Task<IResult> GetSummaryAsync(
        HttpContext context,
        INewPermissionService permissions,
        IStatisticsService statistics,
        IGracePeriodService gracePeriods,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var summary = await SocialAssistanceQueryService.GetSummaryAsync(
            statistics, gracePeriods, logger, ct);
        return Results.Json(summary);
    }

    private static async Task<IResult> ListGracePeriodsAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        ILoggerService logger,
        CancellationToken ct,
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

        var userId = context.GetUserId();
        if (userId is null)
        {
            return ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
                ApiErrorCodes.AuthRequired, "请先登录");
        }

        var result = await GracePeriodQueryService.ListAsync(
            database,
            dataScope,
            logger,
            userId.Value,
            page ?? 1,
            pageSize ?? GracePeriodQueryService.DefaultPageSize,
            keyword,
            ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }
}
