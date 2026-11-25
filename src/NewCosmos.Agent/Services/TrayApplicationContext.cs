using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using NewCosmos.Agent.Configuration;
using NewCosmos.Agent.Security;
using NewCosmos.Services.Core;
using Forms = System.Windows.Forms;

namespace NewCosmos.Agent.Services;

/// <summary>
/// 托盘宿主：NotifyIcon + 右键菜单（打开日志目录 / 立即心跳 / 打开配置文件 / 退出）。
/// 后台心跳与任务轮询由 <see cref="AgentServiceRunner"/> 在<b>线程池</b>运行，
/// 避免 WinForms 同步上下文把续体投递到已结束的消息循环上（退出时卡死）。
/// </summary>
public sealed class TrayApplicationContext : Forms.ApplicationContext
{
    private const string IconResourceName = "NewCosmos.Agent.Assets.agent.png";

    private readonly AgentSettings _settings;
    private readonly string _configFilePath;
    private readonly HttpAgentClient _client;
    private readonly ILoggerService _logger;
    private readonly CancellationTokenSource _cts;

    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private Task<int>? _serviceTask;
    private bool _disposed;

    public TrayApplicationContext(
        AgentSettings settings,
        string configFilePath,
        HttpAgentClient client,
        ILoggerService logger,
        CancellationTokenSource cts)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _configFilePath = configFilePath ?? string.Empty;
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cts = cts ?? throw new ArgumentNullException(nameof(cts));

        _icon = LoadTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = $"NewCosmos Agent — {_settings.MachineName}",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => OpenLogDirectory();

        // 已注册：立即启动心跳/任务轮询；未注册：等用户用网页绑定码完成自动注册后再启动，
        // 避免后台循环因缺少 deviceKey 立即退出。
        if (!string.IsNullOrWhiteSpace(DeviceKeyStore.Load(_settings, _configFilePath, _logger)))
        {
            StartBackgroundService();
        }
        else
        {
            _logger.Info("[Tray] 尚未绑定设备：请右键「绑定本机打印机…」输入网页绑定码，首次使用会自动注册本机。");
            ShowUnboundHint();
        }

        _logger.Info($"[Tray] 托盘已启动: Machine={_settings.MachineName}");
    }

    /// <summary>托盘退出后由 Program 等待后台循环收尾并返回进程退出码。</summary>
    public Task<int> Completion => _serviceTask ?? Task.FromResult(0);

    private void StartBackgroundService()
    {
        if (_serviceTask is { IsCompleted: false })
            return;

        // 线程池执行：不捕获 WinForms 同步上下文。
        _serviceTask = Task.Run(() => AgentServiceRunner.RunHeartbeatAndWorkerAsync(
            _settings, _configFilePath, _client, _client, _logger, _cts));
        _ = ObserveServiceAsync(_serviceTask);
        _logger.Info("[Tray] 后台心跳/任务轮询已启动");
    }

    /// <summary>启动后提示一次：未绑定设备时引导用户输入网页绑定码，而不是要求先 --register。</summary>
    private void ShowUnboundHint()
    {
        var timer = new Forms.Timer { Interval = 1500 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            ShowBalloon(
                "NewCosmos Agent",
                "尚未绑定设备。请右键「绑定本机打印机…」输入网页绑定码，首次使用会自动注册本机。",
                Forms.ToolTipIcon.Info);
        };
        timer.Start();
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();

        var openLogs = new Forms.ToolStripMenuItem("打开日志目录");
        openLogs.Click += (_, _) => OpenLogDirectory();

        var heartbeatNow = new Forms.ToolStripMenuItem("立即心跳");
        heartbeatNow.Click += async (_, _) => await SendHeartbeatOnceAsync().ConfigureAwait(true);

        var bindPrinters = new Forms.ToolStripMenuItem("绑定本机打印机…");
        bindPrinters.Click += async (_, _) => await BindPrintersAsync().ConfigureAwait(true);

        var openOutput = new Forms.ToolStripMenuItem("打开输出目录");
        openOutput.Click += (_, _) => OpenOutputDirectory();

        var openConfig = new Forms.ToolStripMenuItem("打开配置文件");
        openConfig.Click += (_, _) => OpenConfigFile();

        var exit = new Forms.ToolStripMenuItem("退出");
        exit.Click += (_, _) => RequestExit();

        menu.Items.Add(openLogs);
        menu.Items.Add(heartbeatNow);
        menu.Items.Add(bindPrinters);
        menu.Items.Add(openOutput);
        menu.Items.Add(openConfig);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exit);
        return menu;
    }

    private async Task SendHeartbeatOnceAsync()
    {
        try
        {
            var code = await HeartbeatRunner
                .RunAsync(_settings, _configFilePath, _client, once: true, _logger, CancellationToken.None)
                .ConfigureAwait(true);

            ShowBalloon("立即心跳", code == 0 ? "心跳已发送成功" : $"心跳失败（退出码 {code}）",
                code == 0 ? Forms.ToolTipIcon.Info : Forms.ToolTipIcon.Warning);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Tray] 手动心跳异常: {ex.Message}");
            ShowBalloon("立即心跳", $"心跳异常: {ex.Message}", Forms.ToolTipIcon.Error);
        }
    }

    /// <summary>托盘菜单：输入 6 位绑定码 → 绑定本机全部打印机 → 补一次心跳刷新在线状态。</summary>
    private async Task BindPrintersAsync()
    {
        try
        {
            string code;
            using (var form = new PrinterBindForm())
            {
                if (form.ShowDialog() != Forms.DialogResult.OK)
                    return;

                code = form.Code;
            }

            var result = await PrinterBindRunner
                .BindAsync(_settings, _configFilePath, _client, code, _logger, CancellationToken.None)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                _logger.Warn($"[Tray] 绑定打印机失败: {result.Message}");
                Forms.MessageBox.Show(
                    result.Message,
                    "绑定本机打印机失败",
                    Forms.MessageBoxButtons.OK,
                    Forms.MessageBoxIcon.Warning);
                return;
            }

            var names = result.Bindings
                .Select(b => b.PrinterName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToArray();
            var detail = names.Length == 0
                ? result.Message
                : result.Message + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, names);

            _logger.Info($"[Tray] 绑定打印机成功: {result.Bindings.Count} 台");
            ShowBalloon("绑定本机打印机", result.Message, Forms.ToolTipIcon.Info);
            Forms.MessageBox.Show(
                detail,
                "绑定成功",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Information);

            // 绑定成功后服务端在线状态可能仍是旧值：主动补一次心跳。
            var heartbeatCode = await HeartbeatRunner
                .RunAsync(_settings, _configFilePath, _client, once: true, _logger, CancellationToken.None)
                .ConfigureAwait(true);

            if (heartbeatCode != 0)
                _logger.Warn($"[Tray] 绑定成功后刷新心跳失败（退出码 {heartbeatCode}）");

            // 首次使用没有 deviceKey 时，后台循环此前未启动；自动注册成功后补启动，
            // 让心跳与任务轮询持续运行（本次绑定已额外补发过一次心跳）。
            StartBackgroundService();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tray] 绑定打印机异常");
            Forms.MessageBox.Show(
                $"绑定异常: {ex.Message}",
                "绑定本机打印机失败",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Error);
        }
    }

    private void OpenLogDirectory()
    {
        try
        {
            var logPath = AgentRuntime.CreateStorageOptions().GetLogPath();
            Directory.CreateDirectory(logPath);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{logPath}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Tray] 打开日志目录失败: {ex.Message}");
            ShowBalloon("打开日志目录失败", ex.Message, Forms.ToolTipIcon.Error);
        }
    }

    /// <summary>托盘菜单：打开批量保存输出目录（{OutputRoot}\高龄月报），不存在时先创建。</summary>
    private void OpenOutputDirectory()
    {
        try
        {
            var folder = OutputFolderService.EnsureCategoryFolder(
                _settings.OutputRoot, OutputFolderService.DefaultCategory);
            OutputFolderService.OpenFolder(folder);
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Tray] 打开输出目录失败: {ex.Message}");
            Forms.MessageBox.Show(
                $"打开输出目录失败: {ex.Message}",
                "打开输出目录",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Warning);
        }
    }

    private void OpenConfigFile()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                ShowBalloon("配置文件不存在", $"{_configFilePath}（首次绑定成功后生成）", Forms.ToolTipIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo(_configFilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Tray] 打开配置文件失败: {ex.Message}");
            ShowBalloon("打开配置文件失败", ex.Message, Forms.ToolTipIcon.Error);
        }
    }

    private void RequestExit()
    {
        AgentConsole.Info("托盘退出：正在停止心跳与任务轮询...");
        _logger.Info("[Tray] 用户选择退出");

        try { _cts.Cancel(); } catch { /* 忽略 */ }

        _notifyIcon.Visible = false;
        ExitThread();
    }

    /// <summary>后台循环异常退出（非用户退出）时给出托盘提示，便于现场发现。</summary>
    private async Task ObserveServiceAsync(Task<int> serviceTask)
    {
        try
        {
            var code = await serviceTask.ConfigureAwait(false);
            if (code == 0 || _cts.IsCancellationRequested) return;

            var message = code == 3
                ? "设备未授权或被禁用，请检查服务端设备状态"
                : $"后台服务退出（退出码 {code}），请查看日志";
            ShowBalloon("NewCosmos Agent", message, Forms.ToolTipIcon.Warning);
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
        catch (Exception ex)
        {
            _logger.Warn($"[Tray] 后台服务异常: {ex.Message}");
        }
    }

    private void ShowBalloon(string title, string message, Forms.ToolTipIcon icon)
    {
        try
        {
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(4000);
        }
        catch (Exception)
        {
            // 托盘气泡失败不影响主流程
        }
    }

    /// <summary>无 .ico 资产：运行时把嵌入 PNG 缩放到 32×32 再生成 Icon（DestroyIcon 释放原生句柄）。</summary>
    private static Icon LoadTrayIcon()
    {
        try
        {
            using var stream = typeof(TrayApplicationContext).Assembly
                .GetManifestResourceStream(IconResourceName);
            if (stream is null)
                return SystemIcons.Application;

            using var source = new Bitmap(stream);
            using var scaled = new Bitmap(source, new Size(32, 32));
            var handle = scaled.GetHicon();
            try
            {
                using var icon = Icon.FromHandle(handle);
                return (Icon)icon.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        catch (Exception)
        {
            return SystemIcons.Application;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;

            try { _notifyIcon.Visible = false; } catch { /* 忽略 */ }
            _notifyIcon.Dispose();
            _icon.Dispose();
            _cts.Dispose();
        }

        base.Dispose(disposing);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
