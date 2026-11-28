using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.Reporting;
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
        app.MapGet("/api/applications/asset-source/{assetCheckId}", GetAssetSourceAsync);
        app.MapPost("/api/applications/form", CreateAsync);
        app.MapPut("/api/applications/{id}/form", UpdateAsync);
        app.MapPost("/api/applications/{id}/classify", ClassifyAsync);
        app.MapPost("/api/applications/{id}/support-mode", UpdateSupportModeAsync);
        app.MapGet("/api/applications/support-institutions", GetSupportInstitutionsAsync);
        app.MapPost("/api/applications/{id}/application-date", ChangeApplicationDateAsync);
        app.MapPost("/api/applications/{id}/complete", CompleteAsync);
        app.MapPost("/api/applications/{id}/remote-complete", CompleteRemoteAssistanceAsync);
        app.MapPost("/api/applications/{id}/visits", AddVisitAsync);
        app.MapGet("/api/applications/{id}/imports/subsidies", GetSubsidyImportsAsync);
        app.MapGet("/api/applications/{id}/imports/land-confirmation", GetLandConfirmationImportsAsync);
        app.MapGet("/api/applications/{id}/imports/bank-flow", GetBankFlowImportsAsync);
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

    /// <summary>
    /// 五步表单右侧「来源数据」：按资产核对记录 ID 取详情（含同批次家庭成员）。
    /// 核对模块的 <c>/api/asset-verification/tasks/{id}</c> 要求 ASSET_VIEW，而表单用户只有
    /// INCOME_*，故在此新增表单侧端点，权限用 <see cref="PermissionCodes.INCOME_VIEW"/>；
    /// 查询本身复用 <see cref="AssetVerificationQueryService.GetDetailAsync"/>（其数据范围
    /// 仍按 AssetChecks 口径，越权/不存在统一 404），不在此重抄 SQL。
    /// </summary>
    private static async Task<IResult> GetAssetSourceAsync(
        string assetCheckId,
        HttpContext context,
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

        if (ParseId(assetCheckId) is not { } checkId)
        {
            return InvalidRequest(context, "资产核对记录 ID 无效");
        }

        var result = await CreateAssetQueryService(context).GetDetailAsync(userId, checkId, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
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
    /// 更新特困供养方式/供养机构（Step5）。body：{ supportType: "Centralized|Scattered", institutionId? }。
    /// 写主表 destitute_support_type / support_institution_id 后立即重新判定，返回最新 classification。
    /// </summary>
    private static async Task<IResult> UpdateSupportModeAsync(
        string id,
        ApplicationFormSupportModeRequest? request,
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

        var result = await forms.UpdateSupportModeAsync(
            applicationId, request, user.Id, OperatorName(user), ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>
    /// 供养机构选项（Step5）。当前 Web 端尚无机构数据源（旧库 nc_biz_support_institutions 已不存在），
    /// 按约定返回空数组，前端显示“暂无机构”，不硬编码任何机构数据。
    /// </summary>
    private static async Task<IResult> GetSupportInstitutionsAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.INCOME_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        return Results.Json(Array.Empty<ApplicationSupportInstitutionDto>());
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

    private static async Task<IResult> CompleteRemoteAssistanceAsync(
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

        var result = await forms.CompleteRemoteAssistanceAsync(applicationId, user.Id, OperatorName(user), ct);
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

    /// <summary>
    /// 银行流水核对带入：按申请人 + 共同生活成员身份证从最新核对批次带出成员流水明细、
    /// 汇总与超阈值提示。只读；身份证清单由服务端按主表/成员行收集，**不接受前端传入身份证**
    /// （数据范围越权与不存在统一 404）。前端把合计余额写入金融资产存款额后随 PUT /form 保存。
    /// </summary>
    private static async Task<IResult> GetBankFlowImportsAsync(
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

        var result = await forms.GetBankFlowImportsAsync(applicationId, userId, ct);
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

    /// <summary>
    /// AssetVerificationQueryService 未注册进 DI（核对模块按请求从 RequestServices 组合），
    /// 表单侧沿用同一组合方式，避免为单个端点改共享注册。
    /// </summary>
    private static AssetVerificationQueryService CreateAssetQueryService(HttpContext context)
    {
        var services = context.RequestServices;
        return new AssetVerificationQueryService(
            services.GetRequiredService<IDatabaseService>(),
            services.GetRequiredService<IStatisticsService>(),
            services.GetRequiredService<DataScopeFilterBuilder>());
    }

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
