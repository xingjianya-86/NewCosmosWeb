using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Forms = System.Windows.Forms;

namespace NewCosmos.Agent.Services;

/// <summary>
/// GUI（WinExe）进程与控制台的桥接：
/// <list type="bullet">
/// <item>托盘模式：不附着控制台，所有 Console 写入丢弃，日志只走文件 + 托盘气泡；</item>
/// <item>CLI 且从控制台启动：AttachConsole 父进程并重绑 stdout/stderr，命令行可见输出；</item>
/// <item>CLI 但从资源管理器双击：不弹黑窗，输出暂存，结束/出错时用 MessageBox 展示。</item>
/// </list>
/// 相比直接 <c>Console.WriteLine</c>，这里的兜底不依赖控制台句柄是否存在。
/// </summary>
public static class ConsoleHost
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    private const int MaxCapturedChars = 12000;

    private static bool _initialized;
    private static bool _trayMode;
    private static bool _attached;
    private static bool _outputBound;
    private static CapturedTextWriter? _capture;
    private static StreamWriter? _stdout;
    private static StreamWriter? _stderr;

    /// <summary>当前是否已附着父控制台（CLI 从控制台启动时为 true）。</summary>
    public static bool HasConsole => _attached;

    /// <summary>是否有可写的输出目标（已附着控制台、已绑定重定向句柄，或 CLI 的 MessageBox 暂存缓冲）。</summary>
    public static bool HasOutput => _attached || _outputBound || _capture is not null;

    /// <summary>是否托盘模式（无控制台输出）。</summary>
    public static bool IsTrayMode => _trayMode;

    /// <summary>
    /// 初始化输出通道。必须在任何 Console/AgentConsole 写入之前调用一次。
    /// </summary>
    /// <param name="cliMode">true=命令行模式，尝试附着父控制台；false=托盘模式，不附着。</param>
    public static void Initialize(bool cliMode)
    {
        if (_initialized) return;
        _initialized = true;
        _trayMode = !cliMode;

        if (_trayMode)
        {
            // WinExe 没有控制台；把 Console 写入丢到 Null，避免句柄无效时抛 IOException。
            TrySetNullWriters();
            return;
        }

        if (TryAttachParentConsole())
        {
            if (TryBindStandardStreams(allowConsoleDevice: true))
            {
                _attached = true;
                _outputBound = true;
                TrySetOutputEncoding();
                return;
            }

            // 已附着但标准流绑定失败：释放附着，走 MessageBox 暂存，避免“看似有控制台”却无输出。
            try { FreeConsole(); } catch (Exception) { /* 忽略 */ }
        }
        else if (TryBindStandardStreams(allowConsoleDevice: false))
        {
            // 无控制台，但调用方给了有效的 stdout/stderr（管道/重定向）：照常写，不弹 MessageBox。
            _outputBound = true;
            TrySetOutputEncoding();
            return;
        }

        // 从资源管理器双击、或父进程没有控制台：暂存输出，结束时用 MessageBox 展示。
        _capture = new CapturedTextWriter();
        try
        {
            Console.SetOut(_capture);
            Console.SetError(_capture);
        }
        catch (Exception)
        {
            // 极端环境下 SetOut 失败：后续 AgentConsole 会因 HasOutput=false 自动跳过。
            _capture = null;
        }
    }

    /// <summary>
    /// 把 CLI 暂存的输出用 MessageBox 展示（仅未附着控制台时）。
    /// 必须在主线程/STA 线程调用；托盘模式无操作。
    /// </summary>
    public static void CompleteCli()
    {
        if (_trayMode || _attached || _outputBound) return;

        var text = _capture?.GetText().Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (text.Length > MaxCapturedChars)
            text = text[..MaxCapturedChars] + Environment.NewLine + "...（输出过长已截断，详见日志）";

        try
        {
            Forms.MessageBox.Show(
                text,
                "NewCosmos Agent",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Information);
        }
        catch (Exception)
        {
            // MessageBox 失败时不能再回退控制台（无控制台），静默结束。
        }
    }

    /// <summary>
    /// 异常/错误的统一兜底：有控制台写 stderr；CLI 无控制台进暂存缓冲；托盘模式弹 MessageBox。
    /// 不直接依赖 Console，避免 WinExe 无控制台时丢异常。
    /// </summary>
    public static void ReportError(string title, string message)
    {
        if (_attached || _outputBound)
        {
            try
            {
                Console.Error.WriteLine(message);
            }
            catch (Exception)
            {
                // 控制台已失效：无其他可写目标。
            }
            return;
        }

        if (_capture is not null)
        {
            _capture.WriteLine(message);
            return;
        }

        try
        {
            Forms.MessageBox.Show(
                message,
                title,
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Error);
        }
        catch (Exception)
        {
            // 无 UI 可用时只能依赖文件日志。
        }
    }

    /// <summary>释放控制台附着（进程退出前调用）。</summary>
    public static void Detach()
    {
        try
        {
            _stdout?.Flush();
            _stderr?.Flush();
        }
        catch (Exception)
        {
            // 忽略
        }

        if (_attached)
        {
            TrySetNullWriters();

            try
            {
                FreeConsole();
            }
            catch (Exception)
            {
                // 忽略
            }
        }

        _attached = false;
        _outputBound = false;
    }

    private static bool TryAttachParentConsole()
    {
        try
        {
            return AttachConsole(AttachParentProcess);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryBindStandardStreams(bool allowConsoleDevice)
    {
        try
        {
            // 优先沿用进程已有的标准句柄（cmd 重定向到文件/管道时仍能正常输出）；
            // 没有有效句柄时再打开已附着控制台的 CONOUT$ 设备。
            _stdout = TryCreateWriter(StdOutputHandle, "CONOUT$", allowConsoleDevice);
            _stderr = TryCreateWriter(StdErrorHandle, "CONOUT$", allowConsoleDevice);
            if (_stdout is null || _stderr is null)
            {
                try { _stdout?.Dispose(); } catch (Exception) { /* 忽略 */ }
                _stdout = null;
                _stderr = null;
                return false;
            }

            Console.SetOut(_stdout);
            Console.SetError(_stderr);
            return true;
        }
        catch (Exception)
        {
            _stdout = null;
            _stderr = null;
            return false;
        }
    }

    private static StreamWriter? TryCreateWriter(int stdHandle, string deviceName, bool allowConsoleDevice)
    {
        var existing = GetStdHandle(stdHandle);
        SafeFileHandle handle;

        if (!IsInvalidHandle(existing))
        {
            handle = new SafeFileHandle(existing, ownsHandle: false);
        }
        else if (allowConsoleDevice)
        {
            handle = CreateFile(
                deviceName, GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                return null;
            }
        }
        else
        {
            return null;
        }

        SetStdHandle(stdHandle, handle.DangerousGetHandle());

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        return new StreamWriter(
            new FileStream(handle, FileAccess.Write, bufferSize: 256), encoding)
        {
            AutoFlush = true
        };
    }

    private static void TrySetOutputEncoding()
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (Exception)
        {
            // 某些重定向/无控制台环境不支持切换编码：继续用默认编码。
        }
    }

    private static void TrySetNullWriters()
    {
        try
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);
        }
        catch (Exception)
        {
            // 忽略
        }
    }

    private static bool IsInvalidHandle(IntPtr handle)
        => handle == IntPtr.Zero || handle == new IntPtr(-1);

    /// <summary>有上限的线程安全文本缓冲，避免 CLI 长时间运行（如 --worker）无限增长。</summary>
    private sealed class CapturedTextWriter : TextWriter
    {
        private readonly StringBuilder _buffer = new();
        private readonly object _gate = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_gate)
            {
                if (_buffer.Length < MaxCapturedChars) _buffer.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            lock (_gate)
            {
                if (_buffer.Length >= MaxCapturedChars) return;
                var remaining = MaxCapturedChars - _buffer.Length;
                _buffer.Append(value.Length <= remaining ? value : value[..remaining]);
            }
        }

        public override void WriteLine(string? value)
        {
            Write(value);
            Write(Environment.NewLine);
        }

        public string GetText()
        {
            lock (_gate)
            {
                return _buffer.ToString();
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int nStdHandle, IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);
}
