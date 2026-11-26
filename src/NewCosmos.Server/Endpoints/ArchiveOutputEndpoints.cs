using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 档案制作中心 + 档案输出接口。
/// <para>
/// 读 <c>ARCHIVE_VIEW</c>；生成任务 / 完成归档 <c>ARCHIVE_MANAGE</c>
/// （打印任务按业务类型的 <c>PRINT_*</c> 权限在本期放宽为 <c>ARCHIVE_MANAGE</c>，
/// 由档案输出统一入口承担，避免同一操作要求两套权限）。
/// </para>
/// <para>
/// 业务口径：字段装配复用 <see cref="ArchiveProductionDataService"/> 与各业务既有 Builder；
/// 分类解析唯一走 <see cref="NewCosmos.Helpers.ArchiveCategoryResolver"/>；本文件只做权限与 Result 映射。
/// </para>
/// </summary>
public static class ArchiveOutputEndpoints
{
    private const string Prefix = "/api/archives";

    public static IEndpointRouteBuilder MapArchiveOutputEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{Prefix}/production", GetProductionAsync);
        app.MapGet($"{Prefix}/output/templates", GetOutputTemplatesAsync);
        app.MapPost($"{Prefix}/output/jobs", CreateOutputJobAsync);
        app.MapPost($"{Prefix}/output/complete", CompleteOutputAsync);

        return app;
    }

    /// <summary>
    /// GET /api/archives/production?businessType=&amp;businessId=
    /// 返回制作页摘要 + 侧边栏字段 + 分类（FamilyApplication / AssetVerification / TempRelief / Recovery）。
    /// </summary>
    private static async Task<IResult> GetProductionAsync(
        HttpContext context,
        INewPermissionService permissions,
        ArchiveProductionDataService production,
        CancellationToken ct,
        string? businessType,
        long? businessId)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(businessType))
        {
            return InvalidRequest(context, "businessType 不能为空");
        }

        if (businessId is not > 0)
        {
            return InvalidRequest(context, "businessId 必须为正整数");
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var result = await production.GetProductionAsync(
            businessType.Trim(), businessId.Value, user.Id, ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>
    /// GET /api/archives/output/templates?businessType=&amp;businessId=&amp;classification=&amp;operationOverride=
    /// 分类命中模板（封面 → 目录 → 其他 sort_order → 名称），全部默认勾选。
    /// </summary>
    private static async Task<IResult> GetOutputTemplatesAsync(
        HttpContext context,
        INewPermissionService permissions,
        ArchiveProductionDataService production,
        CancellationToken ct,
        string? businessType,
        long? businessId,
        string? classification,
        string? operationOverride)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (string.IsNullOrWhiteSpace(businessType))
        {
            return InvalidRequest(context, "businessType 不能为空");
        }

        var result = await production.GetOutputTemplatesAsync(
            businessType.Trim(), businessId, classification, operationOverride, ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>
    /// POST /api/archives/output/jobs
    /// body: { businessType, businessId, classification?, operationOverride?, templateIds, mode, copies?, isDuplex?, printerBindingId?, printerName? }
    /// </summary>
    private static async Task<IResult> CreateOutputJobAsync(
        ArchiveOutputJobRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ArchiveProductionDataService production,
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

        var result = await production.CreateOutputJobAsync(request, user, ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>
    /// POST /api/archives/output/complete
    /// body: { businessType, businessId }
    /// 幂等：已归档返回成功；AssetVerification 无归档语义返回 400。
    /// </summary>
    private static async Task<IResult> CompleteOutputAsync(
        ArchiveOutputCompleteRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        ArchiveProductionDataService production,
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

        if (string.IsNullOrWhiteSpace(request.BusinessType))
        {
            return InvalidRequest(context, "businessType 不能为空");
        }

        if (request.BusinessId is not > 0)
        {
            return InvalidRequest(context, "businessId 必须为正整数");
        }

        var result = await production.CompleteArchiveAsync(
            request.BusinessType.Trim(), request.BusinessId.Value, user, ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new { completed = true });
    }

    // ───────────────────────────── 工具 ─────────────────────────────

    private static IResult MapResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.APPLICATION_NOT_FOUND
            or ErrorCodes.RECORD_NOT_FOUND
            or ErrorCodes.NOT_FOUND)
        {
            return NotFound(context, result.Message ?? "请求的资源不存在");
        }

        if (result.ErrorCode is ErrorCodes.VALIDATION_FAILED
            or ErrorCodes.INVALID_TRANSITION)
        {
            return InvalidRequest(context, result.Message ?? "请求参数校验失败");
        }

        return ApiErrors.FromResult(context, result);
    }

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError, message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired, "请先登录");

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound, message);
}
