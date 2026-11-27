using System.Globalization;
using System.Reflection;
using NewCosmos.Models.Options;

namespace NewCosmos.Server.Configuration;

/// <summary>
/// 服务端运行配置。全部来自环境变量，仓库内不保存数据库密码或 Agent 注册密钥。
/// </summary>
public sealed class ServerEnvironmentOptions
{
    public const string DefaultCorsOrigin = "https://cosmos.sua17.cn";

    public required DatabaseOptions Database { get; init; }
    public required StorageOptions Storage { get; init; }
    public required AppOptions App { get; init; }
    public required PerformanceOptions Performance { get; init; }
    public required DataCenterOptions DataCenter { get; init; }

    public required string[] CorsOrigins { get; init; }
    public required string AgentRegisterToken { get; init; }
    public required bool AutoMigrate { get; init; }
    public required string Mode { get; init; }
    public required int AgentHeartbeatSeconds { get; init; }
    public required long MaxRequestBodyBytes { get; init; }

    public static ServerEnvironmentOptions Load()
    {
        var mode = GetString("NC_MODE", string.Empty).Trim().ToLowerInvariant();
        if (mode is not ("test" or "prod"))
        {
            mode = string.Equals(GetString("ASPNETCORE_ENVIRONMENT", "Production"),
                "Development", StringComparison.OrdinalIgnoreCase)
                ? "test"
                : "prod";
        }

        var database = new DatabaseOptions
        {
            Type = "PostgreSQL",
            Host = RequireString("NC_DB_HOST"),
            Port = GetInt("NC_DB_PORT", 5432),
            DatabaseName = RequireString("NC_DB_NAME"),
            Username = RequireString("NC_DB_USER"),
            // 密码只允许来自环境变量，禁止在代码/appsettings 中写默认值。
            Password = RequireAny("NC_DB_PASS", "NC_DB_PASSWORD"),
            PgDumpPath = GetString("NC_PG_DUMP_PATH", string.Empty),
            RetentionDays = GetInt("NC_DB_BACKUP_RETENTION_DAYS", 30),
            ConnectionTimeout = GetInt("NC_DB_CONNECTION_TIMEOUT", 15),
            CommandTimeout = GetInt("NC_DB_COMMAND_TIMEOUT", 30),
            MaxPoolSize = GetInt("NC_DB_MAX_POOL", 30),
            MinPoolSize = GetInt("NC_DB_MIN_POOL", 2),
            IsProduction = mode == "prod",
            ApplicationName = "NewCosmos.Server",
            SslMode = GetString("NC_DB_SSL_MODE", "Prefer"),
            TrustServerCertificate = GetBool("NC_DB_TRUST_CERT", true),
            IncludeErrorDetail = GetBool("NC_DB_INCLUDE_ERROR_DETAIL", false)
        };
        database.Validate();

        var app = new AppOptions
        {
            Version = GetString("NC_APP_VERSION",
                typeof(ServerEnvironmentOptions).Assembly
                    .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion
                ?? typeof(ServerEnvironmentOptions).Assembly.GetName().Version?.ToString(3)
                ?? "2.0.20261127"),
            ApplicationName = GetString("NC_APP_NAME", "NewCosmos"),
            WindowTitle = "NewCosmos Server"
        };

        var storage = new StorageOptions
        {
            ApplicationName = app.ApplicationName,
            BackupDirectoryName = GetString("NC_BACKUP_DIR_NAME", "Backups"),
            // 可为绝对路径（如 /srv/newcosmos/logs）；相对路径时相对应用目录。
            LogDirectoryName = GetString("NC_LOG_DIR", "logs"),
            TempDirectoryName = "Temp",
            UseAppDataDirectory = false
        };

        var performance = new PerformanceOptions
        {
            MaxRetries = GetInt("NC_DB_MAX_RETRIES", 3),
            RetryDelayMilliseconds = GetInt("NC_DB_RETRY_DELAY_MS", 500),
            ExponentialBackoff = GetBool("NC_DB_EXPONENTIAL_BACKOFF", true),
            SlowOperationThresholdMs = GetInt("NC_SLOW_OPERATION_MS", 500),
            PrintTaskTimeoutMinutes = GetInt("NC_PRINT_TASK_TIMEOUT_MINUTES", 10),
            PrintRetryBaseDelayMs = GetInt("NC_PRINT_RETRY_BASE_MS", 1000),
            PrintRetryIncrementMs = GetInt("NC_PRINT_RETRY_INCREMENT_MS", 1000),
            PermissionCacheMinutes = GetInt("NC_PERMISSION_CACHE_MINUTES", 5),
            PermissionVersionCheckIntervalSeconds = GetInt("NC_PERMISSION_VERSION_CHECK_SECONDS", 60)
        };
        performance.Validate();

        var origins = GetString("NC_CORS_ORIGIN", DefaultCorsOrigin)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(origin => origin.TrimEnd('/'))
            .Where(origin => !string.Equals(origin, "*", StringComparison.Ordinal) && origin.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (origins.Length == 0)
        {
            origins = [DefaultCorsOrigin];
        }

        var bodyLimitMb = Math.Clamp(GetInt("NC_MAX_BODY_MB", 4), 1, 128);

        return new ServerEnvironmentOptions
        {
            Database = database,
            Storage = storage,
            App = app,
            Performance = performance,
            DataCenter = DataCenterOptions.Load(),
            CorsOrigins = origins,
            AgentRegisterToken = GetString("NC_AGENT_REGISTER_TOKEN", string.Empty).Trim(),
            AutoMigrate = GetBool("NC_AUTO_MIGRATE", false),
            Mode = mode,
            AgentHeartbeatSeconds = Math.Clamp(GetInt("NC_AGENT_HEARTBEAT_SECONDS", 30), 5, 3600),
            MaxRequestBodyBytes = bodyLimitMb * 1024L * 1024L
        };
    }

    private static string RequireString(string name)
    {
        var value = GetString(name, string.Empty).Trim();
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"缺少必需的环境变量 {name}。");
        }

        return value;
    }

    private static string RequireAny(params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetString(name, string.Empty).Trim();
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        throw new InvalidOperationException($"缺少必需的环境变量：{string.Join(" / ", names)}");
    }

    private static string GetString(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
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
