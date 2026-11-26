using Microsoft.AspNetCore.Mvc;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 档案照片查询 / 补传 / 导出。
/// <para>
/// 读操作（列表、详情、类别、导出）<c>ARCHIVE_VIEW</c>；上传、删除 <c>ARCHIVE_MANAGE</c>。
/// 推送打印本期不提供（Agent 暂无 Photo 任务支持）。
/// </para>
/// </summary>
public static class ArchivePhotoEndpoints
{
    private const string Prefix = "/api/archives/photos";

    public static IEndpointRouteBuilder MapArchivePhotoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Prefix, ListAsync);
        app.MapGet($"{Prefix}/categories", GetCategoriesAsync);
        app.MapGet($"{Prefix}/export", ExportAsync);
        app.MapGet($"{Prefix}/{{id:long}}", GetDetailAsync);
        app.MapPost(Prefix, CreateAsync).DisableAntiforgery();
        app.MapDelete($"{Prefix}/{{id:long}}", DeleteAsync);

        return app;
    }

    /// <summary>
    /// GET /api/archives/photos?keyword=&amp;category=&amp;page=1&amp;pageSize=20
    /// keyword 匹配身份证或照片名；category 空 = 全部；按 created_at 倒序。
    /// </summary>
    private static async Task<IResult> ListAsync(
        HttpContext context,
        INewPermissionService permissions,
        ArchivePhotoService photos,
        CancellationToken ct,
        string? keyword,
        string? category,
        int? page,
        int? pageSize)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await photos.GetPagedAsync(
            keyword,
            category,
            page ?? 1,
            pageSize ?? ArchivePhotoService.DefaultPageSize,
            ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new
            {
                items = result.Value.Items,
                total = result.Value.Total,
                page = result.Value.Page,
                pageSize = result.Value.PageSize
            });
    }

    /// <summary>GET /api/archives/photos/{id} → 含 photoBase64 的详情。</summary>
    private static async Task<IResult> GetDetailAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ArchivePhotoService photos,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await photos.GetByIdAsync(id, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>GET /api/archives/photos/categories → [{ code, label }]（10 类）。</summary>
    private static async Task<IResult> GetCategoriesAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        return Results.Json(ArchivePhotoService.GetCategories());
    }

    /// <summary>
    /// POST /api/archives/photos（multipart：idCard、category、file）→ { id }
    /// 文件格式按魔数识别，仅接受 png / jpeg / webp；单张 ≤10MB。
    /// </summary>
    private static async Task<IResult> CreateAsync(
        HttpContext context,
        INewPermissionService permissions,
        ArchivePhotoService photos,
        CancellationToken ct,
        [FromForm] string? idCard,
        [FromForm] string? category,
        IFormFile? file)
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

        if (file is null || file.Length <= 0)
        {
            return InvalidRequest(context, "请选择照片文件");
        }

        if (file.Length > ArchivePhotoService.MaxPhotoBytes)
        {
            return InvalidRequest(context, "照片不能超过 10MB");
        }

        byte[] fileData;
        await using (var stream = file.OpenReadStream())
        {
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer, ct);
            fileData = buffer.ToArray();
        }

        var operatorName = string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName;
        var result = await photos.CreateAsync(
            idCard, category, fileData, user.Id, operatorName, ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new { id = result.Value });
    }

    /// <summary>DELETE /api/archives/photos/{id}（仅 ARCHIVE_MANAGE）。</summary>
    private static async Task<IResult> DeleteAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        ArchivePhotoService photos,
        CancellationToken ct)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_MANAGE, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await photos.DeleteAsync(id, ct);
        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    /// <summary>
    /// GET /api/archives/photos/export?keyword=&amp;category=&amp;ids=1,2,3
    /// 返回 application/zip，文件名 档案照片_yyyyMMddHHmm.zip。
    /// </summary>
    private static async Task<IResult> ExportAsync(
        HttpContext context,
        INewPermissionService permissions,
        ArchivePhotoService photos,
        CancellationToken ct,
        string? keyword,
        string? category,
        string? ids)
    {
        var denied = await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.ARCHIVE_VIEW, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await photos.ExportAsync(keyword, category, ids, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.File(result.Value.Content, "application/zip", result.Value.FileName);
    }

    private static IResult MapResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.RECORD_NOT_FOUND
            or ErrorCodes.NOT_FOUND
            or ErrorCodes.ARCHIVE_NOT_FOUND)
        {
            return NotFound(context, result.Message ?? "请求的资源不存在");
        }

        if (result.ErrorCode == ErrorCodes.VALIDATION_FAILED)
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
