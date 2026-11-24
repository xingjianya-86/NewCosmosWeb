using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 家庭经济状况核对只读接口：首页统计、任务列表、详情、月度审核。
/// 所有端点先校验登录会话与 ASSET_VIEW 权限，再在查询服务内叠加数据范围。
/// </summary>
public static class AssetVerificationEndpoints
{
    public static IEndpointRouteBuilder MapAssetVerificationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/asset-verification/summary", GetSummaryAsync);
        app.MapGet("/api/asset-verification/tasks", ListTasksAsync);
        app.MapGet("/api/asset-verification/tasks/{id:long}", GetDetailAsync);
        app.MapGet("/api/asset-verification/monthly", ListMonthlyAsync);
        return app;
    }

    private static async Task<IResult> GetSummaryAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is null)
        {
            return Unauthorized(context);
        }

        var summary = await CreateQueryService(context).GetSummaryAsync(ct);
        return Results.Json(summary);
    }

    private static async Task<IResult> ListTasksAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        int? page,
        int? pageSize,
        string? status,
        string? keyword,
        string? dateFrom,
        string? dateTo)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!TryReadDate(dateFrom, out var from) || !TryReadDate(dateTo, out var to))
        {
            return InvalidRequest(context, "日期格式应为 YYYY-MM-DD");
        }

        if (from is { } fromValue && to is { } toValue && fromValue > toValue)
        {
            return InvalidRequest(context, "开始日期不能晚于结束日期");
        }

        var result = await CreateQueryService(context).ListTasksAsync(
            userId,
            page ?? 1,
            pageSize ?? AssetVerificationQueryService.DefaultPageSize,
            status,
            keyword,
            from,
            to?.AddDays(1),
            ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetDetailAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await CreateQueryService(context).GetDetailAsync(userId, id, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ListMonthlyAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct,
        int? year,
        int? month,
        int? page,
        int? pageSize,
        string? status,
        string? keyword)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (year is < 1900 or > 9999)
        {
            return InvalidRequest(context, "年份参数无效");
        }

        if (month is < 1 or > 12)
        {
            return InvalidRequest(context, "月份参数应为 1-12");
        }

        var result = await CreateQueryService(context).ListMonthlyAsync(
            userId,
            year,
            month,
            page ?? 1,
            pageSize ?? AssetVerificationQueryService.DefaultPageSize,
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
    private static AssetVerificationQueryService CreateQueryService(HttpContext context)
    {
        var services = context.RequestServices;
        return new AssetVerificationQueryService(
            services.GetRequiredService<IDatabaseService>(),
            services.GetRequiredService<IStatisticsService>(),
            services.GetRequiredService<DataScopeFilterBuilder>());
    }

    private static bool TryReadDate(string? raw, out DateTime? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (DateTime.TryParseExact(
                raw.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context,
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired,
            "请先登录");
}
