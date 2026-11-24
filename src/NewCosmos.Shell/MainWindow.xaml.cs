using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace NewCosmos.Shell;

public partial class MainWindow : Window
{
    private readonly ShellOptions _options;
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
        _options = ShellOptions.Load();
        Title = _options.Title;
        ServerText.Text = _options.StartUrl;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NewCosmos", "WebView2");
            Directory.CreateDirectory(userData);

            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await WebView.EnsureCoreWebView2Async(env);

            var core = WebView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = _options.EnableDevTools;
            core.Settings.AreDefaultContextMenusEnabled = _options.EnableDevTools;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;
            core.Settings.AreBrowserAcceleratorKeysEnabled = true;

            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationCompleted += OnNavigationCompleted;
            core.ProcessFailed += OnProcessFailed;
            core.DownloadStarting += OnDownloadStarting;

            WebView.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;
            core.Navigate(_options.StartUrl);
        }
        catch (Exception ex)
        {
            ShowError($"WebView2 初始化失败：{ex.Message}");
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IsAllowed(e.Uri)) return;

        e.Cancel = true;
        OpenExternal(e.Uri);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternal(e.Uri);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        if (e.IsSuccess)
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
            WebView.Visibility = Visibility.Visible;
            return;
        }

        ShowError($"页面加载失败：{e.WebErrorStatus}（{_options.StartUrl}）");
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        ShowError($"WebView2 进程异常：{e.ProcessFailedKind}");
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        // 默认保存到用户下载目录；不覆盖文件名，交给 WebView2/系统。
        e.DownloadOperation.StateChanged += (_, _) =>
        {
            if (e.DownloadOperation.State == CoreWebView2DownloadState.Completed)
            {
                var path = e.DownloadOperation.ResultFilePath;
                if (File.Exists(path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                    {
                        UseShellExecute = true
                    });
                }
            }
        };
    }

    private bool IsAllowed(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme is "about" or "data" or "blob") return true;
        if (parsed.Scheme is not ("http" or "https")) return false;

        var host = parsed.Host;
        return _options.AllowedHosts.Any(h =>
            string.Equals(host, h, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
    }

    private static void OpenExternal(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch
        {
            // 外部链接打开失败不阻塞主窗口。
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        LoadingPanel.Visibility = Visibility.Collapsed;
        WebView.Visibility = Visibility.Collapsed;
    }

    private void Retry_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingText.Text = "正在重连…";
        WebView.CoreWebView2?.Navigate(_options.StartUrl);
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e) => OpenExternal(_options.StartUrl);
}

public sealed class ShellOptions
{
    public string StartUrl { get; init; } = "https://cosmos.sua17.cn/";
    public string Title { get; init; } = "NewCosmos 社会救助系统";
    public bool EnableDevTools { get; init; }
    public string[] AllowedHosts { get; init; } = ["cosmos.sua17.cn", "cosmos-static.sua17.cn"];

    public static ShellOptions Load()
    {
        var envUrl = Environment.GetEnvironmentVariable("NC_SHELL_URL");
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NewCosmos", "shell.json");

        ShellOptions? fromFile = null;
        try
        {
            if (File.Exists(configPath))
            {
                fromFile = JsonSerializer.Deserialize<ShellOptions>(
                    File.ReadAllText(configPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
        }
        catch
        {
            // 配置损坏时用默认值，不阻塞启动。
        }

        var options = fromFile ?? new ShellOptions();
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            options = new ShellOptions
            {
                StartUrl = envUrl.Trim(),
                Title = options.Title,
                EnableDevTools = options.EnableDevTools,
                AllowedHosts = options.AllowedHosts
            };
        }
        return options;
    }
}
