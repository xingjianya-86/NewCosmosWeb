using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NewCosmos.Agent.Configuration;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services.Update;

/// <summary>升级提示级别（宿主按级别选图标/颜色）。</summary>
public enum UpdateNotifyLevel
{
    Info,
    Warning
}

/// <summary>
/// 升级提示回调；宿主负责线程安全（托盘侧封送回 UI 线程，CLI 侧直接打印）。
/// </summary>
public delegate void UpdateNotifyHandler(UpdateNotifyLevel level, string message);

/// <summary>
/// 执行安装并让位：启动安装包（成功由 iss [Run] 拉起新版，失败由 cmd 兜底拉起旧版）。
/// 注入该委托是为了单测可以把"真的跑安装器"换掉；返回失败时 UpdateService 会恢复任务认领并提示重试。
/// </summary>
public delegate Result AgentInstallLauncher(string setupPath);

/// <summary>
/// Agent 自动更新（L2 全自动）编排：检查清单 → 版本比较 → 下载 → SHA256 + Authenticode 校验
/// → 空闲门控（暂停认领、等在执行任务归零）→ 静默安装 → 让位退出。
///
/// 设计原则：
/// <list type="number">
/// <item>更新是旁路功能，任何失败只记日志/提示，绝不影响心跳与任务轮询；</item>
/// <item>业务流用 <see cref="Result{T}"/> 表达，不靠异常控制流程（异常只兜底未预期错误）；</item>
/// <item>校验必须全部通过才允许碰运行中的程序目录——清单可被有写权限的人篡改，
/// 所以 SHA256 之外还要求 Authenticode 证书链有效。</item>
/// </list>
/// </summary>
public sealed class AgentUpdateService
{
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly AgentSettings _settings;
    private readonly ILoggerService _logger;
    private readonly AgentUpdateGate _gate;
    private readonly AgentInstallLauncher _launchInstaller;
    private readonly UpdateNotifyHandler? _notify;
    private readonly Action? _requestExit;
    private int _checking;

    /// <param name="settings">配置（AutoUpdate/UpdateManifestUrl/间隔等）。</param>
    /// <param name="logger">日志（无 PII：只记版本、路径、错误摘要）。</param>
    /// <param name="gate">空闲门控；与 JobWorker 共用同一实例。</param>
    /// <param name="launchInstaller">安装执行器（注入替换点）。</param>
    /// <param name="notify">升级提示回调，可空（CLI 不需要）。</param>
    /// <param name="requestExit">让位退出回调（托盘 RequestExit / cts.Cancel），可空。</param>
    public AgentUpdateService(
        AgentSettings settings,
        ILoggerService logger,
        AgentUpdateGate gate,
        AgentInstallLauncher launchInstaller,
        UpdateNotifyHandler? notify = null,
        Action? requestExit = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _launchInstaller = launchInstaller ?? throw new ArgumentNullException(nameof(launchInstaller));
        _notify = notify;
        _requestExit = requestExit;
    }

    /// <summary>
    /// 常驻检查循环：首查延迟（UpdateFirstCheckDelaySeconds）→ 每 UpdateCheckIntervalHours 检查一次。
    /// 任何异常只记日志不退出——自动更新绝不能把心跳/任务轮询带崩。
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _logger.Info(
            $"[Update] 自动更新循环启动: firstDelay={_settings.UpdateFirstCheckDelaySeconds}s, " +
            $"interval={_settings.UpdateCheckIntervalHours}h, autoUpdate={_settings.AutoUpdate}, " +
            $"manifest={_settings.UpdateManifestUrl}");

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_settings.UpdateFirstCheckDelaySeconds), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // Agent 正在退出
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 失败已在 CheckOnceAsync 内部记 Warn 并给出气泡，这里不重复刷日志。
                await CheckOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Update] 检查更新异常");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(_settings.UpdateCheckIntervalHours), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.Info("[Update] 自动更新循环停止");
    }

    /// <summary>
    /// 单次检查：成功返回面向用户的提示文案，失败返回原因（调用方负责展示"升级失败…稍后重试"）。
    /// 同一时刻只允许一个检查在跑（托盘手动检查可能与后台循环撞车）。
    /// </summary>
    public async Task<Result<string>> CheckOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.AutoUpdate)
        {
            _logger.Debug("[Update] autoUpdate=false，跳过更新检查");
            return Result.Success("自动更新已关闭");
        }

        if (string.IsNullOrWhiteSpace(_settings.UpdateManifestUrl))
        {
            // ServerBaseUrl 与清单地址都为空时的常态：不算错误，Debug 留痕即可。
            _logger.Debug("[Update] 未配置更新清单地址，跳过更新检查");
            return Result.Success("未配置更新清单地址，跳过检查");
        }

        if (Interlocked.CompareExchange(ref _checking, 1, 0) != 0)
            return Result.Success("已有一次更新检查在进行中");

        try
        {
            return await CheckCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    private async Task<Result<string>> CheckCoreAsync(CancellationToken cancellationToken)
    {
        var current = AgentRuntime.Version;

        // 2) 拉清单（带时间戳穿透 nginx 的 1 小时缓存）。
        string json;
        using (var manifestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            manifestCts.CancelAfter(AgentUpdateDefaults.ManifestTimeout);
            try
            {
                using var response = await Http
                    .GetAsync(AgentUpdateDefaults.WithCacheBuster(_settings.UpdateManifestUrl), manifestCts.Token)
                    .ConfigureAwait(false);

                // 404 = 这套环境还没发布过 Agent 清单，是常态（新部署/未启用自动升级），静默跳过。
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.Debug($"[Update] 尚无更新清单: {_settings.UpdateManifestUrl}");
                    return Result.Success("尚无更新清单");
                }

                if (!response.IsSuccessStatusCode)
                    return Fail($"清单请求失败 HTTP {(int)response.StatusCode}");

                json = await response.Content.ReadAsStringAsync(manifestCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // Agent 退出，不当作升级失败
            }
            catch (OperationCanceledException)
            {
                return Fail("清单请求超时（20 秒）");
            }
            catch (HttpRequestException ex)
            {
                return Fail($"无法连接更新服务器: {ex.Message}", ex);
            }
        }

        // 3) 解析 + 版本比较。
        var parsed = AgentUpdateManifest.Parse(json);
        if (parsed.IsFailure)
            return Fail(parsed.Message);

        var manifest = parsed.Value;
        if (AgentUpdateManifest.CompareVersion(manifest.Version, current) <= 0)
        {
            _logger.Debug($"[Update] 已是最新版本: current={current}, manifest={manifest.Version}");
            return Result.Success($"已是最新版本（{current}）");
        }

        // 4+5) 下载安装包（本地已有且哈希匹配则复用），并做 SHA256 校验。
        var prepared = await PrepareInstallerAsync(manifest, cancellationToken).ConfigureAwait(false);
        if (prepared.IsFailure)
            return Fail(prepared.Message);

        var installerPath = prepared.Value;

        // 6) Authenticode：防"清单 + 安装包一起被换"。SHA256 只能证明文件没被下载过程篡改，
        //    签名链才能证明发布者可信；失败即删包放弃本轮（宁可不升级也不装来路不明的包）。
        var signature = VerifyAuthenticode(installerPath);
        if (signature.IsFailure)
        {
            TryDelete(installerPath);
            return Fail(signature.Message);
        }

        // 7) 空闲门控：暂停认领 → 等在执行任务归零。
        _gate.RequestPause();
        bool idle;
        try
        {
            idle = await _gate.WaitForIdleAsync(AgentUpdateDefaults.IdleWaitTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            _gate.Resume(); // 退出/异常路径必须恢复认领，否则 Agent 卡死不接活
            throw;
        }

        if (!idle)
        {
            _gate.Resume();
            _logger.Warn("[Update] 任务执行中，推迟升级（等待空闲超时 60 秒）");
            return Result.Success("任务执行中，推迟升级");
        }

        // 8) 告知用户（托盘气泡由宿主封送到 UI 线程）。
        _logger.Info($"[Update] 发现新版本 {manifest.Version}（当前 {current}），开始静默升级");
        _notify?.Invoke(UpdateNotifyLevel.Info, $"发现新版本 {manifest.Version}，正在升级…");

        // 9) 执行安装。成功路径由安装器负责收尾（iss [Run] 拉新版 / cmd 兜底拉旧版），
        //    随后触发让位退出；这里不再恢复门控——进程即将退出。
        // 竞态说明：安装器 CloseApplications=yes 会在 Agent 未退净时接管关闭，
        // Agent 主动退出只是让 exe 尽早解锁，避免安装器等待或弹"文件被占用"。
        var launched = _launchInstaller(installerPath);
        if (launched.IsFailure)
            return Fail(launched.Message);

        _logger.Info($"[Update] 安装程序已启动，Agent 让位退出: {installerPath}");
        try
        {
            _requestExit?.Invoke();
        }
        catch (Exception ex)
        {
            // 退出回调失败：安装器仍会 CloseApplications 收拾我们，这里只留痕不 Resume，
            // 否则可能出现"旧进程继续接活 + 新版被拉起"的双实例。
            _logger.LogError(ex, "[Update] 触发 Agent 退出失败");
        }

        return Result.Success($"已启动 {manifest.Version} 升级，Agent 正在退出…");
    }

    /// <summary>
    /// 步骤 4+5：把安装包落到 %TEMP%\NewCosmos\Updates\NewCosmosAgentSetup_{version}.exe。
    /// 已存在且 SHA256 匹配 → 复用（避免每次检查重下几十 MB）；不匹配 → 删除后重新下载；
    /// 下载完成后强制 SHA256 校验，不匹配删包并放弃本轮。
    /// </summary>
    private async Task<Result<string>> PrepareInstallerAsync(
        AgentUpdateManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Result.Failure<string>(AgentUpdateManifest.ErrorCodeInvalidUrl, "清单 url 不是有效的 http/https 地址");
        }

        var expected = NormalizeSha256(manifest.Sha256);
        var directory = AgentUpdateDefaults.UpdateCacheDirectory;
        var path = Path.Combine(directory, AgentUpdateDefaults.BuildInstallerFileName(manifest.Version));

        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            return Result.Failure<string>("AGENT_UPDATE_IO", $"无法创建更新缓存目录: {ex.Message}");
        }

        if (File.Exists(path))
        {
            string existing;
            try
            {
                existing = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result.Failure<string>("AGENT_UPDATE_IO", $"读取已下载安装包失败: {ex.Message}");
            }

            if (Sha256Matches(expected, existing))
            {
                _logger.Debug($"[Update] 复用已下载的安装包: {path}");
                return Result.Success(path);
            }

            // 哈希不匹配：多为上次下载中断的残留或清单已更新，删除后重下。
            _logger.Warn($"[Update] 已存在安装包哈希不匹配，重新下载: {path}");
            TryDelete(path);
        }

        try
        {
            using var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            downloadCts.CancelAfter(AgentUpdateDefaults.DownloadTimeout);

            using var response = await Http
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, downloadCts.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<string>(
                    "AGENT_UPDATE_DOWNLOAD_FAILED",
                    $"下载安装包失败 HTTP {(int)response.StatusCode}");
            }

            await using var source = await response.Content.ReadAsStreamAsync(downloadCts.Token).ConfigureAwait(false);
            await using var target = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await source.CopyToAsync(target, downloadCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            TryDelete(path);
            return Result.Failure<string>(
                "AGENT_UPDATE_DOWNLOAD_TIMEOUT",
                $"下载安装包超时（{(int)AgentUpdateDefaults.DownloadTimeout.TotalMinutes} 分钟）");
        }
        catch (HttpRequestException ex)
        {
            TryDelete(path);
            return Result.Failure<string>("AGENT_UPDATE_DOWNLOAD_FAILED", $"下载安装包失败: {ex.Message}");
        }
        catch (IOException ex)
        {
            TryDelete(path);
            return Result.Failure<string>("AGENT_UPDATE_IO", $"写入安装包失败: {ex.Message}");
        }

        // 步骤 5：SHA256 校验（不匹配 → 删文件 → Warn → 放弃本轮）。
        string actual;
        try
        {
            actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(path);
            return Result.Failure<string>("AGENT_UPDATE_IO", $"校验安装包失败: {ex.Message}");
        }

        if (!Sha256Matches(expected, actual))
        {
            TryDelete(path);
            _logger.Warn($"[Update] 安装包 SHA256 校验失败，已删除: {path}");
            return Result.Failure<string>("AGENT_UPDATE_SHA_MISMATCH", "安装包 SHA256 校验失败");
        }

        _logger.Info($"[Update] 安装包已就绪: {path}");
        return Result.Success(path);
    }

    /// <summary>
    /// Authenticode 校验：文件必须有签名，且证书链能构建到本机信任锚
    /// （信任锚按 deploy/RELEASE_CLIENTS.md §2.2 导入的 NewCosmosCodeSign.cer）。
    /// 自签名内网证书没有 CRL/OCSP，故关闭吊销检查——链构建本身已把发布者收敛到导入过的证书。
    /// </summary>
    private static Result VerifyAuthenticode(string path)
    {
        // .NET 10 把"从文件加载证书"标了 SYSLIB0057，但 X509CertificateLoader 只能读 .cer/.pem
        // 证书文件，**无法**从 exe 的 Authenticode 签名里提取签名者——CreateFromSignedFile 是唯一入口，
        // 故此处豁免该过时提示（本地豁免，不污染整个 csproj 的 NoWarn）。
#pragma warning disable SYSLIB0057
        X509Certificate signed;
        try
        {
            signed = X509Certificate.CreateFromSignedFile(path);
        }
        catch (CryptographicException ex)
        {
            return Result.Failure("AGENT_UPDATE_NOT_SIGNED", $"安装包没有有效签名: {ex.Message}");
        }
#pragma warning restore SYSLIB0057

        using (signed)
        using (var certificate = new X509Certificate2(signed))
        using (var chain = new X509Chain())
        {
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

            if (chain.Build(certificate))
                return Result.Success();

            var statuses = string.Join(
                "; ",
                chain.ChainStatus.Select(s => $"{s.Status}: {s.StatusInformation.Trim()}"));
            if (string.IsNullOrWhiteSpace(statuses))
                statuses = "未知状态";

            return Result.Failure(
                "AGENT_UPDATE_BAD_SIGNATURE",
                $"安装包签名链校验失败（{statuses}）。请确认已导入签名证书（deploy/RELEASE_CLIENTS.md §2.2）");
        }
    }

    /// <summary>
    /// 步骤 10 的失败统一收口：恢复门控（未暂停时空操作）→ Warn 日志 → 气泡「升级失败…稍后重试」。
    /// 任何失败都必须 Resume，否则一次失败会让 Agent 永久停止认领任务。
    /// </summary>
    private Result<string> Fail(string reason, Exception? exception = null)
    {
        _gate.Resume();

        if (exception is null)
            _logger.Warn($"[Update] {reason}");
        else
            _logger.Warn($"[Update] {reason}: {exception.Message}");

        _notify?.Invoke(UpdateNotifyLevel.Warning, $"升级失败: {reason}，将稍后重试");
        return Result.Failure<string>("AGENT_UPDATE_FAILED", reason);
    }

    /// <summary>SHA256（纯函数，便于单测）：两侧规范化成纯大写十六进制后比较，且必须是完整 64 位。</summary>
    public static bool Sha256Matches(string? expected, string? actual)
    {
        var left = NormalizeSha256(expected);
        // 固定 64 位：空/过短的清单值不会因为"两边都空"而误判通过。
        return left.Length == 64
               && string.Equals(left, NormalizeSha256(actual), StringComparison.Ordinal);
    }

    /// <summary>
    /// 清单里的 sha256 可能带 0x 前缀/大小写/空白：统一成纯大写十六进制。
    /// 0x 前缀必须先摘掉——十六进制摘要里不可能出现 'x'，所以开头的 "0x" 一定是前缀而非数据。
    /// </summary>
    public static string NormalizeSha256(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];

        return string.Concat(text.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant));
    }

    /// <summary>计算文件 SHA256（十六进制大写）。</summary>
    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            // 删不掉只留痕：下轮哈希不匹配会重新下载覆盖。
            System.Diagnostics.Debug.WriteLine($"删除更新缓存失败: {ex.Message}");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        // 总超时交给每次请求的 linked CTS（清单 20s / 下载 15min），避免共享超时误杀大文件下载。
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"NewCosmos.Agent/{AgentRuntime.Version}");
        return client;
    }
}
