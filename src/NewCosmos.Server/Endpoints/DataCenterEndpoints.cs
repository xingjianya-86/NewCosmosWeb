using NewCosmos.Constants;
using NewCosmos.Server.Configuration;
using NewCosmos.Server.Contracts;
using NewCosmos.Server.Infrastructure;
using NewCosmos.Server.Services.DataCenter;
using NewCosmos.Services.Core;
using NewCosmos.Services.UserManagement;
using Npgsql;

namespace NewCosmos.Server.Endpoints;

/// <summary>
/// 数据中心：数据库状态、Schema、备份还原、日志清理、软删除清理、登录端版本、后台任务。
/// 权限：查看与维护 SYSTEM_BACKUP；还原 SYSTEM_RESTORE。
/// </summary>
public static class DataCenterEndpoints
{
    private const string Prefix = "/api/system/data-center";

    public static IEndpointRouteBuilder MapDataCenterEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{Prefix}/overview", GetOverviewAsync);
        app.MapGet($"{Prefix}/schema", GetSchemaAsync);
        app.MapPost($"{Prefix}/schema/initialize", InitializeSchemaAsync);
        app.MapPost($"{Prefix}/schema/fix", FixSchemaAsync);

        app.MapGet($"{Prefix}/backups", GetBackupsAsync);
        app.MapPost($"{Prefix}/backups", StartBackupAsync);
        app.MapGet($"{Prefix}/backups/{{fileName}}/download", DownloadBackupAsync);
        app.MapPost($"{Prefix}/backups/{{fileName}}/restore", RestoreBackupAsync);
        app.MapPost($"{Prefix}/backups/upload", UploadBackupAsync).DisableAntiforgery();

        app.MapPost($"{Prefix}/maintenance/logs", CleanupLogsAsync);
        app.MapGet($"{Prefix}/soft-delete", DetectSoftDeleteAsync);
        app.MapPost($"{Prefix}/soft-delete/cleanup", CleanupSoftDeletedAsync);

        app.MapGet($"{Prefix}/clients", GetClientsAsync);

        app.MapGet($"{Prefix}/jobs/{{jobId}}", GetJobAsync);
        app.MapPost($"{Prefix}/jobs/{{jobId}}/cancel", CancelJobAsync);
        return app;
    }

    // ───────────────────────── 查询 ─────────────────────────

    private static async Task<IResult> GetOverviewAsync(
        HttpContext context,
        INewPermissionService permissions,
        DataCenterService service,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.SYSTEM_BACKUP, ct) is { } denied)
        {
            return denied;
        }

        try
        {
            return Results.Json(await service.GetOverviewAsync(ct));
        }
        catch (Exception ex)
        {
            return Failure(context, ex.Message);
        }
    }

    private static async Task<IResult> GetSchemaAsync(
        HttpContext context,
        INewPermissionService permissions,
        DataCenterService service,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.SYSTEM_BACKUP, ct) is { } denied)
        {
            return denied;
        }

        try
        {
            return Results.Json(await service.GetSchemaStatusAsync(ct));
        }
        catch (Exception ex)
        {
            return Failure(context, ex.Message);
        }
    }

    private static async Task<IResult> GetBackupsAsync(
        HttpContext context,
        INewPermissionService permissions,
        DataCenterService service,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.SYSTEM_BACKUP, ct) is { } denied)
        {
            return denied;
        }

        try
        {
            return Results.Json(await service.GetBackupFilesAsync(ct));
        }
        catch (Exception ex)
        {
            return Failure(context, ex.Message);
        }
    }

    private static async Task<IResult> GetClientsAsync(
        HttpContext context,
        INewPermissionService permissions,
        DataCenterService service,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.SYSTEM_BACKUP, ct) is { } denied)
        {
            return denied;
        }

        try
        {
            return Results.Json(await service.GetClientsAsync(ct));
        }
        catch (Exception ex)
        {
            return Failure(context, ex.Message);
        }
    }

    private static async Task<IResult> DetectSoftDeleteAsync(
        HttpContext context,
        INewPermissionService permissions,
        DataCenterService service,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.SYSTEM_BACKUP, ct) is { } denied)
        {
            return denied;
        }

        try
        {
            return Results.Json(await service.DetectSoftDeleteAsync(ct));
        }
        catch (Exception ex)
        {
            return Failure(context, ex.Message);
        }
    }

    // ───────────────────────── Schema 任务 ─────────────────────────

    private static async Task<IResult> InitializeSchemaAsync(
        HttpContext context,
        INewPermissionService permissions,
        DataCenterService service,
        DataCenterJobRegistry jobs,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.SYSTEM_BACKUP, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!jobs.TryStart("schema-initialize", "初始化缺失表", userId,
                out var job, out var error))
        {
            return Conflict(context, error!);
        }

        var jobId = job.JobId;
        var operatorName = CurrentUserContext.FullName;
        _ = Task.Run(async () =>
        {
            try
            {
                jobs.Start(jobId, "正在检查并创建缺失表…");
                var missing = await service.InitializeSchemaAsync(
                    string.IsNullOrWhiteSpace(operatorName) ? $"用户#{userId}" : operatorName,
                    jobs.GetCancellationToken(jobId));
                jobs.Complete(jobId, $"初始化完成：处理 {missing} 张缺失表");
            }
            catch (Exception ex)
            {
                jobs.Fail(jobId, ex.Message);
            }
        }, CancellationToken.None);

        return Results.Json(new StartJobResponse(jobId, "初始化任务已启动"), statusCode: 202);
    }

    private static async Task<IResult> FixSchemaAsync(
        HttpContext context,
        INewPermissionService permissions,
        DataCenterService service,
        DataCenterJobRegistry jobs,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.SYSTEM_BACKUP, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!jobs.TryStart("schema-fix", "修复缺失列", userId, out var job, out var error))
        {
            return Conflict(context, error!);
        }

        var jobId = job.JobId;
        _ = Task.Run(async () =>
        {
            try
            {
                jobs.Start(jobId, "正在校验 Schema 差异…");
                var fix = await service.FixSchemaAsync(jobs.GetCancellationToken(jobId));
                if (fix.FailedCount > 0)
                {
                    jobs.Fail(jobId, $"{fix.Summary}；失败：{string.Join("；", fix.FailedItems.Take(5))}");
                }
                else
                {
                    jobs.Complete(jobId, fix.Summary);
                }
            }
            catch (Exception ex)
            {
                jobs.Fail(jobId, ex.Message);
            }
        }, CancellationToken.None);

        return Results.Json(new StartJobResponse(jobId, "修复任务已启动"), statusCode: 202);
    }

    // ───────────────────────── 备份 ─────────────────────────

    private static async Task<IResult> StartBackupAsync(
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseBackupService backup,
        DataCenterOptions options,
        DataCenterJobRegistry jobs,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.SYSTEM_BACKUP, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!jobs.TryStart("backup", "数据库备份", userId, out var job, out var error))
        {
            return Conflict(context, error!);
        }

        var jobId = job.JobId;
        _ = Task.Run(async () =>
        {
            try
            {
                jobs.Start(jobId, "正在执行 pg_dump…");
                var result = await backup.BackupDatabaseAsync(
                    options.BackupRoot, jobs.GetCancellationToken(jobId));
                if (result.IsFailure)
                {
                    jobs.Fail(jobId, result.Message ?? "备份失败");
                    return;
                }

                var name = Path.GetFileName(result.Value);
                var size = File.Exists(result.Value) ? new FileInfo(result.Value).Length : 0;
                jobs.Complete(jobId, $"备份完成：{name}（{FormatSize(size)}）");
            }
            catch (Exception ex)
            {
                jobs.Fail(jobId, ex.Message);
            }
        }, CancellationToken.None);

        return Results.Json(new StartJobResponse(jobId, "备份任务已启动"), statusCode: 202);
    }

    private static async Task<IResult> DownloadBackupAsync(
        string fileName,
        HttpContext context,
        INewPermissionService permissions,
        DataCenterOptions options,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.SYSTEM_BACKUP, ct) is { } denied)
        {
            return denied;
        }

        if (!TryResolveBackupPath(options, fileName, out var path))
        {
            return NotFound(context, "备份文件不存在");
        }

        return Results.File(path, "application/sql", fileName, enableRangeProcessing: true);
    }

    private static async Task<IResult> RestoreBackupAsync(
        string fileName,
        RestoreRequest request,
        HttpContext context,
        INewPermissionService permissions,
        IDatabaseBackupService backup,
        DataCenterOptions options,
        DataCenterJobRegistry jobs,
        MaintenanceState maintenance,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.SYSTEM_RESTORE, ct) is { } denied)
        {
            return denied;
        }

        if (!options.AllowRestore)
        {
            return Failure(context, "服务端已关闭 Web 还原（NC_DATACENTER_ALLOW_RESTORE=false）", 403);
        }

        if (!TryResolveBackupPath(options, fileName, out var path))
        {
            return NotFound(context, "备份文件不存在");
        }

        if (!string.Equals(request.ConfirmFileName?.Trim(), fileName, StringComparison.Ordinal))
        {
            return Failure(context, "确认文件名与所选备份不一致，已拒绝还原");
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!jobs.TryStart("restore", "数据库还原", userId, out var job, out var error))
        {
            return Conflict(context, error!);
        }

        var jobId = job.JobId;
        _ = Task.Run(async () =>
        {
            try
            {
                jobs.Start(jobId, "正在创建还原前自动备份…");
                var preBackup = await backup.BackupDatabaseAsync(
                    options.BackupRoot, jobs.GetCancellationToken(jobId));
                if (preBackup.IsFailure)
                {
                    jobs.Fail(jobId, $"还原前自动备份失败，已中止：{preBackup.Message}");
                    return;
                }

                jobs.Report(jobId, $"还原前备份完成：{Path.GetFileName(preBackup.Value)}", 20);
                jobs.Report(jobId, "进入维护模式，正在还原数据库…", 30);
                maintenance.Enter();
                try
                {
                    var restore = await backup.RestoreDatabaseAsync(
                        path, jobs.GetCancellationToken(jobId));
                    if (restore.IsFailure)
                    {
                        jobs.Fail(jobId, restore.Message ?? "还原失败");
                        return;
                    }
                }
                finally
                {
                    NpgsqlConnection.ClearAllPools();
                    maintenance.Exit();
                }

                jobs.Complete(jobId,
                    $"还原完成：{fileName}；还原前备份：{Path.GetFileName(preBackup.Value)}");
            }
            catch (Exception ex)
            {
                maintenance.Exit();
                logger.LogSecurity("数据中心-还原异常", ("FileName", fileName), ("Error", ex.Message));
                jobs.Fail(jobId, ex.Message);
            }
        }, CancellationToken.None);

        return Results.Json(
            new StartJobResponse(jobId, "还原任务已启动，期间其它页面暂时不可用"),
            statusCode: 202);
    }

    private static async Task<IResult> UploadBackupAsync(
        HttpContext context,
        INewPermissionService permissions,
        DataCenterOptions options,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.SYSTEM_RESTORE, ct) is { } denied)
        {
            return denied;
        }

        if (!options.AllowRestore)
        {
            return Failure(context, "服务端已关闭 Web 还原", 403);
        }

        if (!context.Request.HasFormContentType)
        {
            return Failure(context, "请使用 multipart/form-data 上传备份文件");
        }

        var form = await context.Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return Failure(context, "请选择要上传的 .sql 备份文件");
        }

        var originalName = Path.GetFileName(file.FileName);
        if (!originalName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(context, "仅支持 .sql 格式的 pg_dump 备份");
        }

        if (file.Length > 512L * 1024 * 1024)
        {
            return Failure(context, "备份文件超过 512MB 限制");
        }

        Directory.CreateDirectory(options.BackupRoot);
        var targetName = $"upload_{DateTime.Now:yyyyMMdd_HHmmss}_{originalName}";
        var targetPath = Path.Combine(options.BackupRoot, targetName);
        await using (var stream = File.Create(targetPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        return Results.Json(new BackupFileDto(
            targetName,
            file.Length,
            FormatSize(file.Length),
            DateTime.Now));
    }

    // ───────────────────────── 日志 / 软删除任务 ─────────────────────────

    private static async Task<IResult> CleanupLogsAsync(
        HttpContext context,
        INewPermissionService permissions,
        DataCenterService service,
        DataCenterJobRegistry jobs,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.SYSTEM_BACKUP, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!jobs.TryStart("logs", "清理过期日志", userId, out var job, out var error))
        {
            return Conflict(context, error!);
        }

        var jobId = job.JobId;
        _ = Task.Run(async () =>
        {
            try
            {
                jobs.Start(jobId, "正在清理 app 30 / biz 180 / sec 365 / err 90 / perf 30…");
                var deleted = await service.CleanupOldLogsAsync(jobs.GetCancellationToken(jobId));
                jobs.Complete(jobId, $"已清理 {deleted} 个过期日志文件");
            }
            catch (Exception ex)
            {
                jobs.Fail(jobId, ex.Message);
            }
        }, CancellationToken.None);

        return Results.Json(new StartJobResponse(jobId, "日志清理任务已启动"), statusCode: 202);
    }

    private static async Task<IResult> CleanupSoftDeletedAsync(
        HttpContext context,
        INewPermissionService permissions,
        DataCenterService service,
        DataCenterJobRegistry jobs,
        CancellationToken ct)
    {
        if (await DenyAsync(context, permissions, PermissionCodes.SYSTEM_BACKUP, ct) is { } denied)
        {
            return denied;
        }

        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        if (!jobs.TryStart("soft-delete", "清理软删除数据", userId, out var job, out var error))
        {
            return Conflict(context, error!);
        }

        var jobId = job.JobId;
        _ = Task.Run(async () =>
        {
            try
            {
                jobs.Start(jobId, "正在创建清理前备份…");
                jobs.Report(jobId, "备份完成后开始事务内清理…", 20);
                var result = await service.CleanupSoftDeletedAsync(jobs.GetCancellationToken(jobId));
                jobs.Complete(jobId, result.Message);
            }
            catch (Exception ex)
            {
                jobs.Fail(jobId, ex.Message);
            }
        }, CancellationToken.None);

        return Results.Json(new StartJobResponse(jobId, "软删除清理任务已启动"), statusCode: 202);
    }

    // ───────────────────────── 任务 ─────────────────────────

    private static IResult GetJobAsync(
        string jobId,
        HttpContext context,
        DataCenterJobRegistry jobs)
    {
        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        var job = jobs.Get(jobId, userId);
        return job is null ? NotFound(context, "任务不存在或已过期") : Results.Json(ToDto(job));
    }

    private static IResult CancelJobAsync(
        string jobId,
        HttpContext context,
        DataCenterJobRegistry jobs)
    {
        if (context.GetUserId() is not { } userId)
        {
            return Unauthorized(context);
        }

        return jobs.TryRequestCancel(jobId, userId, out var error)
            ? Results.Json(new { message = "已发送取消请求" })
            : Failure(context, error!);
    }

    private static DataCenterJobDto ToDto(DataCenterJobSnapshot job)
        => new(
            job.JobId,
            job.Kind,
            job.Title,
            job.State.ToString().ToLowerInvariant(),
            job.Percent,
            job.Message,
            job.StartedAt,
            job.FinishedAt,
            job.Error,
            job.Logs);

    // ───────────────────────── 辅助 ─────────────────────────

    private static async Task<IResult?> DenyAsync(
        HttpContext context,
        INewPermissionService permissions,
        string permission,
        CancellationToken ct)
        => await EndpointSecurity.RequirePermissionAsync(context, permissions, permission, ct);

    private static bool TryResolveBackupPath(
        DataCenterOptions options,
        string fileName,
        out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            !fileName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidate = Path.Combine(options.BackupRoot, fileName);
        if (!File.Exists(candidate))
        {
            return false;
        }

        path = candidate;
        return true;
    }

    private static IResult Failure(HttpContext context, string message, int status = 400)
        => ApiErrors.Write(context, status, ApiErrorCodes.ValidationError, message);

    private static IResult Conflict(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status409Conflict, ApiErrorCodes.ValidationError, message);

    private static IResult Unauthorized(HttpContext context)
        => ApiErrors.Write(context, StatusCodes.Status401Unauthorized,
            ApiErrorCodes.AuthRequired, "请先登录");

    private static IResult NotFound(HttpContext context, string message)
        => ApiErrors.Write(context, StatusCodes.Status404NotFound,
            ApiErrorCodes.NotFound, message);

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    /// <summary>还原确认请求体。</summary>
    public sealed record RestoreRequest(string? ConfirmFileName);
}
