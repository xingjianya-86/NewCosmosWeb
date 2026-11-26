using System.Globalization;

namespace NewCosmos.Server.Configuration;

/// <summary>
/// 数据中心运行配置：备份、日志、导入、Schema 状态文件与 PostgreSQL 客户端工具。
/// 全部来自环境变量；容器路径（/srv/newcosmos/...）由 docker-compose 挂载持久卷。
/// </summary>
public sealed class DataCenterOptions
{
    public required string BackupRoot { get; init; }
    public required string LogRoot { get; init; }
    public required string ImportRoot { get; init; }
    public required string StateRoot { get; init; }

    /// <summary>备份保留天数；0 = 不自动清理。</summary>
    public required int BackupRetentionDays { get; init; }

    /// <summary>是否允许从 Web 触发数据库还原。</summary>
    public required bool AllowRestore { get; init; }

    /// <summary>单个 Excel 导入文件上限（字节）。</summary>
    public required long MaxImportFileBytes { get; init; }

    public required string PgDumpPath { get; init; }
    public required string PsqlPath { get; init; }

    /// <summary>还原期间维护模式的放行路径（仅健康检查；任务轮询在维护结束后恢复）。</summary>
    public static readonly string[] MaintenanceAllowedPrefixes =
    [
        "/health"
    ];

    public static DataCenterOptions Load()
    {
        var backupRoot = GetString("NC_DATACENTER_BACKUP_ROOT", "/srv/newcosmos/backups");
        var logRoot = GetString("NC_DATACENTER_LOG_ROOT", "/app/logs");
        var importRoot = GetString("NC_DATACENTER_IMPORT_ROOT", "/tmp/newcosmos-imports");
        var stateRoot = GetString("NC_DATACENTER_STATE_ROOT", "/srv/newcosmos/state");
        var retention = Math.Clamp(GetInt("NC_DATACENTER_BACKUP_RETENTION_DAYS", 30), 0, 3650);
        var importMb = Math.Clamp(GetInt("NC_DATACENTER_IMPORT_MAX_MB", 128), 1, 512);

        return new DataCenterOptions
        {
            BackupRoot = backupRoot,
            LogRoot = logRoot,
            ImportRoot = importRoot,
            StateRoot = stateRoot,
            BackupRetentionDays = retention,
            AllowRestore = GetBool("NC_DATACENTER_ALLOW_RESTORE", true),
            MaxImportFileBytes = importMb * 1024L * 1024L,
            PgDumpPath = GetString("NC_DATACENTER_PG_DUMP", "pg_dump"),
            PsqlPath = GetString("NC_DATACENTER_PSQL", "psql")
        };
    }

    private static string GetString(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static int GetInt(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static bool GetBool(string name, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name)?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return fallback;
        }

        return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || raw.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
