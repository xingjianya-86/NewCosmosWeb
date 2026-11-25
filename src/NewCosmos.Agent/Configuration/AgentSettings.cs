using System.Text.Json.Serialization;

namespace NewCosmos.Agent.Configuration;

/// <summary>
/// Agent 本地配置（%APPDATA%\NewCosmos\agent.json）。
/// 禁止在源码/仓库中硬编码任何密码、注册令牌或设备密钥。
/// </summary>
public sealed class AgentSettings
{
    /// <summary>服务端业务 API 根地址，如 https://cosmos-static.sua17.cn:20000</summary>
    public string ServerBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// 一次性注册令牌（对应服务端环境变量 NC_AGENT_REGISTER_TOKEN）。
    /// 也可用环境变量 NC_AGENT_REGISTER_TOKEN 或命令行 --register-token 覆盖。
    /// </summary>
    public string RegisterToken { get; set; } = string.Empty;

    /// <summary>
    /// 设备密钥文件路径。缺省与配置文件同路径（agent.json 内保存 DPAPI 密文）。
    /// 配置为独立文件时，该文件保存 DPAPI 密文（失败退化为当前用户 ACL 的明文文件）。
    /// </summary>
    public string DeviceKeyFile { get; set; } = string.Empty;

    /// <summary>心跳间隔秒数（服务端注册响应可覆盖），默认 30 秒。</summary>
    public int HeartbeatSeconds { get; set; } = 30;

    /// <summary>任务队列轮询间隔秒数（2–300），默认 3 秒。</summary>
    public int JobPollSeconds { get; set; } = 3;

    /// <summary>
    /// 打印任务临时目录（模板/渲染源/预览 PDF）。
    /// 默认 %TEMP%\NewCosmos\PrintJobs；支持环境变量，如 %TEMP%。
    /// </summary>
    public string PrintJobsDirectory { get; set; } = @"%TEMP%\NewCosmos\PrintJobs";

    /// <summary>预览 PDF 保留最近 N 个（默认 10），更早的尽力删除。</summary>
    public int PreviewKeepCount { get; set; } = 10;

    /// <summary>
    /// 批量模板任务/保存类任务的输出根目录，默认 %USERPROFILE%\Documents\NewCosmos。
    /// 实际输出目录为 {OutputRoot}\{分类}\{期间}\（如 ...\NewCosmos\高龄月报\202611）。
    /// 支持环境变量。
    /// </summary>
    public string OutputRoot { get; set; } = @"%USERPROFILE%\Documents\NewCosmos";

    /// <summary>上报的机器名，缺省取 Environment.MachineName。</summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>服务端注册返回的设备 ID（仅用于展示/排障）。</summary>
    public int? DeviceId { get; set; }

    /// <summary>DPAPI(CurrentUser) 加密后的设备密钥（Base64）。与配置文件同路径时使用。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeviceKeyProtected { get; set; }

    /// <summary>
    /// DPAPI 不可用时的降级明文设备密钥（仅在 DeviceKeyFile 与配置文件同路径时出现）。
    /// 写入后立即用 icacls 收紧为当前用户；日志会记录警告。
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeviceKey { get; set; }

    /// <summary>解析后的设备密钥文件绝对路径（空 = 与配置文件相同）。</summary>
    public string ResolveDeviceKeyFile(string configFilePath)
    {
        if (string.IsNullOrWhiteSpace(DeviceKeyFile))
            return configFilePath;

        return Path.IsPathRooted(DeviceKeyFile)
            ? Path.GetFullPath(DeviceKeyFile)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configFilePath) ?? ".", DeviceKeyFile));
    }

    public void Normalize(string configFilePath)
    {
        ServerBaseUrl = (ServerBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        RegisterToken = (RegisterToken ?? string.Empty).Trim();
        MachineName = string.IsNullOrWhiteSpace(MachineName) ? Environment.MachineName : MachineName.Trim();
        HeartbeatSeconds = Math.Clamp(HeartbeatSeconds <= 0 ? 30 : HeartbeatSeconds, 5, 3600);
        JobPollSeconds = Math.Clamp(JobPollSeconds <= 0 ? 3 : JobPollSeconds, 2, 300);
        PreviewKeepCount = Math.Clamp(PreviewKeepCount <= 0 ? 10 : PreviewKeepCount, 1, 100);

        var printJobsDirectory = string.IsNullOrWhiteSpace(PrintJobsDirectory)
            ? Path.Combine(Path.GetTempPath(), "NewCosmos", "PrintJobs")
            : Environment.ExpandEnvironmentVariables(PrintJobsDirectory.Trim());
        PrintJobsDirectory = Path.GetFullPath(printJobsDirectory);

        // 保存类任务输出根：缺失时回退默认（Documents\NewCosmos），保证老配置文件也有可用值。
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaultOutputRoot = string.IsNullOrWhiteSpace(userProfile)
            ? Path.Combine(Path.GetTempPath(), "NewCosmos", "Output")
            : Path.Combine(userProfile, "Documents", "NewCosmos");
        var outputRoot = string.IsNullOrWhiteSpace(OutputRoot)
            ? defaultOutputRoot
            : Environment.ExpandEnvironmentVariables(OutputRoot.Trim());
        OutputRoot = Path.GetFullPath(outputRoot);

        DeviceKeyFile = ResolveDeviceKeyFile(configFilePath);
    }
}
