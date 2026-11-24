using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 低收入人口认定五步表单接口。
/// 所有端点先校验登录会话 + 权限码；读写单条档案前由 ApplicationFormService
/// 叠加 DataScopeFilterBuilder 数据范围（不信任前端 userId/orgId）。
/// </summary>
public static class ApplicationFormEndpoints
{
    public static IEndpointRouteBuilder MapApplicationFormEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/applications/{id}/form", GetFormAsync);
        app.MapPost("/api/applications/form", CreateAsync);
        app.MapPut("/api/applications/{id}/form", UpdateAsync);
        app.MapPost("/api/applications/{id}/classify", ClassifyAsync);
        app.MapPost("/api/applications/{id}/complete", CompleteAsync);
        app.MapDelete("/api/applications/{id}", DeleteAsync);
        return app;
    }

    private static async Task<IResult> GetFormAsync(
        string id,
        HttpContext context,
        ApplicationFormService forms,
        INewPermissionService permissions,
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

        if (ParseId(id) is not { } applicationId)
        {
            return InvalidId(context);
        }

        var result = await forms.GetFormAsync(applicationId, userId, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> CreateAsync(
        ApplicationFormSaveRequest? request,
        HttpContext context,
        ApplicationFormService forms,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_CREATE, ct);
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

        var result = await forms.CreateAsync(request, user.Id, OperatorName(user), ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> UpdateAsync(
        string id,
        ApplicationFormSaveRequest? request,
        HttpContext context,
        ApplicationFormService forms,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        if (ParseId(id) is not { } applicationId)
        {
            return InvalidId(context);
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var result = await forms.UpdateAsync(applicationId, request, user.Id, OperatorName(user), ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ClassifyAsync(
        string id,
        HttpContext context,
        ApplicationFormService forms,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        if (ParseId(id) is not { } applicationId)
        {
            return InvalidId(context);
        }

        var result = await forms.ClassifyAsync(applicationId, user.Id, OperatorName(user), ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> CompleteAsync(
        string id,
        HttpContext context,
        ApplicationFormService forms,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        if (ParseId(id) is not { } applicationId)
        {
            return InvalidId(context);
        }

        var result = await forms.CompleteAsync(applicationId, user.Id, OperatorName(user), ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> DeleteAsync(
        string id,
        HttpContext context,
        ApplicationFormService forms,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_DELETE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        if (ParseId(id) is not { } applicationId)
        {
            return InvalidId(context);
        }

        var result = await forms.DeleteAsync(applicationId, user.Id, OperatorName(user), ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.NoContent();
    }

    private static long? ParseId(string id)
        => long.TryParse(id, out var value) && value > 0 ? value : null;

    /// <summary>操作人一律取会话中的登录用户名，不接受前端传入。</summary>
    private static string OperatorName(UserDto user)
        => string.IsNullOrWhiteSpace(user.Username) ? user.FullName ?? "System" : user.Username;

    /// <summary>
    /// 业务错误统一出口：共享 ApiErrors.Map 未覆盖的冲突/重复类错误在此收敛为
    /// 前端可读的 400/409，避免落入默认 500。
    /// </summary>
    private static IResult MapResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.DUPLICATE_ID_CARD or ErrorCodes.INVALID_TRANSITION)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, result.Message);
        }

        if (result.ErrorCode == ErrorCodes.CONCURRENCY_CONFLICT)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status409Conflict,
                "CONFLICT", result.Message);
        }

        if (result.ErrorCode is ErrorCodes.APPLICATION_NOT_FOUND or ErrorCodes.FAMILY_MEMBER_NOT_FOUND)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound, result.Message);
        }

        return ApiErrors.FromResult(context, result);
    }

    private static IResult InvalidId(HttpContext context)
        => InvalidRequest(context, "申请 ID 无效");

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError, message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired, "请先登录");
}
