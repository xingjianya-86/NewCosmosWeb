using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace NewCosmos.Shell.Services;

/// <summary>在线更新清单契约：{ version, url, sha256, notes, publishedAt }。</summary>
public sealed class UpdateManifest
{
    public string Version { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string PublishedAt { get; set; } = string.Empty;
}

/// <summary>
/// 检查/下载/校验/启动安装包。安装包按 Inno Setup 约定传 /SILENT /NORESTART。
/// 所有失败都以对话框提示，不抛到 UI 线程外。
/// </summary>
public sealed class UpdateService
{
    private static readonly HttpClient Http = CreateHttpClient();
    private readonly ShellOptions _options;

    public UpdateService(ShellOptions options) => _options = options;

    public bool IsBusy { get; private set; }

    public static string CurrentVersion
    {
        get
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return informational.Split('+')[0].Trim();

            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }

    /// <param name="owner">可见主窗口（用于消息框居中）；隐藏时可为 null。</param>
    /// <param name="notify">托盘气泡提示（下载进度）。</param>
    public async Task CheckAndInstallAsync(Window? owner, Action<string>? notify = null,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy) return;
        IsBusy = true;

        try
        {
            if (string.IsNullOrWhiteSpace(_options.UpdateManifestUrl))
            {
                ShowInfo(owner,
                    "尚未配置更新地址。\r\n\r\n请在 %APPDATA%\\NewCosmos\\shell.json 设置 updateManifestUrl。",
                    MessageBoxImage.Warning);
                return;
            }

            notify?.Invoke("正在检查更新…");
            var manifest = await FetchManifestAsync(_options.UpdateManifestUrl, cancellationToken)
                .ConfigureAwait(true);

            if (!IsNewer(manifest.Version, CurrentVersion))
            {
                ShowInfo(owner, $"当前已是最新版本（{CurrentVersion}）。", MessageBoxImage.Information);
                return;
            }

            var notes = string.IsNullOrWhiteSpace(manifest.Notes)
                ? string.Empty
                : "\r\n\r\n更新说明：\r\n" + Truncate(manifest.Notes.Trim(), 800);

            var answer = Ask(owner,
                $"发现新版本 {manifest.Version}（当前 {CurrentVersion}）。{notes}\r\n\r\n是否下载并安装？",
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            notify?.Invoke($"正在下载 {manifest.Version}…");
            var installer = await DownloadAsync(manifest, cancellationToken).ConfigureAwait(true);

            notify?.Invoke("下载完成，正在启动安装程序…");
            LaunchInstaller(installer);

            var exit = Ask(owner,
                "安装程序已启动。\r\n\r\n是否立即退出 NewCosmos 以完成安装？",
                MessageBoxImage.Information);
            if (exit == MessageBoxResult.Yes && System.Windows.Application.Current is App app)
                app.ExitApp();
        }
        catch (OperationCanceledException)
        {
            // 用户取消/退出，忽略。
        }
        catch (Exception ex)
        {
            ShowInfo(owner, $"检查更新失败：\r\n{ex.Message}", MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static async Task<UpdateManifest> FetchManifestAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("updateManifestUrl 不是有效的 http/https 地址。");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        var json = await Http.GetStringAsync(uri, timeout.Token).ConfigureAwait(false);
        UpdateManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifest>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"更新清单不是合法 JSON：{ex.Message}", ex);
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version)
            || string.IsNullOrWhiteSpace(manifest.Url) || string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            throw new InvalidDataException("更新清单缺少 version/url/sha256 字段。");
        }

        return manifest;
    }

    private static async Task<string> DownloadAsync(UpdateManifest manifest, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(manifest.Url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("更新清单的 url 不是有效的 http/https 地址。");
        }

        var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        if (extension is not (".exe" or ".msi"))
            throw new InvalidDataException("更新包必须是 .exe 或 .msi 安装程序。");

        var directory = Path.Combine(Path.GetTempPath(), "NewCosmos", "ShellUpdate");
        Directory.CreateDirectory(directory);
        var safeVersion = string.Concat(manifest.Version
            .Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_'));
        var path = Path.Combine(directory, $"NewCosmos.Shell-{safeVersion}{extension}");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));

        using (var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                   .ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(timeout.Token)
                .ConfigureAwait(false);
            await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                81920, useAsync: true);
            await source.CopyToAsync(target, timeout.Token).ConfigureAwait(false);
        }

        var actual = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
        var expected = NormalizeSha256(manifest.Sha256);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(path);
            throw new InvalidDataException(
                $"更新包 SHA256 校验失败。\r\n期望：{expected}\r\n实际：{actual}");
        }

        return path;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static void LaunchInstaller(string path)
    {
        _ = Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Arguments = "/SILENT /NORESTART"
        }) ?? throw new InvalidOperationException("无法启动安装程序。");
    }

    internal static bool IsNewer(string candidate, string current)
    {
        static string Clean(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            return trimmed.StartsWith('v') || trimmed.StartsWith('V') ? trimmed[1..] : trimmed;
        }

        var left = Clean(candidate);
        var right = Clean(current);

        if (Version.TryParse(left, out var candidateVersion)
            && Version.TryParse(right, out var currentVersion))
        {
            return candidateVersion > currentVersion;
        }

        // 非标准版本号（如 2026.11.26-beta）退回文化无关字符串比较。
        return string.CompareOrdinal(left, right) > 0;
    }

    private static string NormalizeSha256(string value) =>
        new((value ?? string.Empty)
            .Where(c => Uri.IsHexDigit(c))
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* 忽略 */ }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"NewCosmos.Shell/{CurrentVersion}");
        return client;
    }

    private static void ShowInfo(Window? owner, string message, MessageBoxImage icon)
    {
        if (owner is { IsVisible: true })
            System.Windows.MessageBox.Show(owner, message, "NewCosmos 更新", MessageBoxButton.OK, icon);
        else
            System.Windows.MessageBox.Show(message, "NewCosmos 更新", MessageBoxButton.OK, icon);
    }

    private static MessageBoxResult Ask(Window? owner, string message, MessageBoxImage icon)
    {
        return owner is { IsVisible: true }
            ? System.Windows.MessageBox.Show(owner, message, "NewCosmos 更新", MessageBoxButton.YesNo, icon)
            : System.Windows.MessageBox.Show(message, "NewCosmos 更新", MessageBoxButton.YesNo, icon);
    }
}
