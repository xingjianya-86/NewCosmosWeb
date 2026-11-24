using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Server.Infrastructure;

/// <summary>
/// 启动时按文件名顺序执行 Migrations/*.sql（NC_AUTO_MIGRATE=1）。
/// 脚本约定只使用 CREATE ... IF NOT EXISTS，重复执行安全。
/// </summary>
public sealed class DatabaseMigrator(
    IDatabaseService database,
    ILoggerService logger,
    IHostEnvironment environment)
{
    private static readonly string MigrationDirectory = Path.Combine(AppContext.BaseDirectory, "Migrations");

    public async Task RunAsync(CancellationToken ct = default)
    {
        try
        {
            if (!Directory.Exists(MigrationDirectory))
            {
                logger.Error($"迁移目录不存在: {MigrationDirectory}");
                return;
            }

            var files = Directory.GetFiles(MigrationDirectory, "*.sql")
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .ToArray();

            if (files.Length == 0)
            {
                logger.Warn("迁移目录中没有 .sql 文件");
                return;
            }

            foreach (var file in files)
            {
                var sql = await File.ReadAllTextAsync(file, ct);
                var result = await database.ExecuteNonQueryAsync(sql, ct);
                if (result.IsFailure)
                {
                    logger.LogError(new InvalidOperationException(result.Message),
                        $"执行迁移失败: {Path.GetFileName(file)} ({result.ErrorCode})");
                    return;
                }

                logger.Info($"数据库迁移已执行: {Path.GetFileName(file)}（{environment.EnvironmentName}）");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "执行数据库迁移异常");
        }
    }
}
