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
        app.MapGet("/api/social-assistance/change-management/imported-search", SearchImportedAsync);
        app.MapGet("/api/social-assistance/change-management/{applicationId:long}", GetDetailAsync);
        app.MapGet("/api/social-assistance/change-management/{applicationId:long}/history", GetHistoryAsync);

        app.MapPost("/api/social-assistance/change-management/{applicationId:long}/head-change", HeadChangeAsync);
        app.MapPost("/api/social-assistance/change-management/{applicationId:long}/member-change", MemberChangeAsync);
        app.MapPost("/api/social-assistance/change-management/{applicationId:long}/household-death", HouseholdDeathAsync);
        app.MapPost("/api/social-assistance/change-management/{applicationId:long}/grace-confirm", GraceConfirmAsync);
        app.MapPost("/api/social-assistance/change-management/{applicationId:long}/verify-stop", VerifyStopAsync);
        // 导入库待建档 → 建档迁移（双源搜索的写入口，对齐桌面版"点变更时自动建档"）。
        app.MapPost(
            "/api/social-assistance/change-management/imported/{sourceTable}/{sourceId:long}/migrate",
            MigrateImportedAsync);
        return app;
    }

    /// <summary>
    /// 导入库来源表 → ArchiveSearchSource 白名单。只接受这 6 张导入库家庭表，
    /// 其余表名一律 400（Core 的 MigrateToCurrentAsync 也会再校验一次）。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, ArchiveSearchSource> ImportedSourceTables =
        new Dictionary<string, ArchiveSearchSource>(StringComparer.OrdinalIgnoreCase)
        {
            ["nc_biz_rural_subsistence_families"] = ArchiveSearchSource.RuralSubsistence,
            ["nc_biz_urban_subsistence_families"] = ArchiveSearchSource.UrbanSubsistence,
            ["nc_biz_low_income_edge_families"] = ArchiveSearchSource.LowIncomeEdge,
            ["nc_biz_destitute_families"] = ArchiveSearchSource.Destitute,
            ["nc_biz_rigid_expenditure_families"] = ArchiveSearchSource.RigidExpenditure,
            ["nc_biz_other_difficulty_families"] = ArchiveSearchSource.OtherDifficulty
        };

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

    // ───────────────────────────── 导入库待建档 ─────────────────────────────

    /// <summary>
    /// 导入库待建档检索：按姓名/身份证搜 6 个历史导入库，返回脱敏列表 +
    /// 是否已建档（ExistingApplicationId）。只读，权限 CHANGE_VIEW；
    /// 与「档案查询」一致不做数据范围过滤，范围校验在建档迁移时执行。
    /// </summary>
    private static async Task<IResult> SearchImportedAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        IImportedArchiveService importedArchives,
        ILoggerService logger,
        CancellationToken ct,
        string? keyword)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.CHANGE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await ChangeManagementQueryService.SearchImportedAsync(
            database, importedArchives, logger, keyword, ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>
    /// 导入库建档迁移：校验来源表白名单 → 数据范围（按乡镇/村文本套用与列表
    /// 相同的数据范围条件）→ 调 Core MigrateToCurrentAsync（幂等，已建档直接返回）。
    /// 权限 CHANGE_MANAGE；迁移后的档案为草稿，前端引导到五步表单补全。
    /// </summary>
    private static async Task<IResult> MigrateImportedAsync(
        string sourceTable,
        long sourceId,
        MigrateImportedArchiveRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseService database,
        DataScopeFilterBuilder dataScope,
        IImportedArchiveService importedArchives,
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

        var table = (sourceTable ?? string.Empty).Trim();
        if (sourceId <= 0 || !ImportedSourceTables.TryGetValue(table, out var source))
        {
            return InvalidRequest(context, "不支持的导入库来源");
        }

        var item = new ArchiveSearchResultItem
        {
            Source = source,
            SourceTable = table,
            SourceId = sourceId
        };

        var detailResult = await importedArchives.GetImportedFamilyDetailAsync(item, ct);
        if (detailResult.IsFailure)
        {
            return MapResult(context, detailResult);
        }

        var inScope = await ChangeManagementQueryService.IsImportedRecordInScopeAsync(
            database, dataScope, logger, userId,
            detailResult.Value?.Town, detailResult.Value?.Community, ct);
        if (!inScope)
        {
            return ApiErrors.Write(
                context,
                StatusCodes.Status404NotFound,
                ApiErrorCodes.NotFound,
                "该导入库档案不在当前账号数据范围内");
        }

        var classification = string.IsNullOrWhiteSpace(request?.ClassificationCode)
            ? detailResult.Value?.DerivedClassificationCode ?? string.Empty
            : request!.ClassificationCode!.Trim();

        var migrateResult = await importedArchives.MigrateToCurrentAsync(item, classification, ct);
        if (migrateResult.IsFailure)
        {
            return MapResult(context, migrateResult);
        }

        var migrated = migrateResult.Value!;
        logger.Info(
            $"动态管理导入库建档: {table}#{sourceId} → ApplicationId={migrated.ApplicationId}, " +
            $"AlreadyExisted={migrated.AlreadyExisted}, UserId={userId}");

        return Results.Json(new MigrateImportedArchiveResponse(
            migrated.ApplicationId,
            migrated.AlreadyExisted,
            migrated.MigratedMemberCount));
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
