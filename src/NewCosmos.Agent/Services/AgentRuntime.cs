using System.Reflection;
using NewCosmos.Models.Options;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services;

/// <summary>Agent 运行时装配：日志、临时目录、性能参数。</summary>
public static class AgentRuntime
{
    public const string ApplicationName = "NewCosmos";

    public static string Version =>
        typeof(AgentRuntime).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?.Split('+')[0]
        ?? "2.0.20261124";

    /// <summary>日志/临时文件统一落 %APPDATA%\NewCosmos 与 %TEMP%\NewCosmos。</summary>
    public static StorageOptions CreateStorageOptions() => new()
    {
        ApplicationName = ApplicationName,
        UseAppDataDirectory = true,
        BackupDirectoryName = "Backup",
        LogDirectoryName = "Logs",
        TempDirectoryName = "Temp"
    };

    /// <summary>
    /// 引擎只读取超时/重试字段；这里给出保守默认值，避免依赖 legacy config/performance.ini。
    /// </summary>
    public static PerformanceOptions CreatePerformanceOptions() => new()
    {
        MaxRetries = 3,
        RetryDelayMilliseconds = 500,
        ExponentialBackoff = true,
        SlowOperationThresholdMs = 3000,
        PrintTaskTimeoutMinutes = 10,
        PrintRetryBaseDelayMs = 500,
        PrintRetryIncrementMs = 250,
        PermissionCacheMinutes = 5,
        PermissionVersionCheckIntervalSeconds = 60
    };

    public static ILoggerService CreateLogger()
    {
        var storage = CreateStorageOptions();
        var app = new AppOptions
        {
            Version = Version,
            ApplicationName = "NewCosmos.Agent",
            WindowTitle = "NewCosmos Agent"
        };
        return new LoggerService(storage, app, CreatePerformanceOptions());
    }
}
