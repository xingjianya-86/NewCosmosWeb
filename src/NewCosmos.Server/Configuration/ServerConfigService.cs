using System.Text.Json;
using NewCosmos.Models.Options;
using NewCosmos.Services.Core;

namespace NewCosmos.Server.Configuration;

/// <summary>
/// 服务端 IConfigService 实现：配置来自环境变量（<see cref="ServerEnvironmentOptions"/>），
/// 仅 Schema 初始化状态需要持久化，写在 <see cref="DataCenterOptions.StateRoot"/> 的 JSON 文件里。
/// 文件写入不影响任何业务配置，也不包含凭据。
/// </summary>
public sealed class ServerConfigService(
    ServerEnvironmentOptions options,
    DataCenterOptions dataCenter,
    ILoggerService logger) : IConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _sync = new();

    public AppOptions GetAppOptions() => options.App;

    public DatabaseOptions GetDatabaseOptions() => options.Database;

    public StorageOptions GetStorageOptions() => options.Storage;

    public PerformanceOptions GetPerformanceOptions() => options.Performance;

    public PreferencesOptions GetPreferencesOptions() => new();

    public SchemaOptions GetSchemaOptions()
    {
        lock (_sync)
        {
            try
            {
                var path = GetSchemaStatePath();
                if (!File.Exists(path))
                {
                    return new SchemaOptions();
                }

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<SchemaOptions>(json, JsonOptions) ?? new SchemaOptions();
            }
            catch (Exception ex)
            {
                logger.Warn($"读取 Schema 状态失败，按未初始化处理: {ex.Message}");
                return new SchemaOptions();
            }
        }
    }

    /// <summary>
    /// OCR/AI 判定服务地址来自 <c>NC_OCR_SERVICE_URL</c>（见 <see cref="ServerEnvironmentOptions.OcrServiceUrl"/>）。
    /// 为空 = 未配置：OCR 识别与 AI 重病判定均降级为人工，不影响表单保存。
    /// </summary>
    public NetworkOptions GetNetworkOptions() => new() { OcrServiceUrl = options.OcrServiceUrl };

    public UpdateOptions GetUpdateOptions() => new() { Enabled = false };

    public DocumentOutputOptions GetDocumentOutputOptions() => new();

    public void SetDocumentOutputBaseDirectory(string directory)
    {
        // 服务端不提供用户级输出目录设置。
    }

    public void ResetDocumentOutputBaseDirectory()
    {
        // 服务端不提供用户级输出目录设置。
    }

    public void SaveNetworkOptions(NetworkOptions options)
    {
        // 服务端网络接入由部署配置管理，不在页面里改。
    }

    public void UpdateDatabaseEndpoint(string host, int port)
    {
        // 连接串在容器启动时固化，改端点需更新 server.env 并重启。
    }

    public void SaveSchemaOptions(SchemaOptions options)
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(dataCenter.StateRoot);
                var path = GetSchemaStatePath();
                var temp = path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(options, JsonOptions));
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                logger.Error($"保存 Schema 状态失败: {ex.Message}");
                throw;
            }
        }
    }

    public void EnsureConfigDirectoryExists()
    {
        Directory.CreateDirectory(dataCenter.StateRoot);
    }

    public bool CheckConfigFilesExist() => true;

    public List<string> GetMissingConfigFiles() => [];

    public List<string> GenerateDefaultConfigFiles() => [];

    public void SaveDatabaseOptions(DatabaseOptions options)
    {
        // 服务端数据库连接由环境变量提供，不允许运行时改写。
    }

    private string GetSchemaStatePath() => Path.Combine(dataCenter.StateRoot, "schema-options.json");
}
