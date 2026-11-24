using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 高龄津贴发放只读接口：概览、登记列表/详情、下月待办、类别复核。
/// 所有端点先校验登录会话与 ELDERLY_VIEW 权限；列表/详情在查询服务内叠加数据范围。
/// </summary>
public static class ElderlyEndpoints
{
    public static IEndpointRouteBuilder MapElderlyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/elderly/summary", GetSummaryAsync);
        app.MapGet("/api/elderly/applications", ListApplicationsAsync);
        app.MapGet("/api/elderly/applications/{id:long}", GetApplicationDetailAsync);
        app.MapGet("/api/elderly/pending", GetPendingAsync);
        app.MapGet("/api/elderly/reviews", ListReviewsAsync);
        return app;
    }

    private static async Task<IResult> GetSummaryAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await CreateQueryService(context).GetSummaryAsync(ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ListApplicationsAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        int? page,
        int? pageSize,
        string? status,
        string? keyword)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await CreateQueryService(context).ListApplicationsAsync(
            userId,
            page ?? 1,
            pageSize ?? ElderlyQueryService.DefaultPageSize,
            status,
            keyword,
            ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetApplicationDetailAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await CreateQueryService(context).GetApplicationDetailAsync(userId, id, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetPendingAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await CreateQueryService(context).GetPendingAsync(userId, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ListReviewsAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        int? page,
        int? pageSize,
        string? status,
        string? keyword)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await CreateQueryService(context).ListReviewsAsync(
            userId,
            page ?? 1,
            pageSize ?? ElderlyQueryService.DefaultPageSize,
            status,
            keyword,
            ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>
    /// Program.cs 未注册本模块查询服务（共享文件不在本批改动范围），
    /// 依赖均已注册，这里按请求从 RequestServices 解析后组合。
    /// </summary>
    private static ElderlyQueryService CreateQueryService(HttpContext context)
    {
        var services = context.RequestServices;
        return new ElderlyQueryService(
            services.GetRequiredService<IDatabaseService>(),
            services.GetRequiredService<DataScopeFilterBuilder>(),
            services.GetRequiredService<IElderlyApplicationService>(),
            services.GetRequiredService<IStatisticsService>(),
            services.GetRequiredService<ILoggerService>());
    }

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context,
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired,
            "请先登录");
}
