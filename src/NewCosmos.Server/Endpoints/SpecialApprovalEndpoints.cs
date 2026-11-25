using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.SpecialApproval;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 一事一议 / 特殊审批接口。
/// <para>
/// 权限：读取沿用 INCOME_VIEW；发起/编辑/提交用 SPECIAL_APPROVAL_CREATE；
/// 会议审议用 SPECIAL_APPROVAL_REVIEW。
/// 数据范围：所有按 id 的读写都先按申请主表执行与 ApplicationFormService.LoadInScopeAsync
/// 相同口径的数据范围校验（越权与不存在统一 404，不泄露存在性）。
/// </para>
/// </summary>
public static class SpecialApprovalEndpoints
{
    public static IEndpointRouteBuilder MapSpecialApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/special-approvals", CreateAsync);
        app.MapGet("/api/special-approvals/{id}", GetByIdAsync);
        app.MapGet("/api/applications/{id}/special-approval", GetByApplicationAsync);
        app.MapPut("/api/special-approvals/{id}", UpdateAsync);
        app.MapPost("/api/special-approvals/{id}/submit", SubmitAsync);
        app.MapPost("/api/special-approvals/{id}/review", ReviewAsync);
        return app;
    }

    private static async Task<IResult> CreateAsync(
        SpecialApprovalSaveRequest? request,
        HttpContext context,
        ISpecialApprovalService approvals,
        DataScopeFilterBuilder dataScope,
        IDatabaseService database,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.SPECIAL_APPROVAL_CREATE, ct);
        if (denied is not null)
            return denied;

        if (context.GetCurrentUser() is not { } user)
            return Unauthorized(context);

        if (request is null)
            return InvalidRequest(context, "请求体不能为空");
        if (request.ApplicationId <= 0)
            return InvalidRequest(context, "申请 ID 无效");

        var scopeResult = await EnsureApplicationInScopeAsync(
            request.ApplicationId, user.Id, dataScope, database, ct);
        if (scopeResult.IsFailure)
            return MapResult(context, scopeResult);

        var result = await approvals.CreateAsync(request, OperatorName(user), ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(result.Value);
    }

    private static async Task<IResult> GetByIdAsync(
        string id,
        HttpContext context,
        ISpecialApprovalService approvals,
        DataScopeFilterBuilder dataScope,
        IDatabaseService database,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_VIEW, ct);
        if (denied is not null)
            return denied;

        if (context.GetCurrentUser() is not { } user)
            return Unauthorized(context);

        if (ParseId(id) is not { } formId)
            return InvalidRequest(context, "申报表 ID 无效");

        var result = await approvals.GetByIdAsync(formId, ct);
        if (result.IsFailure)
            return MapResult(context, result);

        var scopeResult = await EnsureApplicationInScopeAsync(
            result.Value.ApplicationId, user.Id, dataScope, database, ct);
        if (scopeResult.IsFailure)
            return MapResult(context, scopeResult);

        return Results.Json(result.Value);
    }

    private static async Task<IResult> GetByApplicationAsync(
        string id,
        HttpContext context,
        ISpecialApprovalService approvals,
        DataScopeFilterBuilder dataScope,
        IDatabaseService database,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_VIEW, ct);
        if (denied is not null)
            return denied;

        if (context.GetCurrentUser() is not { } user)
            return Unauthorized(context);

        if (ParseId(id) is not { } applicationId)
            return InvalidRequest(context, "申请 ID 无效");

        var scopeResult = await EnsureApplicationInScopeAsync(
            applicationId, user.Id, dataScope, database, ct);
        if (scopeResult.IsFailure)
            return MapResult(context, scopeResult);

        var result = await approvals.GetByApplicationIdAsync(applicationId, ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(result.Value);
    }

    private static async Task<IResult> UpdateAsync(
        string id,
        SpecialApprovalSaveRequest? request,
        HttpContext context,
        ISpecialApprovalService approvals,
        DataScopeFilterBuilder dataScope,
        IDatabaseService database,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.SPECIAL_APPROVAL_CREATE, ct);
        if (denied is not null)
            return denied;

        if (context.GetCurrentUser() is not { } user)
            return Unauthorized(context);

        if (ParseId(id) is not { } formId)
            return InvalidRequest(context, "申报表 ID 无效");
        if (request is null)
            return InvalidRequest(context, "请求体不能为空");

        var existing = await approvals.GetByIdAsync(formId, ct);
        if (existing.IsFailure)
            return MapResult(context, existing);

        var scopeResult = await EnsureApplicationInScopeAsync(
            existing.Value.ApplicationId, user.Id, dataScope, database, ct);
        if (scopeResult.IsFailure)
            return MapResult(context, scopeResult);

        var result = await approvals.UpdateAsync(formId, request, OperatorName(user), ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(result.Value);
    }

    private static async Task<IResult> SubmitAsync(
        string id,
        HttpContext context,
        ISpecialApprovalService approvals,
        DataScopeFilterBuilder dataScope,
        IDatabaseService database,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.SPECIAL_APPROVAL_CREATE, ct);
        if (denied is not null)
            return denied;

        if (context.GetCurrentUser() is not { } user)
            return Unauthorized(context);

        if (ParseId(id) is not { } formId)
            return InvalidRequest(context, "申报表 ID 无效");

        var existing = await approvals.GetByIdAsync(formId, ct);
        if (existing.IsFailure)
            return MapResult(context, existing);

        var scopeResult = await EnsureApplicationInScopeAsync(
            existing.Value.ApplicationId, user.Id, dataScope, database, ct);
        if (scopeResult.IsFailure)
            return MapResult(context, scopeResult);

        var result = await approvals.SubmitAsync(formId, OperatorName(user), ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(result.Value);
    }

    private static async Task<IResult> ReviewAsync(
        string id,
        SpecialApprovalReviewRequest? request,
        HttpContext context,
        ISpecialApprovalService approvals,
        DataScopeFilterBuilder dataScope,
        IDatabaseService database,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.SPECIAL_APPROVAL_REVIEW, ct);
        if (denied is not null)
            return denied;

        if (context.GetCurrentUser() is not { } user)
            return Unauthorized(context);

        if (ParseId(id) is not { } formId)
            return InvalidRequest(context, "申报表 ID 无效");
        if (request is null)
            return InvalidRequest(context, "请求体不能为空");

        var existing = await approvals.GetByIdAsync(formId, ct);
        if (existing.IsFailure)
            return MapResult(context, existing);

        var scopeResult = await EnsureApplicationInScopeAsync(
            existing.Value.ApplicationId, user.Id, dataScope, database, ct);
        if (scopeResult.IsFailure)
            return MapResult(context, scopeResult);

        var result = await approvals.ReviewAsync(formId, request, OperatorName(user), ct);
        return result.IsFailure ? MapResult(context, result) : Results.Json(result.Value);
    }

    // ───────────────────────────── 数据范围 ─────────────────────────────

    /// <summary>
    /// 与 ApplicationFormService.LoadInScopeAsync 同口径：先按当前用户构造
    /// nc_biz_applications 数据范围条件，再判断申请是否存在且可见。
    /// 越权与不存在都返回 NOT_FOUND。
    /// </summary>
    private static async Task<Result> EnsureApplicationInScopeAsync(
        long applicationId,
        int userId,
        DataScopeFilterBuilder dataScope,
        IDatabaseService database,
        CancellationToken ct)
    {
        var scope = await dataScope.BuildAsync(
            userId, DataScopeTarget.Applications, "a", 2, ct);

        var sql = "SELECT a.id FROM nc_biz_applications a " +
                  "WHERE a.id = $1 AND a.deleted_at IS NULL";
        var parameters = new List<object?> { applicationId };
        if (!scope.IsEmpty)
        {
            sql += " AND " + scope.Sql;
            parameters.AddRange(scope.Parameters);
        }
        sql += " LIMIT 1";

        var result = await database.ExecuteScalarAsync<long?>(sql, ct, parameters.ToArray());
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode, result.Message);
        if (result.Value is not > 0)
            return Result.Failure(ApiErrorCodes.NotFound, "申请不存在");

        return Result.Success();
    }

    // ───────────────────────────── 通用 ─────────────────────────────

    private static long? ParseId(string id)
        => long.TryParse(id, out var value) && value > 0 ? value : null;

    /// <summary>操作人一律取会话中的登录用户名，不接受前端传入。</summary>
    private static string OperatorName(UserDto user)
        => string.IsNullOrWhiteSpace(user.Username) ? user.FullName ?? "System" : user.Username;

    /// <summary>
    /// 业务错误统一出口：一事一议冲突/重复类错误收敛为 409，
    /// 校验类为 400，未找到为 404；其余交 ApiErrors 统一映射。
    /// </summary>
    private static IResult MapResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.SPECIAL_APPROVAL_FORM_NOT_FOUND
            or ErrorCodes.APPLICATION_NOT_FOUND
            or ErrorCodes.NOT_FOUND)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound, result.Message);
        }

        if (result.ErrorCode is ErrorCodes.SPECIAL_APPROVAL_ALREADY_EXISTS
            or ErrorCodes.SPECIAL_APPROVAL_ALREADY_SUBMITTED
            or ErrorCodes.SPECIAL_APPROVAL_REVIEW_DONE
            or ErrorCodes.CONCURRENCY_CONFLICT
            or ErrorCodes.INVALID_TRANSITION)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status409Conflict,
                "CONFLICT", result.Message);
        }

        if (result.ErrorCode is ErrorCodes.VALIDATION_FAILED
            or ErrorCodes.REQUIRED_FIELD_MISSING
            or ErrorCodes.INVALID_CLASSIFICATION)
        {
            return ApiErrors.Write(
                context, StatusCodes.Status400BadRequest,
                ApiErrorCodes.ValidationError, result.Message);
        }

        return ApiErrors.FromResult(context, result);
    }

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError, message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired, "请先登录");
}
