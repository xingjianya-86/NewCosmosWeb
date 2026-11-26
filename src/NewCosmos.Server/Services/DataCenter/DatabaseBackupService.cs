using System.Diagnostics;
using System.Text;
using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Server.Configuration;
using NewCosmos.Services.Core;
using Npgsql;

namespace NewCosmos.Server.Services.DataCenter;

/// <summary>
/// 服务端 pg_dump / psql 备份还原实现（对应 PC <c>DatabaseBackupService</c>）。
/// 与 PC 的差异：进程工具来自容器内 postgresql-client-16，备份目录为挂载卷；
/// 还原前由调用方进入维护模式并自动预备份，本服务只做单事务回放。
/// </summary>
public sealed class DatabaseBackupService(
    DatabaseOptions database,
    AppOptions app,
    DataCenterOptions dataCenter,
    ILoggerService logger)
    : IDatabaseBackupService
{
    private const string PgDumpHeader = "-- PostgreSQL database dump";

    public async Task<Result<string>> BackupDatabaseAsync(
        string backupPath,
        CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(backupPath);
            var fileName = $"{app.ApplicationName}_backup_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
            var fullPath = Path.Combine(backupPath, fileName);

            var result = await RunProcessAsync(
                dataCenter.PgDumpPath,
                $"-h {Quote(database.Host)} -p {database.Port} -U {Quote(database.Username)} " +
                $"-d {Quote(database.DatabaseName)} -F p -f {Quote(fullPath)}",
                ct);

            if (!result.Success)
            {
                TryDelete(fullPath);
                return Result.Failure<string>(ErrorCodes.DB_QUERY_ERROR,
                    $"pg_dump 执行失败：{result.Error}");
            }

            if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
            {
                TryDelete(fullPath);
                return Result.Failure<string>(ErrorCodes.DB_QUERY_ERROR, "pg_dump 未生成有效备份文件");
            }

            PruneExpiredBackups(backupPath);

            var size = new FileInfo(fullPath).Length;
            logger.LogBusiness("数据库备份完成",
                ("BackupFile", fileName),
                ("Size", FormatSize(size)));
            return Result.Success(fullPath);
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<string>(ErrorCodes.CANCELLED, "备份已取消");
        }
        catch (Exception ex)
        {
            logger.Error($"数据库备份异常: {ex.Message}");
            return Result.FromException<string>(ex);
        }
    }

    public async Task<Result<string>> RestoreDatabaseAsync(
        string backupFilePath,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
            {
                return Result.Failure<string>(ErrorCodes.FILE_NOT_FOUND,
                    $"备份文件不存在：{backupFilePath}");
            }

            var header = await ReadHeadAsync(backupFilePath, 2048, ct);
            if (header.IndexOf(PgDumpHeader, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return Result.Failure<string>(ErrorCodes.VALIDATION_FAILED,
                    "所选文件不是 pg_dump 生成的数据库备份，已拒绝还原");
            }

            var scriptPath = Path.Combine(Path.GetTempPath(),
                $"nc_restore_preamble_{Guid.NewGuid():N}.sql");
            try
            {
                await File.WriteAllTextAsync(scriptPath, BuildPreamble(), new UTF8Encoding(false), ct);

                // 清池 + 终止其它连接，避免 DROP SCHEMA 被长连接阻塞。
                NpgsqlConnection.ClearAllPools();
                await TerminateOtherConnectionsAsync(ct);
                NpgsqlConnection.ClearAllPools();

                var result = await RunProcessAsync(
                    dataCenter.PsqlPath,
                    $"-h {Quote(database.Host)} -p {database.Port} -U {Quote(database.Username)} " +
                    $"-d {Quote(database.DatabaseName)} --single-transaction --set ON_ERROR_STOP=1 " +
                    $"--no-psqlrc -f {Quote(scriptPath)} -f {Quote(backupFilePath)}",
                    ct);

                if (!result.Success)
                {
                    logger.LogSecurity("数据库还原失败",
                        ("BackupFile", Path.GetFileName(backupFilePath)));
                    return Result.Failure<string>(ErrorCodes.DB_QUERY_ERROR,
                        $"还原失败，已整体回滚，数据库保持原状态：{Truncate(result.Error, 800)}");
                }

                NpgsqlConnection.ClearAllPools();
                logger.LogSecurity("数据库还原成功",
                    ("BackupFile", Path.GetFileName(backupFilePath)),
                    ("Database", database.DatabaseName),
                    ("Size", FormatSize(new FileInfo(backupFilePath).Length)));
                return Result.Success(backupFilePath);
            }
            finally
            {
                TryDelete(scriptPath);
            }
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<string>(ErrorCodes.CANCELLED, "还原已取消");
        }
        catch (Exception ex)
        {
            logger.Error($"数据库还原异常: {ex.Message}");
            return Result.FromException<string>(ex);
        }
    }

    private async Task TerminateOtherConnectionsAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(BuildConnectionString());
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT pg_terminate_backend(pid)
              FROM pg_stat_activity
             WHERE datname = current_database()
               AND pid <> pg_backend_pid()
            """, connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private string BuildConnectionString() => new NpgsqlConnectionStringBuilder
    {
        Host = database.Host,
        Port = database.Port,
        Database = database.DatabaseName,
        Username = database.Username,
        Password = database.Password,
        Timeout = 15,
        CommandTimeout = 30
    }.ConnectionString;

    private string BuildPreamble()
    {
        var owner = database.Username.Replace("\"", "\"\"");
        return $"""
            -- NewCosmos restore preamble: drop and recreate public schema, then replay dump.
            SET statement_timeout = 0;
            SET lock_timeout = '30s';
            DROP SCHEMA IF EXISTS public CASCADE;
            CREATE SCHEMA public AUTHORIZATION "{owner}";
            GRANT ALL ON SCHEMA public TO "{owner}";
            """;
    }

    private async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken ct)
    {
        var pgPassPath = WritePgPassFile();
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment["PGPASSFILE"] = pgPassPath;

        try
        {
            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                return new ProcessResult(false,
                    $"无法启动 {fileName}（容器内是否安装 postgresql-client-16）：{ex.Message}");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return process.ExitCode == 0
                ? new ProcessResult(true, string.Empty)
                : new ProcessResult(false,
                    string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        }
        finally
        {
            TryDelete(pgPassPath);
        }
    }

    private string WritePgPassFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nc_pgpass_{Guid.NewGuid():N}");
        var line = string.Join(':',
            EscapePgPass(database.Host),
            database.Port.ToString(),
            EscapePgPass(database.DatabaseName),
            EscapePgPass(database.Username),
            EscapePgPass(database.Password));
        File.WriteAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return path;
    }

    private static string EscapePgPass(string value) =>
        value.Replace("\\", "\\\\").Replace(":", "\\:");

    private void PruneExpiredBackups(string backupPath)
    {
        if (dataCenter.BackupRetentionDays <= 0)
        {
            return;
        }

        try
        {
            var cutoff = DateTime.Now.AddDays(-dataCenter.BackupRetentionDays);
            var pattern = $"{app.ApplicationName}_backup_*.sql";
            foreach (var file in Directory.GetFiles(backupPath, pattern))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    TryDelete(file);
                }
            }
        }
        catch (Exception ex)
        {
            logger.Warn($"清理过期备份失败: {ex.Message}");
        }
    }

    private static async Task<string> ReadHeadAsync(string path, int maxBytes, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 4096, useAsync: true);
        var buffer = new byte[Math.Min(maxBytes, (int)Math.Min(stream.Length, maxBytes))];
        var read = await stream.ReadAsync(buffer, ct);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 临时文件清理失败不影响主流程。
        }
    }

    internal static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    private sealed record ProcessResult(bool Success, string Error);
}
