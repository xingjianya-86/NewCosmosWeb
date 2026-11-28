namespace NewCosmos.Agent.Services.Update;

/// <summary>
/// 自动更新相关常量的唯一来源：清单路径、超时、下载目录、门控时序。
/// 集中存放是为了"改一处不漏一处"，也便于测试直接引用同一份口径。
/// </summary>
public static class AgentUpdateDefaults
{
    /// <summary>清单相对路径（拼在 ServerBaseUrl 之后，见 AgentSettings.UpdateManifestUrl）。</summary>
    public const string ManifestPathSuffix = "/downloads/windows/agent-latest.json";

    /// <summary>清单请求超时（与 Shell 侧 UpdateService 保持一致）。</summary>
    public static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>安装包下载超时（安装包几十 MB，弱网场景给足时间）。</summary>
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

    /// <summary>暂停后等待在执行任务归零的最长时长；超时则推迟本轮升级。</summary>
    public static readonly TimeSpan IdleWaitTimeout = TimeSpan.FromSeconds(60);

    /// <summary>门控暂停期间任务轮询的短延时：只等不认领（JobWorker 用）。</summary>
    public const int PausePollMilliseconds = 500;

    /// <summary>安装包缓存目录 %TEMP%\NewCosmos\Updates。</summary>
    public static string UpdateCacheDirectory =>
        Path.Combine(Path.GetTempPath(), "NewCosmos", "Updates");

    /// <summary>
    /// 安装包缓存文件名：版本号只保留字母数字与 . - _ ，避免清单里的恶意版本号拼进路径。
    /// </summary>
    public static string BuildInstallerFileName(string? version)
    {
        var safe = string.Concat((version ?? string.Empty)
            .Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'));
        return $"NewCosmosAgentSetup_{(safe.Length == 0 ? "unknown" : safe)}.exe";
    }

    /// <summary>
    /// 清单缓存穿透：nginx 对 /downloads/ 下发 <c>Cache-Control: max-age=3600</c>，
    /// 带时间戳查询参数才能拿到刚发布的清单（见 deploy/RELEASE_CLIENTS.md §8）。
    /// </summary>
    public static string WithCacheBuster(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        var separator = url.Contains('?') ? "&" : "?";
        return url + separator + "t=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
