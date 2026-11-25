using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 月度审核页附件接口：核查照片、PDF 报告上传/版本/原文预览、驳回。
/// 权限先走 <see cref="EndpointSecurity"/>（查看 ASSET_VIEW / 修改 ASSET_EDIT），
/// 暂不做机构数据范围过滤（由后续批次补充）。文件单件上限 20MB。
/// </summary>
public static class AssetMonthlyAuditEndpoints
{
    public static IEndpointRouteBuilder MapAssetMonthlyAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/asset-verification/checks/{checkId:long}/photos", GetPhotosAsync);
        app.MapPost("/api/asset-verification/checks/{checkId:long}/photos", UploadPhotoAsync)
            .DisableAntiforgery();
        app.MapGet("/api/asset-verification/checks/{checkId:long}/reports", GetReportsAsync);
        app.MapPost("/api/asset-verification/checks/{checkId:long}/reports", UploadReportAsync)
            .DisableAntiforgery();
        app.MapGet("/api/asset-verification/reports/{reportId:long}/content", GetReportContentAsync);
        app.MapPost("/api/asset-verification/checks/{checkId:long}/reject", RejectCheckAsync);
        app.MapPost("/api/asset-verification/reports/{reportId:long}/print", PrintReportAsync);
        return app;
    }

    private static async Task<IResult> GetPhotosAsync(
        long checkId,
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

        var result = await CreateService(context).ListPhotosAsync(checkId, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> UploadPhotoAsync(
        long checkId,
        IFormFile? file,
        [FromForm] string? category,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var validation = ValidateUpload(file, "照片", context);
        if (validation is not null)
        {
            return validation;
        }

        var fileName = SafeFileName(file!.FileName, "photo");
        var data = await ReadAllBytesAsync(file, ct);

        var result = await CreateService(context).UploadPhotoAsync(
            checkId,
            fileName,
            category,
            data,
            user.Id,
            OperatorName(user),
            ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(new { id = result.Value });
    }

    private static async Task<IResult> GetReportsAsync(
        long checkId,
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

        var result = await CreateService(context).ListReportsAsync(checkId, ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> UploadReportAsync(
        long checkId,
        IFormFile? file,
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetCurrentUser() is not { } user)
        {
            return Unauthorized(context);
        }

        var validation = ValidateUpload(file, "报告", context);
        if (validation is not null)
        {
            return validation;
        }

        var fileName = SafeFileName(file!.FileName, "report.pdf");
        if (!IsSupportedReport(fileName))
        {
            return InvalidRequest(context, "报告仅支持 PDF 或图片文件（jpg/jpeg/png/webp）");
        }

        var data = await ReadAllBytesAsync(file, ct);
        var result = await CreateService(context).UploadReportAsync(
            checkId, fileName, data, user.Id, ct);

        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.Json(new { id = result.Value.Id, version = result.Value.Version });
    }

    private static async Task<IResult> GetReportContentAsync(
        long reportId,
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

        var result = await CreateService(context).GetReportContentAsync(reportId, ct);
        if (result.IsFailure)
        {
            return ApiErrors.FromResult(context, result);
        }

        var content = result.Value;
        return Results.File(content.Data, content.ContentType, content.FileName);
    }

    private static async Task<IResult> RejectCheckAsync(
        long checkId,
        HttpContext context,
        INewPermissionService permissions,
        IAssetVerificationService verification,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ASSET_EDIT, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (context.GetUserId() is null)
        {
            return Unauthorized(context);
        }

        var existsResult = await CreateService(context).CheckExistsAsync(checkId, ct);
        if (existsResult.IsFailure)
        {
            return ApiErrors.FromResult(context, existsResult);
        }

        if (!existsResult.Value)
        {
            return NotFound(context, "核对记录不存在");
        }

        var result = await verification.UpdateCheckStatusAsync(checkId, "3", ct);
        return result.IsFailure
            ? ApiErrors.FromResult(context, result)
            : Results.NoContent();
    }

    /// <summary>
    /// PDF 报告打印暂未接入：明确返回 501，不伪造 PrintJob。
    /// 前端据此禁用打印按钮并提示。
    /// </summary>
    private static async Task<IResult> PrintReportAsync(
        long reportId,
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

        return ApiErrors.Write(
            context,
            StatusCodes.Status501NotImplemented,
            ApiErrorCodes.ValidationError,
            "PDF 报告打印待接入");
    }

    private static IResult? ValidateUpload(IFormFile? file, string label, HttpContext context)
    {
        if (file is null || file.Length <= 0)
        {
            return InvalidRequest(context, $"请选择要上传的{label}文件");
        }

        if (file.Length > AssetMonthlyAuditService.MaxUploadBytes)
        {
            return InvalidRequest(context, "单个文件不能超过 20MB");
        }

        return null;
    }

    private static async Task<byte[]> ReadAllBytesAsync(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static string SafeFileName(string? raw, string fallback)
    {
        var name = Path.GetFileName((raw ?? string.Empty).Trim());
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private static bool IsSupportedReport(string fileName)
        => Path.GetExtension(fileName).ToLowerInvariant()
            is ".pdf" or ".jpg" or ".jpeg" or ".jpe" or ".png" or ".webp";

    private static string OperatorName(UserDto user)
        => string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName!;

    private static AssetMonthlyAuditService CreateService(HttpContext context)
        => new(context.RequestServices.GetRequiredService<IDatabaseService>());

    private static IResult InvalidRequest(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.ValidationError,
            message);

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(
            context,
            StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound,
            message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(
            context,
            StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired,
            "请先登录");
}
