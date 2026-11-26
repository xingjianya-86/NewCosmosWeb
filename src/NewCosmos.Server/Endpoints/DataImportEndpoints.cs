using NewCosmos.Constants;
using NewCosmos.Server.Configuration;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services.DataCenter;
using NewCosmos.Services.Core;
using NewCosmos.Services.Import;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 数据导入（前端「数据导入」页）：类型下拉、模板下载、Excel 预览、后台导入、导入日志。
/// <para>
/// 权限：除任务日志（仅发起人可读）外，全部要求 <c>SYSTEM_IMPORT</c>。
/// 对齐 PC <c>Pages/DatabaseManagement/DataImportPage.xaml</c> + <c>DataImportViewModel</c>。
/// </para>
/// </summary>
public static class DataImportEndpoints
{
    private const string Prefix = "/api/system/import";
    private const int MaxFilesPerRequest = 50;

    private static readonly string[] ExcelExtensions = [".xlsx", ".xls"];

    public static IEndpointRouteBuilder MapDataImportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{Prefix}/types", GetTypes);
        app.MapGet($"{Prefix}/templates/{{importType}}", DownloadTemplateAsync);
        app.MapPost($"{Prefix}/preview", PreviewAsync).DisableAntiforgery();
        app.MapPost($"{Prefix}/run", StartImportAsync).DisableAntiforgery();
        app.MapGet($"{Prefix}/jobs/{{jobId}}", GetJobSummary);
        app.MapGet($"{Prefix}/jobs/{{jobId}}/log", GetJobLog);
        return app;
    }

    // ───────────────────────── 导入类型 ─────────────────────────

    private static async Task<IResult> GetTypes(
        HttpContext context,
        INewPermissionService permissions,
        DataImportService service,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        return Results.Json(service.GetTypes());
    }

    // ───────────────────────── 模板 ─────────────────────────

    private static async Task<IResult> DownloadTemplateAsync(
        string importType,
        HttpContext context,
        INewPermissionService permissions,
        DataImportService service,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var normalized = NormalizeImportType(importType);
        var export = await service.ExportTemplateAsync(normalized, ct);
        if (export.FilePath is null)
        {
            return Failure(context, export.ErrorMessage ?? "模板导出失败");
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(export.FilePath, ct);
            return Results.File(
                bytes,
                GetExcelContentType(export.FileName),
                export.FileName ?? "导入模板.xlsx");
        }
        catch (IOException ex)
        {
            return Failure(context, $"读取模板文件失败：{ex.Message}");
        }
        finally
        {
            service.DeleteWorkingDirectory(export.Directory);
        }
    }

    // ───────────────────────── 预览 ─────────────────────────

    private static async Task<IResult> PreviewAsync(
        HttpContext context,
        INewPermissionService permissions,
        DataCenterOptions options,
        DataImportService service,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        var (form, formError) = await ReadFormAsync(context, ct);
        if (form is null)
        {
            return formError!;
        }

        var importType = NormalizeImportType(form["importType"].ToString());
        if (!service.IsImplementedType(importType))
        {
            return Failure(
                context,
                string.IsNullOrWhiteSpace(importType) ? "请先选择导入类型" : $"未知的导入类型：{importType}");
        }

        if (form.Files.Count > MaxFilesPerRequest)
        {
            return Failure(context, $"单次最多上传 {MaxFilesPerRequest} 个文件");
        }

        var directory = service.CreateWorkingDirectory("preview");
        try
        {
            if (ImportTypeCodes.IsCombinedType(importType))
            {
                var familyUpload = form.Files.GetFile("familyFile");
                var personUpload = form.Files.GetFile("personFile");
                var extras = form.Files
                    .Where(file => !ReferenceEquals(file, familyUpload) && !ReferenceEquals(file, personUpload))
                    .ToList();

                string? familyPath = null;
                string? personPath = null;
                if (familyUpload is not null)
                {
                    familyPath = await SaveUploadAsync(familyUpload, directory, options, ct);
                }

                if (personUpload is not null)
                {
                    personPath = await SaveUploadAsync(personUpload, directory, options, ct);
                }

                // 额外的 files 一并落盘，供「按文件名模式自动匹配」使用。
                foreach (var extra in extras)
                {
                    await SaveUploadAsync(extra, directory, options, ct);
                }

                var combinedResponse = await service.PreviewAsync(
                    importType, familyPath, personPath, [], directory, ct);
                return combinedResponse.Success
                    ? Results.Json(combinedResponse)
                    : Failure(context, combinedResponse.ErrorMessage ?? "预览失败");
            }

            var uploads = form.Files
                .Where(file => file.Name is "files" or "file")
                .ToList();
            if (uploads.Count == 0)
            {
                return Failure(context, "请至少选择一个 Excel 文件");
            }

            var paths = new List<string>(uploads.Count);
            foreach (var upload in uploads)
            {
                paths.Add(await SaveUploadAsync(upload, directory, options, ct));
            }

            var singleResponse = await service.PreviewAsync(
                importType, null, null, paths, directory, ct);
            return singleResponse.Success
                ? Results.Json(singleResponse)
                : Failure(context, singleResponse.ErrorMessage ?? "预览失败");
        }
        catch (UploadValidationException ex)
        {
            return Failure(context, ex.Message);
        }
        catch (IOException ex)
        {
            return Failure(context, $"保存上传文件失败：{ex.Message}");
        }
        finally
        {
            service.DeleteWorkingDirectory(directory);
        }
    }

    // ───────────────────────── 导入任务 ─────────────────────────

    private static async Task<IResult> StartImportAsync(
        HttpContext context,
        INewPermissionService permissions,
        DataCenterOptions options,
        DataImportService service,
        DataCenterJobRegistry jobs,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var (form, formError) = await ReadFormAsync(context, ct);
        if (form is null)
        {
            return formError!;
        }

        var importType = NormalizeImportType(form["importType"].ToString());
        if (!service.IsImplementedType(importType))
        {
            return Failure(
                context,
                string.IsNullOrWhiteSpace(importType) ? "请先选择导入类型" : $"未知的导入类型：{importType}");
        }

        var combined = ImportTypeCodes.IsCombinedType(importType);
        var clearBeforeImport = ParseBool(form["clearBeforeImport"].ToString());

        if (form.Files.Count > MaxFilesPerRequest)
        {
            return Failure(context, $"单次最多上传 {MaxFilesPerRequest} 个文件");
        }

        if (combined && form.Files.Count == 0)
        {
            return Failure(context, "请提供家庭文件与人员文件");
        }

        var supportsScope = !combined && service.SupportsScope(importType);

        if (!DataImportService.TryBuildScope(
                form["year"].ToString(),
                form["rangeStart"].ToString(),
                form["rangeEnd"].ToString(),
                clearBeforeImport,
                supportsScope,
                out var scope,
                out var scopeError))
        {
            return Failure(context, scopeError!);
        }

        IFormFile? familyUpload = null;
        IFormFile? personUpload = null;
        var extras = new List<IFormFile>();
        var singleUploads = new List<IFormFile>();

        if (combined)
        {
            familyUpload = form.Files.GetFile("familyFile");
            personUpload = form.Files.GetFile("personFile");
            foreach (var file in form.Files)
            {
                if (!ReferenceEquals(file, familyUpload) && !ReferenceEquals(file, personUpload))
                {
                    extras.Add(file);
                }
            }

            ValidateUploadIfPresent(familyUpload, options);
            ValidateUploadIfPresent(personUpload, options);
            foreach (var extra in extras)
            {
                ValidateUpload(extra, options);
            }
        }
        else
        {
            singleUploads = form.Files
                .Where(file => file.Name is "files" or "file")
                .ToList();
            if (singleUploads.Count == 0)
            {
                return Failure(context, "请至少选择一个 Excel 文件");
            }

            foreach (var upload in singleUploads)
            {
                ValidateUpload(upload, options);
            }
        }

        if (!service.TryStartJob(importType, userId, out var jobId, out var jobError))
        {
            return Conflict(context, jobError!);
        }

        string directory;
        try
        {
            directory = service.CreateJobDirectory(jobId);
        }
        catch (Exception ex)
        {
            jobs.Fail(jobId, $"创建导入临时目录失败：{ex.Message}");
            return ApiErrors.Write(
                context, StatusCodes.Status500InternalServerError,
                ApiErrorCodes.InternalError, $"创建导入临时目录失败：{ex.Message}");
        }

        try
        {
            string? familyPath = null;
            string? personPath = null;
            var filePaths = new List<string>();

            if (combined)
            {
                if (familyUpload is not null)
                {
                    familyPath = await SaveUploadAsync(familyUpload, directory, options, ct);
                }

                if (personUpload is not null)
                {
                    personPath = await SaveUploadAsync(personUpload, directory, options, ct);
                }

                // 额外文件用于任务内按文件名模式自动匹配。
                foreach (var extra in extras)
                {
                    await SaveUploadAsync(extra, directory, options, ct);
                }
            }
            else
            {
                foreach (var upload in singleUploads)
                {
                    filePaths.Add(await SaveUploadAsync(upload, directory, options, ct));
                }
            }

            var request = new DataImportService.ImportJobRequest(
                jobId,
                importType,
                directory,
                familyPath,
                personPath,
                filePaths,
                scope,
                clearBeforeImport,
                userId,
                CurrentUserContext.FullName);

            _ = Task.Run(() => service.RunImportJobAsync(request), CancellationToken.None);

            return Results.Json(new ImportJobStartedDto(jobId), statusCode: StatusCodes.Status202Accepted);
        }
        catch (Exception ex)
        {
            // 任务已占用全局槽位：任何落盘失败都必须释放，避免后续任务被永久阻塞。
            service.DeleteWorkingDirectory(directory);
            jobs.Fail(jobId, $"保存导入文件失败：{ex.Message}");
            return Failure(context, $"保存导入文件失败：{ex.Message}");
        }
    }

    // ───────────────────────── 任务结果 / 日志 ─────────────────────────

    private static async Task<IResult> GetJobSummary(
        string jobId,
        HttpContext context,
        INewPermissionService permissions,
        DataImportService service,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var summary = service.GetJobSummary(jobId, userId);
        return summary is null
            ? NotFound(context, "任务不存在、已过期或无权查看")
            : Results.Json(summary);
    }

    private static IResult GetJobLog(
        string jobId,
        HttpContext context,
        DataImportService service)
    {
        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var log = service.GetJobLog(jobId, userId);
        if (log is null)
        {
            return NotFound(context, "任务不存在、已过期或无权查看");
        }

        return Results.File(log.Content, "text/plain; charset=utf-8", log.FileName);
    }

    // ───────────────────────── 上传辅助 ─────────────────────────

    private static async Task<(IFormCollection? Form, IResult? Error)> ReadFormAsync(
        HttpContext context,
        CancellationToken ct)
    {
        if (!context.Request.HasFormContentType)
        {
            return (null, Failure(context, "请使用 multipart/form-data 上传 Excel 文件"));
        }

        try
        {
            return (await context.Request.ReadFormAsync(ct), null);
        }
        catch (InvalidDataException)
        {
            return (null, Failure(context, "上传的表单数据无效，请重新选择文件"));
        }
    }

    private static async Task<string> SaveUploadAsync(
        IFormFile file,
        string directory,
        DataCenterOptions options,
        CancellationToken ct)
    {
        ValidateUpload(file, options);

        var safeName = SanitizeFileName(file.FileName);
        var extension = Path.GetExtension(safeName);
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var target = Path.Combine(directory, safeName);
        var index = 2;
        while (File.Exists(target))
        {
            target = Path.Combine(directory, $"{stem}_{index++}{extension}");
        }

        await using var stream = new FileStream(
            target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
        await file.CopyToAsync(stream, ct);
        return target;
    }

    private static void ValidateUploadIfPresent(IFormFile? file, DataCenterOptions options)
    {
        if (file is not null)
        {
            ValidateUpload(file, options);
        }
    }

    private static void ValidateUpload(IFormFile file, DataCenterOptions options)
    {
        var name = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new UploadValidationException("上传文件名无效");
        }

        if (!ExcelExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
        {
            throw new UploadValidationException($"文件「{name}」格式不支持，仅允许 .xlsx / .xls");
        }

        if (file.Length <= 0)
        {
            throw new UploadValidationException($"文件「{name}」为空，请重新选择");
        }

        if (file.Length > options.MaxImportFileBytes)
        {
            throw new UploadValidationException(
                $"文件「{name}」超过单文件上限（{FormatSize(options.MaxImportFileBytes)}）");
        }
    }

    /// <summary>去路径、去非法字符、限长 120（保留扩展名），保证落盘名安全。</summary>
    private static string SanitizeFileName(string rawFileName)
    {
        var name = Path.GetFileName(rawFileName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
        {
            name = "import.xlsx";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = new string(name.Where(ch => !char.IsControl(ch)).ToArray());

        if (name.Length > 120)
        {
            var extension = Path.GetExtension(name);
            var stem = Path.GetFileNameWithoutExtension(name);
            var maxStem = Math.Max(1, 120 - extension.Length);
            name = stem[..Math.Min(stem.Length, maxStem)] + extension;
        }

        return string.IsNullOrWhiteSpace(name) ? "import.xlsx" : name;
    }

    // ───────────────────────── 辅助 ─────────────────────────

    private static async Task<IResult?> DenyAsync(
        HttpContext context,
        INewPermissionService permissions,
        CancellationToken ct)
        => await EndpointSecurity.RequirePermissionAsync(
            context, permissions, PermissionCodes.SYSTEM_IMPORT, ct);

    private static string NormalizeImportType(string? importType)
        => (importType ?? string.Empty).Trim().ToUpperInvariant();

    private static bool ParseBool(string? raw)
    {
        var value = raw?.Trim();
        return value is not null
               && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("1", StringComparison.Ordinal)
                   || value.Equals("on", StringComparison.OrdinalIgnoreCase)
                   || value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetExcelContentType(string? fileName)
        => string.Equals(Path.GetExtension(fileName), ".xls", StringComparison.OrdinalIgnoreCase)
            ? "application/vnd.ms-excel"
            : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    private static IResult Failure(HttpContext context, string message, int status = 400)
        => ApiErrors.Write(context, status, ApiErrorCodes.ValidationError, message);

    private static IResult Conflict(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status409Conflict, ApiErrorCodes.ValidationError, message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired, "请先登录");

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status404NotFound, ApiErrorCodes.NotFound, message);

    /// <summary>上传文件校验失败（转为 400，不进入业务）。</summary>
    private sealed class UploadValidationException(string message) : Exception(message);
}
