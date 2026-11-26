using NewCosmos.Constants;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 每月公示文档输出接口。
/// <para>
/// 权限统一用 <see cref="PermissionCodes.ARCHIVE_MANAGE"/>（读也用它，沿用旧版「随档案管理权限」）。
/// 预览（families）与生成（groups）口径不同是旧版既有行为：预览只取 5 个导入库，
/// 生成才合并当前库在保户并排除退出人员。业务取数/分组/字段装配全部在
/// <see cref="PublicityService"/>，本文件只做参数校验与 HTTP 映射。
/// </para>
/// </summary>
public static class PublicityEndpoints
{
    private const string Prefix = "/api/publicity";

    public static IEndpointRouteBuilder MapPublicityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{Prefix}/families", GetFamiliesAsync);
        app.MapGet($"{Prefix}/groups", GetGroupsAsync);
        app.MapPost($"{Prefix}/jobs", CreateJobsAsync);
        return app;
    }

    /// <summary>GET /api/publicity/families?year=&amp;month= —— 预览名单（仅 5 个导入库）。</summary>
    private static async Task<IResult> GetFamiliesAsync(
        HttpContext context,
        INewPermissionService permissions,
        PublicityService publicity,
        CancellationToken ct,
        int? year,
        int? month)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!TryResolvePeriod(year, month, out var resolvedYear, out var resolvedMonth, out var error))
        {
            return InvalidRequest(context, error!);
        }

        var result = await publicity.GetPreviewAsync(resolvedYear, resolvedMonth, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>GET /api/publicity/groups?year=&amp;month= —— 生成口径名单（合并当前库并排除退出）。</summary>
    private static async Task<IResult> GetGroupsAsync(
        HttpContext context,
        INewPermissionService permissions,
        PublicityService publicity,
        CancellationToken ct,
        int? year,
        int? month)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (!TryResolvePeriod(year, month, out var resolvedYear, out var resolvedMonth, out var error))
        {
            return InvalidRequest(context, error!);
        }

        var result = await publicity.GetGroupsAsync(resolvedYear, resolvedMonth, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>POST /api/publicity/jobs —— 按村生成公示文档并提交 Agent 保存任务。</summary>
    private static async Task<IResult> CreateJobsAsync(
        PublicityJobRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        PublicityService publicity,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        if (!TryResolvePeriod(request.Year, request.Month, out var year, out var month, out var error))
        {
            return InvalidRequest(context, error!);
        }

        var operatorName = string.IsNullOrWhiteSpace(user.FullName)
            ? user.Username
            : user.FullName;

        var result = await publicity.CreateJobsAsync(
            user.Id,
            operatorName,
            user.OrganizationId,
            year,
            month,
            request.Villages,
            ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>年份缺省取当前年、月份缺省取当前月；显式传入时校验范围。</summary>
    private static bool TryResolvePeriod(
        int? year,
        int? month,
        out int resolvedYear,
        out int resolvedMonth,
        out string? error)
    {
        resolvedYear = year ?? DateTime.Now.Year;
        resolvedMonth = month ?? DateTime.Now.Month;
        error = null;

        if (resolvedYear is < 1900 or > 9999)
        {
            error = "年份参数无效";
            return false;
        }

        if (resolvedMonth is < 1 or > 12)
        {
            error = "月份参数应为 1-12";
            return false;
        }

        return true;
    }

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context,
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired,
            "请先登录");

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            message);
}

/// <summary>POST /api/publicity/jobs 请求体。villages 为空/省略表示全部村。</summary>
public sealed record PublicityJobRequest(
    int? Year,
    int? Month,
    List<string>? Villages);
