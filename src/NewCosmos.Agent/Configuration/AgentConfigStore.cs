using System.Text.Json;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Configuration;

/// <summary>agent.json 读写（原子替换）。</summary>
public static class AgentConfigStore
{
    public const string DefaultFileName = "agent.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>%APPDATA%\NewCosmos\agent.json（可用 --config 或 NC_AGENT_CONFIG 覆盖）。</summary>
    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NewCosmos",
            DefaultFileName);

    public static string ResolvePath(string? explicitPath)
    {
        var configured = explicitPath;
        if (string.IsNullOrWhiteSpace(configured))
            configured = Environment.GetEnvironmentVariable("NC_AGENT_CONFIG");
        if (string.IsNullOrWhiteSpace(configured))
            configured = DefaultPath;

        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured));
    }

    public static AgentSettings Load(string configFilePath, ILoggerService logger)
    {
        var settings = new AgentSettings();

        if (File.Exists(configFilePath))
        {
            try
            {
                var json = File.ReadAllText(configFilePath);
                settings = JsonSerializer.Deserialize<AgentSettings>(json, JsonOptions) ?? new AgentSettings();
            }
            catch (Exception ex)
            {
                logger.Warn($"[AgentConfig] 读取配置文件失败，使用默认配置: {configFilePath} - {ex.Message}");
            }
        }
        else
        {
            logger.Info($"[AgentConfig] 配置文件不存在，将在首次注册时创建: {configFilePath}");
        }

        // 环境变量优先于文件（便于部署时注入，不落盘）
        var envServer = Environment.GetEnvironmentVariable("NC_AGENT_SERVER_URL");
        if (!string.IsNullOrWhiteSpace(envServer))
            settings.ServerBaseUrl = envServer;

        var envToken = Environment.GetEnvironmentVariable("NC_AGENT_REGISTER_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
            settings.RegisterToken = envToken;

        settings.Normalize(configFilePath);
        return settings;
    }

    public static void Save(AgentSettings settings, string configFilePath, ILoggerService logger)
    {
        var dir = Path.GetDirectoryName(configFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tempPath = configFilePath + ".tmp";
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, configFilePath, overwrite: true);

        logger.Info($"[AgentConfig] 配置已保存: {configFilePath}");
    }
}
