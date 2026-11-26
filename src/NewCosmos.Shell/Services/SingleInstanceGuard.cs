using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace NewCosmos.Shell.Services;

/// <summary>
/// 单实例守卫：Mutex 判定 + 命名 EventWaitHandle 唤醒已有窗口。
/// 第二个实例只负责 Set 事件后退出，不再弹 MessageBox。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = "NewCosmos_Shell_SingleInstance";
    private const string ActivateEventName = "NewCosmos_Shell_Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private ManualResetEvent? _exitEvent;
    private Thread? _listener;
    private bool _disposed;

    private SingleInstanceGuard(bool isOwner) => IsOwner = isOwner;

    /// <summary>当前进程是否持有单实例 Mutex。</summary>
    public bool IsOwner { get; }

    /// <summary>收到“显示主窗口”请求；可能在监听线程触发，调用方需切回 UI 线程。</summary>
    public event Action? ActivationRequested;

    public static SingleInstanceGuard Create()
    {
        // 不使用 initiallyOwned：上一进程崩溃留下的 AbandonedMutex 也能被新进程接管。
        var mutex = new Mutex(false, MutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }

        if (!owned)
        {
            mutex.Dispose();
            return new SingleInstanceGuard(false);
        }

        var guard = new SingleInstanceGuard(true) { _mutex = mutex };
        guard._activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        guard._exitEvent = new ManualResetEvent(false);
        guard._listener = new Thread(guard.ListenLoop)
        {
            IsBackground = true,
            Name = "NewCosmos.Shell.ActivateListener"
        };
        guard._listener.Start();
        return guard;
    }

    /// <summary>第二个实例调用：唤醒已运行窗口；失败时静默退出（不弹框阻断）。</summary>
    public static bool NotifyExisting(int timeoutMs = 4000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            try
            {
                using var handle = EventWaitHandle.OpenExisting(ActivateEventName);
                handle.Set();
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                // 第一个实例还在启动：稍后重试。
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        return false;
    }

    private void ListenLoop()
    {
        try
        {
            while (true)
            {
                var index = WaitHandle.WaitAny([_activateEvent!, _exitEvent!]);
                if (index != 0) return;
                ActivationRequested?.Invoke();
            }
        }
        catch (ObjectDisposedException)
        {
            // 退出过程中句柄被释放，正常结束监听。
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Shell] 单实例唤醒监听异常: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _exitEvent?.Set(); } catch { /* 忽略 */ }
        try { _listener?.Join(500); } catch { /* 忽略 */ }

        _activateEvent?.Dispose();
        _exitEvent?.Dispose();

        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch { /* 未持有或已释放 */ }
            _mutex.Dispose();
        }
    }
}
