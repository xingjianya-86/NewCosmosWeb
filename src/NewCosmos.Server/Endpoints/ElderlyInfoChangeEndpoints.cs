using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 高龄年度认证·信息变更 HTTP API：
/// 查询 ELDERLY_VIEW；创建/审批/驳回 ELDERLY_EDIT；数据范围由 QueryService 校验。
/// </summary>
public static class ElderlyInfoChangeEndpoints
{
    public static IEndpointRouteBuilder MapElderlyInfoChangeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/api/elderly/certifications/info-changes",
            ListAsync);
        app.MapGet(
            "/api/elderly/certifications/tasks/{taskId:long}/info-changes",
            ListByTaskAsync);
        app.MapPost(
            "/api/elderly/certifications/tasks/{taskId:long}/info-changes",
            CreateAsync);
        app.MapPost(
            "/api/elderly/certifications/info-changes/{id:long}/approve",
            ApproveAsync);
        app.MapPost(
            "/api/elderly/certifications/info-changes/{id:long}/reject",
            RejectAsync);
        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        INewPermissionService permissions,
        ElderlyInfoChangeQueryService query,
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

        var result = await query.GetPagedAsync(
            userId, keyword, status,
            page ?? 1, pageSize ?? 20, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ListByTaskAsync(
        long taskId,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyInfoChangeQueryService query,
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

        var result = await query.GetByTaskAsync(userId, taskId, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> CreateAsync(
        long taskId,
        ElderlyInfoChangeCreateRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyInfoChangeWriteService write,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_EDIT, ct);
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

        var result = await write.CreateAsync(
            user.Id, taskId, request, OperatorName(user), ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ApproveAsync(
        long id,
        ElderlyInfoChangeApproveRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyInfoChangeWriteService write,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var result = await write.ApproveAsync(
            user.Id, id, request ?? new ElderlyInfoChangeApproveRequest(), OperatorName(user), ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.NoContent();
    }

    private static async Task<IResult> RejectAsync(
        long id,
        ElderlyInfoChangeRejectRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ElderlyInfoChangeWriteService write,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ELDERLY_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var result = await write.RejectAsync(
            user.Id, id, request ?? new ElderlyInfoChangeRejectRequest(), OperatorName(user), ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.NoContent();
    }

    private static string OperatorName(UserDto user)
        => string.IsNullOrWhiteSpace(user.Username) ? user.FullName ?? "System" : user.Username;

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status400BadRequest,
            ErrorCodes.VALIDATION_FAILED,
            message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context,
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired,
            "请先登录");
}
