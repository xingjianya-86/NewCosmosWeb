using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace NewCosmos.Shell.Services;

/// <summary>
/// 托盘图标与右键菜单：显示窗口 / Agent 状态与启停 / 日志、配置、输出、下载目录 / 检查更新 / 退出。
/// NotifyIcon 在 WPF UI 线程创建，事件也由 WPF 消息泵派发。
/// </summary>
public sealed class ShellTray : IDisposable
{
    private readonly App _app;
    private readonly AgentIntegration _agent;
    private readonly UpdateService _updates;

    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Drawing.Icon _icon;
    private readonly Drawing.Font _boldFont;

    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Forms.ToolStripMenuItem _startItem;
    private bool _disposed;

    public ShellTray(App app, AgentIntegration agent, UpdateService updates)
    {
        _app = app;
        _agent = agent;
        _updates = updates;

        _icon = CreateTrayIcon();
        _boldFont = new Drawing.Font(Drawing.SystemFonts.MenuFont!, Drawing.FontStyle.Bold);

        _statusItem = new Forms.ToolStripMenuItem();
        _statusItem.Click += (_, _) => ShowAgentStatus();

        _startItem = new Forms.ToolStripMenuItem();
        _startItem.Click += OnStartAgent;

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "NewCosmos 社会救助管理系统",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => _app.ShowMainWindow();
        _notifyIcon.ContextMenuStrip.Opening += (_, _) => RefreshMenuState();

        RefreshMenuState();
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();

        var showWindow = new Forms.ToolStripMenuItem("显示主窗口") { Font = _boldFont };
        showWindow.Click += (_, _) => _app.ShowMainWindow();

        var openLogs = new Forms.ToolStripMenuItem("打开 Agent 日志");
        openLogs.Click += (_, _) => OpenFolder(_agent.LogDirectory, "Agent 日志");

        var openConfig = new Forms.ToolStripMenuItem("打开 Agent 配置");
        openConfig.Click += (_, _) => OpenAgentConfig();

        var openOutput = new Forms.ToolStripMenuItem("打开输出目录");
        openOutput.Click += (_, _) => OpenFolder(_agent.ResolveOutputRoot(), "输出目录");

        var openDownloads = new Forms.ToolStripMenuItem("打开下载目录");
        openDownloads.Click += (_, _) => OpenFolder(_agent.DownloadsDirectory, "下载目录");

        var checkUpdate = new Forms.ToolStripMenuItem("检查更新");
        checkUpdate.Click += OnCheckUpdate;

        var exit = new Forms.ToolStripMenuItem("退出");
        exit.Click += (_, _) => _app.ExitApp();

        menu.Items.Add(showWindow);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_statusItem);
        menu.Items.Add(_startItem);
        menu.Items.Add(openLogs);
        menu.Items.Add(openConfig);
        menu.Items.Add(openOutput);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(openDownloads);
        menu.Items.Add(checkUpdate);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);
        return menu;
    }

    private void RefreshMenuState()
    {
        try
        {
            var status = _agent.GetStatus();
            _statusItem.Text = status.IsRunning
                ? "Agent 状态：运行中"
                : status.ExecutablePath is null
                    ? "Agent 状态：未安装"
                    : "Agent 状态：未运行";
            _startItem.Text = status.IsRunning ? "重启 Agent" : "启动 Agent";
            _startItem.Enabled = true;
        }
        catch
        {
            _statusItem.Text = "Agent 状态：未知";
            _startItem.Text = "启动 Agent";
            _startItem.Enabled = true;
        }
    }

    private void ShowAgentStatus()
    {
        RunOnUi(() =>
        {
            var status = _agent.GetStatus();
            var lines = new List<string>
            {
                status.IsRunning ? "运行状态：运行中" : "运行状态：未运行"
            };

            if (status.ExecutablePath is null)
            {
                lines.Add("可执行文件：未找到");
            }
            else
            {
                lines.Add($"可执行文件：{status.ExecutablePath}");
                lines.Add($"查找来源：{status.ExecutableSource}");
            }

            if (!string.IsNullOrWhiteSpace(status.Version))
                lines.Add($"文件版本：{status.Version}");

            lines.Add($"配置文件：{_agent.ConfigPath}");
            lines.Add($"日志目录：{_agent.LogDirectory}");
            lines.Add($"输出目录：{_agent.ResolveOutputRoot()}");

            ShowInfo(string.Join("\r\n", lines), "Agent 状态");
        });
    }

    private async void OnStartAgent(object? sender, EventArgs e)
    {
        var running = _agent.IsRunning();
        ShowBalloon("NewCosmos Agent", running ? "正在重启 Agent…" : "正在启动 Agent…");

        AgentOperationResult result;
        try
        {
            result = await Task.Run(() => running ? _agent.Restart() : _agent.Start());
        }
        catch (Exception ex)
        {
            result = new AgentOperationResult(false, $"操作 Agent 失败：{ex.Message}");
        }

        RunOnUi(() =>
        {
            RefreshMenuState();
            if (result.Success)
                ShowBalloon("NewCosmos Agent", result.Message);
            else
                ShowError(result.Message, "NewCosmos Agent");
        });
    }

    private async void OnCheckUpdate(object? sender, EventArgs e)
    {
        await _updates.CheckAndInstallAsync(
            _app.VisibleMainWindow,
            message => ShowBalloon("NewCosmos 更新", message));
    }

    private void OpenAgentConfig()
    {
        if (!File.Exists(_agent.ConfigPath))
        {
            RunOnUi(() => ShowInfo(
                $"配置文件尚未生成：\r\n{_agent.ConfigPath}\r\n\r\n首次绑定设备后由 Agent 自动创建。",
                "Agent 配置"));
            return;
        }

        if (!ShellLauncher.OpenFile(_agent.ConfigPath, out var error))
            RunOnUi(() => ShowError(error ?? "打开 Agent 配置失败。", "Agent 配置"));
    }

    private void OpenFolder(string path, string title)
    {
        if (!ShellLauncher.OpenFolder(path, out var error))
            RunOnUi(() => ShowError(error ?? $"打开{title}失败。", title));
    }

    public void ShowBalloon(string title, string message,
        Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        RunOnUi(() =>
        {
            try
            {
                _notifyIcon.BalloonTipTitle = title;
                _notifyIcon.BalloonTipText = message;
                _notifyIcon.BalloonTipIcon = icon;
                _notifyIcon.ShowBalloonTip(4000);
            }
            catch
            {
                // 通知区域不可用时忽略。
            }
        });
    }

    private void ShowInfo(string message, string title)
    {
        var owner = _app.VisibleMainWindow;
        if (owner is null)
            System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        else
            System.Windows.MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowError(string message, string title)
    {
        var owner = _app.VisibleMainWindow;
        if (owner is null)
            System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            System.Windows.MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RunOnUi(Action action)
    {
        if (_app.Dispatcher.CheckAccess())
            action();
        else
            _app.Dispatcher.BeginInvoke(action);
    }

    /// <summary>无 .ico 资产：运行时绘制 32×32 品牌色托盘图标，释放原生句柄。</summary>
    private static Drawing.Icon CreateTrayIcon()
    {
        try
        {
            using var bitmap = new Drawing.Bitmap(32, 32);
            using (var graphics = Drawing.Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                graphics.Clear(Drawing.Color.Transparent);
                using var brush = new Drawing.SolidBrush(Drawing.ColorTranslator.FromHtml("#AB3227"));
                graphics.FillEllipse(brush, 1, 1, 30, 30);
                using var font = new Drawing.Font("Segoe UI", 12, Drawing.FontStyle.Bold,
                    Drawing.GraphicsUnit.Pixel);
                using var format = new Drawing.StringFormat
                {
                    Alignment = Drawing.StringAlignment.Center,
                    LineAlignment = Drawing.StringAlignment.Center
                };
                graphics.DrawString("NC", font, Drawing.Brushes.White,
                    new Drawing.RectangleF(0, 0, 32, 32), format);
            }

            var handle = bitmap.GetHicon();
            try
            {
                using var temporary = Drawing.Icon.FromHandle(handle);
                return (Drawing.Icon)temporary.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        catch
        {
            return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _notifyIcon.Visible = false; } catch { /* 忽略 */ }
        _notifyIcon.Dispose();
        _icon.Dispose();
        _boldFont.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
