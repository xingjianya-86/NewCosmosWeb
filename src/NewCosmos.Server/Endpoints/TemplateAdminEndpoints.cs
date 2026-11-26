using Microsoft.AspNetCore.Mvc;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 文书模板管理（写 + 读均要求 <c>TEMPLATE_MANAGE</c>）。
/// <para>
/// 前缀 <c>/api/template-admin</c>，避免与只读的 <c>/api/templates</c> 冲突。
/// 分类树与文档类型词表复用 Core（TemplateCategoryProvider / TemplateDocTypes）。
/// </para>
/// </summary>
public static class TemplateAdminEndpoints
{
    private const string Prefix = "/api/template-admin";

    public static IEndpointRouteBuilder MapTemplateAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Prefix, ListAsync);
        app.MapGet($"{Prefix}/categories", GetCategoriesAsync);
        app.MapPost($"{Prefix}/categories", CreateCategoryAsync);
        app.MapPut($"{Prefix}/categories/{{id:long}}", RenameCategoryAsync).DisableAntiforgery();
        app.MapDelete($"{Prefix}/categories/{{id:long}}", DeleteCategoryAsync);
        app.MapGet($"{Prefix}/doc-types", GetDocTypesAsync);
        app.MapGet($"{Prefix}/{{id:long}}", GetDetailAsync);
        app.MapGet($"{Prefix}/{{id:long}}/file", GetFileAsync);
        app.MapPost(Prefix, CreateAsync).DisableAntiforgery();
        app.MapPut($"{Prefix}/{{id:long}}", UpdateAsync).DisableAntiforgery();
        app.MapDelete($"{Prefix}/{{id:long}}", DeleteAsync);
        app.MapPost($"{Prefix}/{{id:long}}/move", MoveAsync);

        return app;
    }

    /// <summary>GET /api/template-admin?keyword= → 模板列表（不含 file_data）。</summary>
    private static async Task<IResult> ListAsync(
        HttpContext context,
        INewPermissionService permissions,
        TemplateAdminService templates,
        CancellationToken ct,
        string? keyword)
    {
        var denied = await RequireAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await templates.ListAsync(keyword, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>GET /api/template-admin/categories → 内置 + 自定义分类树。</summary>
    private static async Task<IResult> GetCategoriesAsync(
        HttpContext context,
        INewPermissionService permissions,
        TemplateAdminService templates,
        CancellationToken ct)
    {
        var denied = await RequireAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await templates.GetCategoryTreeAsync(ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>GET /api/template-admin/doc-types → TemplateDocTypes.All。</summary>
    private static async Task<IResult> GetDocTypesAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
    {
        var denied = await RequireAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        return Results.Json(TemplateAdminService.GetDocTypes());
    }

    /// <summary>POST /api/template-admin/categories body {parentPath,label} → {id,path}。</summary>
    private static async Task<IResult> CreateCategoryAsync(
        [FromBody] TemplateCategoryCreateRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        TemplateAdminService templates,
        CancellationToken ct)
    {
        var denied = await RequireAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var result = await templates.CreateCategoryAsync(request.ParentPath, request.Label, ct);
        if (result.IsFailure)
        {
            return MapResult(context, result);
        }

        return Results.Json(new { id = result.Value.Id, path = result.Value.Path });
    }

    /// <summary>PUT /api/template-admin/categories/{id} body {label} → {id,path}。</summary>
    private static async Task<IResult> RenameCategoryAsync(
        long id,
        [FromBody] TemplateCategoryUpdateRequest? request,
        HttpContext context,
        INewPermissionService permissions,
        TemplateAdminService templates,
        CancellationToken ct)
    {
        var denied = await RequireAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        if (request is null)
        {
            return InvalidRequest(context, "请求体不能为空");
        }

        var result = await templates.RenameCategoryAsync(id, request.Label, ct);
        if (result.IsFailure)
        {
            return MapResult(context, result);
        }

        return Results.Json(new { id = result.Value.Id, path = result.Value.Path });
    }

    /// <summary>DELETE /api/template-admin/categories/{id}?force=false → 204 / 409。</summary>
    private static async Task<IResult> DeleteCategoryAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        TemplateAdminService templates,
        bool? force,
        CancellationToken ct)
    {
        var denied = await RequireAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await templates.DeleteCategoryAsync(id, force ?? false, ct);
        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    /// <summary>GET /api/template-admin/{id} → 详情（含 configJson / categories / 默认份数）。</summary>
    private static async Task<IResult> GetDetailAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        TemplateAdminService templates,
        CancellationToken ct)
    {
        var denied = await RequireAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await templates.GetDetailAsync(id, ct);
        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(result.Value);
    }

    /// <summary>GET /api/template-admin/{id}/file → 下载原始模板文件。</summary>
    private static async Task<IResult> GetFileAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        TemplateAdminService templates,
        CancellationToken ct)
    {
        var denied = await RequireAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await templates.GetFileAsync(id, ct);
        if (result.IsFailure)
        {
            return MapResult(context, result);
        }

        var file = result.Value;
        var isExcel = string.Equals(
            file.FileType, TemplateFileTypes.Excel, StringComparison.OrdinalIgnoreCase);
        var contentType = isExcel
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        return Results.File(file.Content, contentType, BuildDownloadName(file.FileName, isExcel));
    }

    /// <summary>
    /// POST /api/template-admin（multipart：file、name、docType、categories、configJson?）→ { id, configWarnings }
    /// categories 支持 JSON 数组或逗号分隔；docType 必须在 TemplateDocTypes.All。
    /// </summary>
    private static async Task<IResult> CreateAsync(
        HttpContext context,
        INewPermissionService permissions,
        TemplateAdminService templates,
        CancellationToken ct,
        [FromForm] string? name,
        [FromForm] string? docType,
        [FromForm] string? categories,
        [FromForm] string? configJson,
        IFormFile? file)
    {
        var denied = await RequireAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        var fileData = await ReadFileAsync(file, ct);
        var result = await templates.CreateAsync(
            name, docType, categories, configJson, fileData, file?.FileName, ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new
            {
                id = result.Value.Id,
                configWarnings = result.Value.ConfigWarnings
            });
    }

    /// <summary>
    /// PUT /api/template-admin/{id}（multipart：可选 file、name、docType、categories、configJson、copies）
    /// → 200 { configWarnings }。
    /// file 非空时替换 file_data / hash / type；copies 只改 config_json.copies（1-99），保留其他键。
    /// </summary>
    private static async Task<IResult> UpdateAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        TemplateAdminService templates,
        CancellationToken ct,
        [FromForm] string? name,
        [FromForm] string? docType,
        [FromForm] string? categories,
        [FromForm] string? configJson,
        [FromForm] int? copies,
        IFormFile? file)
    {
        var denied = await RequireAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        var fileData = await ReadFileAsync(file, ct);
        var result = await templates.UpdateAsync(
            id, name, docType, categories, configJson, copies, fileData, file?.FileName, ct);

        return result.IsFailure
            ? MapResult(context, result)
            : Results.Json(new { configWarnings = result.Value });
    }

    /// <summary>DELETE /api/template-admin/{id} → 物理删除。</summary>
    private static async Task<IResult> DeleteAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        TemplateAdminService templates,
        CancellationToken ct)
    {
        var denied = await RequireAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await templates.DeleteAsync(id, ct);
        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    /// <summary>POST /api/template-admin/{id}/move?direction=up|down → 交换 sort_order。</summary>
    private static async Task<IResult> MoveAsync(
        long id,
        HttpContext context,
        INewPermissionService permissions,
        TemplateAdminService templates,
        CancellationToken ct,
        string? direction)
    {
        var denied = await RequireAsync(context, permissions, ct);
        if (denied is not null)
        {
            return denied;
        }

        var result = await templates.MoveAsync(id, direction, ct);
        return result.IsFailure ? MapResult(context, result) : Results.NoContent();
    }

    private static Task<IResult?> RequireAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
        => EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.TEMPLATE_MANAGE, ct);

    private static async Task<byte[]?> ReadFileAsync(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length <= 0)
        {
            return null;
        }

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static string BuildDownloadName(string? name, bool isExcel)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "模板" : name.Trim();
        if (baseName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
            || baseName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            baseName = Path.GetFileNameWithoutExtension(baseName);
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            baseName = baseName.Replace(invalid, '_');
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "模板";
        }

        return baseName + (isExcel ? ".xlsx" : ".docx");
    }

    private static IResult MapResult(HttpContext context, Result result)
    {
        if (result.ErrorCode is ErrorCodes.TEMPLATE_NOT_FOUND
            or ErrorCodes.FILE_NOT_FOUND
            or ErrorCodes.RECORD_NOT_FOUND
            or ErrorCodes.NOT_FOUND
            or TemplateAdminService.CategoryNotFound)
        {
            return NotFound(context, result.Message ?? "请求的资源不存在");
        }

        if (result.ErrorCode is TemplateAdminService.CategoryHasChildren
            or TemplateAdminService.CategoryInUse
            or TemplateAdminService.CategoryDuplicate)
        {
            return Conflict(context, result.Message ?? "分类操作冲突");
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

    private static IResult Conflict(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status409Conflict,
            ApiErrorCodes.ValidationError, message);

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound, message);
}
