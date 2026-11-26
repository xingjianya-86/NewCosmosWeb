using System.IO;
using System.Text.Json;

namespace NewCosmos.Shell;

/// <summary>
/// 壳配置（%APPDATA%\NewCosmos\shell.json）。
/// 配置损坏或字段缺失时使用默认值，不阻塞启动。
/// </summary>
public sealed class ShellOptions
{
    public const string DefaultTitle = "帝皇权杖δ-me13 社会救助管理系统";
    public const string DefaultStartUrl = "https://cosmos.sua17.cn/";

    /// <summary>启动后加载的页面地址。</summary>
    public string StartUrl { get; set; } = DefaultStartUrl;

    /// <summary>窗口标题。</summary>
    public string Title { get; set; } = DefaultTitle;

    /// <summary>是否允许 DevTools / 默认右键菜单（生产应为 false）。</summary>
    public bool EnableDevTools { get; set; }

    /// <summary>允许 WebView2 内导航的域（含子域）；其余链接交给系统浏览器。</summary>
    public string[] AllowedHosts { get; set; } = ["cosmos.sua17.cn", "cosmos-static.sua17.cn"];

    /// <summary>关闭窗口时最小化到托盘；false 时关闭窗口即退出程序。</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>在线更新清单地址（JSON：version/url/sha256/notes/publishedAt）；空 = 不检查。</summary>
    public string UpdateManifestUrl { get; set; } = string.Empty;

    /// <summary>
    /// 可选：显式指定 NewCosmos.Agent.exe 路径。
    /// 空 = 依次查找注册表自启项、%LOCALAPPDATA%\Programs\NewCosmos Agent、壳目录及 Agent 子目录。
    /// </summary>
    public string AgentPath { get; set; } = string.Empty;

    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NewCosmos", "shell.json");

    public static ShellOptions Load()
    {
        ShellOptions? fromFile = null;
        try
        {
            if (File.Exists(ConfigPath))
            {
                fromFile = JsonSerializer.Deserialize<ShellOptions>(
                    File.ReadAllText(ConfigPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch
        {
            // 配置损坏时用默认值，不阻塞启动。
        }

        var options = fromFile ?? new ShellOptions();
        options.Normalize();

        // 环境变量优先，便于联调本地/测试环境。
        var envUrl = Environment.GetEnvironmentVariable("NC_SHELL_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
            options.StartUrl = envUrl.Trim();

        return options;
    }

    private void Normalize()
    {
        Title = string.IsNullOrWhiteSpace(Title) ? DefaultTitle : Title.Trim();
        StartUrl = string.IsNullOrWhiteSpace(StartUrl) ? DefaultStartUrl : StartUrl.Trim();

        if (AllowedHosts is null || AllowedHosts.Length == 0)
            AllowedHosts = ["cosmos.sua17.cn", "cosmos-static.sua17.cn"];
        else
            AllowedHosts = AllowedHosts
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Select(h => h.Trim())
                .ToArray();

        UpdateManifestUrl = (UpdateManifestUrl ?? string.Empty).Trim();
        AgentPath = (AgentPath ?? string.Empty).Trim();
    }
}
