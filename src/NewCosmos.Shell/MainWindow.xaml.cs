using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using NewCosmos.Shell.Services;

namespace NewCosmos.Shell;

public partial class MainWindow : Window
{
    private const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private readonly ShellOptions _options;
    private bool _initialized;
    private bool _initializing;

    public MainWindow(ShellOptions options)
    {
        _options = options;
        InitializeComponent();
        Title = options.Title;
        ServerText.Text = options.StartUrl;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        if (_initializing) return;
        _initializing = true;
        LoadingPanel.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;
        LoadingText.Text = "正在加载…";

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

            if (!_initialized)
            {
                core.NavigationStarting += OnNavigationStarting;
                core.NewWindowRequested += OnNewWindowRequested;
                core.NavigationCompleted += OnNavigationCompleted;
                core.ProcessFailed += OnProcessFailed;
                core.DownloadStarting += OnDownloadStarting;
                _initialized = true;
            }

            WebView.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;
            core.Navigate(_options.StartUrl);
        }
        catch (Exception ex)
        {
            HandleInitializationFailure(ex);
        }
        finally
        {
            _initializing = false;
        }
    }

    private void HandleInitializationFailure(Exception ex)
    {
        if (IsRuntimeMissing())
        {
            ShowError(
                "未安装 WebView2 Runtime",
                "本机缺少 Microsoft Edge WebView2 Runtime，无法显示页面。\r\n" +
                "请点击下方按钮下载安装；安装完成后回到本窗口点击“重试”。\r\n\r\n" +
                RuntimeDownloadUrl,
                showRuntimeDownload: true);
            return;
        }

        ShowError("WebView2 初始化失败", $"WebView2 初始化失败：{ex.Message}", showRuntimeDownload: false);
    }

    private static bool IsRuntimeMissing()
    {
        try
        {
            return string.IsNullOrWhiteSpace(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch
        {
            // 未安装时 GetAvailableBrowserVersionString 会抛异常。
            return true;
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
            RuntimeButton.Visibility = Visibility.Collapsed;
            WebView.Visibility = Visibility.Visible;
            return;
        }

        ShowError("无法连接服务",
            $"页面加载失败：{e.WebErrorStatus}\r\n{_options.StartUrl}",
            showRuntimeDownload: false);
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        ShowError("页面进程异常", $"WebView2 进程异常：{e.ProcessFailedKind}", showRuntimeDownload: false);
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        // 默认保存到系统下载目录；不覆盖文件名，交给 WebView2/系统。
        e.DownloadOperation.StateChanged += (_, _) =>
        {
            if (e.DownloadOperation.State != CoreWebView2DownloadState.Completed) return;

            var path = e.DownloadOperation.ResultFilePath;
            if (path is null || !File.Exists(path)) return;

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // 资源管理器定位失败不影响下载结果。
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
        // 外部链接打开失败不阻塞主窗口。
        ShellLauncher.OpenUrl(uri, out _);
    }

    private void ShowError(string title, string message, bool showRuntimeDownload)
    {
        ErrorTitle.Text = title;
        ErrorText.Text = message;
        RuntimeButton.Visibility = showRuntimeDownload ? Visibility.Visible : Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        LoadingPanel.Visibility = Visibility.Collapsed;
        WebView.Visibility = Visibility.Collapsed;
    }

    private async void Retry_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        RuntimeButton.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingText.Text = "正在重连…";

        if (WebView.CoreWebView2 is not null)
        {
            WebView.Visibility = Visibility.Visible;
            WebView.CoreWebView2.Navigate(_options.StartUrl);
            return;
        }

        await InitializeWebViewAsync();
    }

    private void OpenBrowser_Click(object sender, RoutedEventArgs e) =>
        ShellLauncher.OpenUrl(_options.StartUrl, out _);

    private void DownloadRuntime_Click(object sender, RoutedEventArgs e)
    {
        if (ShellLauncher.OpenUrl(RuntimeDownloadUrl, out var error)) return;

        System.Windows.MessageBox.Show(this,
            $"无法打开下载链接：{error}\r\n\r\n请手动访问：\r\n{RuntimeDownloadUrl}",
            "WebView2 Runtime",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (System.Windows.Application.Current is App { IsExiting: false } && _options.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            ((App)System.Windows.Application.Current).NotifyMinimizedToTray();
            return;
        }

        base.OnClosing(e);
    }
}
