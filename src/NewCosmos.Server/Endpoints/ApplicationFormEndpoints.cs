using System.Globalization;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Domain.SocialAssistance;
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
        app.MapPost("/api/applications/{id}/application-date", ChangeApplicationDateAsync);
        app.MapPost("/api/applications/{id}/complete", CompleteAsync);
        app.MapPost("/api/applications/{id}/visits", AddVisitAsync);
        app.MapGet("/api/applications/{id}/imports/subsidies", GetSubsidyImportsAsync);
        app.MapGet("/api/applications/{id}/imports/land-confirmation", GetLandConfirmationImportsAsync);
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

    /// <summary>
    /// 修改申请日期（旧版 Step1「改期」）：请求体 { "newDate": "yyyy-MM-dd" }。
    /// 权限沿用表单编辑码；数据范围复用表单读取链路（内部即 LoadInScopeAsync），
    /// 越权与不存在统一 404。状态闸（仅 Draft 且 current_step&lt;5）、日期上下限、
    /// 编号重取与审计全部由 Core ChangeApplicationDateAsync 负责，端点不绕过。
    /// </summary>
    private static async Task<IResult> ChangeApplicationDateAsync(
        string id,
        ApplicationDateChangeRequest? request,
        HttpContext context,
        ApplicationFormService forms,
        IApplicationService applications,
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

        if (!DateTime.TryParseExact(
                request.NewDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var newDate))
        {
            return InvalidRequest(context, "申请日期格式无效，应为 yyyy-MM-dd");
        }

        // 数据范围闸：GetFormAsync 首步即 LoadInScopeAsync（越权与不存在均 404）。
        var scopeResult = await forms.GetFormAsync(applicationId, user.Id, ct);
        if (scopeResult.IsFailure)
        {
            return MapResult(context, scopeResult);
        }

        var result = await applications.ChangeApplicationDateAsync(
            applicationId, newDate, OperatorName(user), ct);
        if (result.IsFailure)
        {
            // 同日序号并发被占用：改为可重试的 409，而不是通用 DB 500。
            if (result.ErrorCode == ErrorCodes.DB_UNIQUE_VIOLATION)
            {
                return ApiErrors.Write(
                    context,
                    StatusCodes.Status409Conflict,
                    "CONFLICT",
                    result.Message ?? "申请编号撞号，请重试");
            }

            return MapResult(context, result);
        }

        return Results.Json(result.Value);
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

    private static async Task<IResult> AddVisitAsync(
        string id,
        ApplicationVisitCreateRequest? request,
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

        var result = await forms.AddVisitAsync(
            applicationId, request, user.Id, OperatorName(user), ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    // ───────────────────────────── Step3 导入（只读） ─────────────────────────────

    /// <summary>
    /// 补贴导入：返回申请人 + 共同生活成员名下的年度种植补贴，前端放入表单后随 PUT /form 保存。
    /// </summary>
    private static async Task<IResult> GetSubsidyImportsAsync(
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

        var result = await forms.GetSubsidyImportsAsync(applicationId, userId, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>
    /// 土地确权导入：返回申请人 + 共同生活成员姓名匹配的确权地块，前端放入表单后随 PUT /form 保存。
    /// </summary>
    private static async Task<IResult> GetLandConfirmationImportsAsync(
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

        var result = await forms.GetLandConfirmationImportsAsync(applicationId, userId, ct);
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

/// <summary>修改申请日期请求体（契约：{ "newDate": "yyyy-MM-dd" }）。</summary>
public sealed record ApplicationDateChangeRequest(string? NewDate);
