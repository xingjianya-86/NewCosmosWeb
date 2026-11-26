using System.Windows;
using NewCosmos.Shell.Services;

namespace NewCosmos.Shell;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _singleInstance;
    private ShellTray? _tray;
    private MainWindow? _window;
    private bool _trayHintShown;

    public ShellOptions Options { get; private set; } = new();
    public AgentIntegration Agent { get; private set; } = null!;

    /// <summary>托盘“退出”/更新退出时置位，窗口 Closing 不再拦截。</summary>
    public bool IsExiting { get; private set; }

    /// <summary>可见的主窗口；隐藏到托盘时为 null（用于消息框 owner）。</summary>
    public MainWindow? VisibleMainWindow => _window is { IsVisible: true } ? _window : null;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Options = ShellOptions.Load();

        // 注销/关机时不要被“关闭到托盘”拦截。
        SessionEnding += (_, _) => IsExiting = true;

        _singleInstance = SingleInstanceGuard.Create();
        if (!_singleInstance.IsOwner)
        {
            // 第二个实例：唤醒已有窗口后静默退出，不弹 MessageBox。
            SingleInstanceGuard.NotifyExisting();
            Shutdown();
            return;
        }

        // minimizeToTray=false 时关闭主窗口即退出；true 时只能从托盘“退出”。
        ShutdownMode = Options.MinimizeToTray
            ? ShutdownMode.OnExplicitShutdown
            : ShutdownMode.OnMainWindowClose;

        // NotifyIcon / ContextMenuStrip 使用 WinForms，启用视觉样式后再创建托盘。
        System.Windows.Forms.Application.EnableVisualStyles();

        _singleInstance.ActivationRequested += () =>
            Dispatcher.BeginInvoke(new Action(ShowMainWindow));

        Agent = new AgentIntegration(Options);
        var updates = new UpdateService(Options);

        _window = new MainWindow(Options);
        MainWindow = _window;
        _tray = new ShellTray(this, Agent, updates);
        _window.Show();
    }

    /// <summary>显示/还原主窗口（托盘双击、单实例唤醒、菜单项共用）。</summary>
    public void ShowMainWindow()
    {
        if (_window is null) return;

        try
        {
            if (!_window.IsVisible)
                _window.Show();

            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;

            _window.ShowInTaskbar = true;
            _window.Activate();
            _window.Topmost = true;
            _window.Topmost = false;
            _window.Focus();
        }
        catch
        {
            // 窗口正在关闭时忽略激活请求。
        }
    }

    public void NotifyMinimizedToTray()
    {
        if (_trayHintShown) return;
        _trayHintShown = true;
        _tray?.ShowBalloon("NewCosmos", "已最小化到托盘，双击托盘图标可重新打开。");
    }

    /// <summary>真正退出：先撤托盘，再关闭窗口（IsExiting 阻止再次最小化），最后 Shutdown。</summary>
    public void ExitApp()
    {
        if (IsExiting) return;
        IsExiting = true;

        try { _tray?.Dispose(); }
        catch { /* 忽略 */ }
        _tray = null;

        try { _window?.Close(); }
        catch { /* 忽略 */ }

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _tray?.Dispose(); }
        catch { /* 忽略 */ }
        _tray = null;

        try { _singleInstance?.Dispose(); }
        catch { /* 忽略 */ }
        _singleInstance = null;

        base.OnExit(e);
    }
}
