using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.System;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 低收入人口救助帮扶 · 保障对象动态管理接口。
/// 读：CHANGE_VIEW；写：CHANGE_MANAGE。所有查询/写入均按当前登录用户的数据范围
/// （DataScopeTarget.Applications）过滤，不接受前端传入 userId/orgId。
/// 业务判定全部调用 Core IChangeService / IGracePeriodService，Server 不重抄分类/金额公式。
/// </summary>
public static class ChangeManagementEndpoints
{
    public static IEndpointRouteBuilder MapChangeManagementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/social-assistance/change-management", ListAsync);
        app.MapGet("/api/social-assistance/change-management/filters", GetFiltersAsync);
        app.MapGet("/api/social-assistance/change-management/{applicationId:long}", GetDetailAsync);
        app.MapGet("/api/social-assistance/change-management/{applicationId:long}/history", GetHistoryAsync);

        app.MapPost("/api/social-assistance/change-management/{applicationId:long}/head-change", HeadChangeAsync);
        app.MapPost("/api/social-assistance/change-management/{applicationId:long}/member-change", MemberChangeAsync);
        app.MapPost("/api/social-assistance/change-management/{applicationId:long}/household-death", HouseholdDeathAsync);
        app.MapPost("/api/social-assistance/change-management/{applicationId:long}/grace-confirm", GraceConfirmAsync);
        app.MapPost("/api/social-assistance/change-management/{applicationId:long}/verify-stop", VerifyStopAsync);
        return app;
    }

    // ───────────────────────────── 读 ─────────────────────────────

    private static async Task<IResult> ListAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        ILoggerService logger,
        CancellationToken ct,
        int? page,
        int? pageSize,
        string? keyword,
        string? town,
        string? classification)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.CHANGE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await ChangeManagementQueryService.ListAsync(
            database,
            dataScope,
            logger,
            userId,
            page ?? 1,
            pageSize ?? ChangeManagementQueryService.DefaultPageSize,
            keyword,
            town,
            classification,
            ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetFiltersAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.CHANGE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await ChangeManagementQueryService.GetFiltersAsync(
            database, dataScope, logger, userId, ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetDetailAsync(
        long applicationId,
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IChangeService changes,
        IGracePeriodService gracePeriods,
        IFamilyMemberService familyMembers,
        IStandardConfigService standards,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.CHANGE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var canManage = await permissions.HasPermissionAsync(
            userId, PermissionCodes.CHANGE_MANAGE, ct);

        var result = await ChangeManagementQueryService.GetDetailAsync(
            database,
            dataScope,
            changes,
            gracePeriods,
            familyMembers,
            standards,
            logger,
            userId,
            canManage,
            applicationId,
            ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GetHistoryAsync(
        long applicationId,
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IChangeService changes,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.CHANGE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var result = await ChangeManagementQueryService.GetHistoryAsync(
            database, dataScope, changes, logger, userId, applicationId, ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    // ───────────────────────────── 写 ─────────────────────────────

    private static async Task<IResult> HeadChangeAsync(
        long applicationId,
        HeadChangeRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IChangeService changes,
        IFamilyMemberService familyMembers,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.CHANGE_MANAGE, ct);
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

        var result = await ChangeManagementCommandService.HeadChangeAsync(
            database,
            dataScope,
            changes,
            familyMembers,
            logger,
            user.Id,
            OperatorName(user),
            applicationId,
            request,
            ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> MemberChangeAsync(
        long applicationId,
        MemberChangeRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IChangeService changes,
        IFamilyMemberService familyMembers,
        IIncomeCalculationService incomeCalculation,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.CHANGE_MANAGE, ct);
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

        var result = await ChangeManagementCommandService.MemberChangeAsync(
            database,
            dataScope,
            changes,
            familyMembers,
            incomeCalculation,
            logger,
            user.Id,
            OperatorName(user),
            applicationId,
            request,
            ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> HouseholdDeathAsync(
        long applicationId,
        HouseholdDeathRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IChangeService changes,
        IFamilyMemberService familyMembers,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.CHANGE_MANAGE, ct);
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

        var result = await ChangeManagementCommandService.HouseholdDeathAsync(
            database,
            dataScope,
            changes,
            familyMembers,
            logger,
            user.Id,
            OperatorName(user),
            applicationId,
            request,
            ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> GraceConfirmAsync(
        long applicationId,
        GraceConfirmRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IGracePeriodService gracePeriods,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.CHANGE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var result = await ChangeManagementCommandService.GraceConfirmAsync(
            database,
            dataScope,
            gracePeriods,
            logger,
            userId,
            applicationId,
            request,
            ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> VerifyStopAsync(
        long applicationId,
        VerifyStopRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IChangeService changes,
        ILoggerService logger,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.CHANGE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var result = await ChangeManagementCommandService.VerifyStopAsync(
            database,
            dataScope,
            changes,
            logger,
            user.Id,
            OperatorName(user),
            applicationId,
            request ?? new VerifyStopRequest(null),
            ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    // ───────────────────────────── 通用 ─────────────────────────────

    private static string OperatorName(UserDto user)
        => string.IsNullOrWhiteSpace(user.Username) ? user.FullName ?? "System" : user.Username;

    private static IResult MapResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.NOT_FOUND
            or ErrorCodes.APPLICATION_NOT_FOUND
            or ErrorCodes.FAMILY_MEMBER_NOT_FOUND
            or ErrorCodes.RECORD_NOT_FOUND)
        {
            return ApiErrors.Write(
                context,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                result.Message);
        }

        if (result.ErrorCode is ErrorCodes.DUPLICATE_ID_CARD)
        {
            return ApiErrors.Write(
                context,
                StatusCodes.Status409Conflict,
                ErrorCodes.DUPLICATE_ID_CARD,
                result.Message);
        }

        if (result.ErrorCode is ErrorCodes.INVALID_TRANSITION or ErrorCodes.CONCURRENCY_CONFLICT)
        {
            return ApiErrors.Write(
                context,
                StatusCodes.Status409Conflict,
                "CONFLICT",
                result.Message);
        }

        if (result.ErrorCode is ErrorCodes.VALIDATION_FAILED
            or ErrorCodes.INVALID_ID_CARD
            or ErrorCodes.REQUIRED_FIELD_MISSING
            or ErrorCodes.MEMBER_IS_HEAD
            or ErrorCodes.LAST_MEMBER_CANNOT_DELETE)
        {
            return ApiErrors.Write(
                context,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError,
                result.Message);
        }

        return ApiErrors.FromResult(context, result);
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
