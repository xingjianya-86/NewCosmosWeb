namespace NewCosmos.Agent.Services.Update;

/// <summary>
/// 升级空闲门控：升级前必须"停新活、等旧活干完"，否则安装器会覆盖正在执行打印任务的程序目录。
///
/// 协作方：
/// <list type="bullet">
/// <item><see cref="JobWorker"/>：每次 claim 前检查 <see cref="IsPauseRequested"/>，暂停期间只短延时不认领；
/// <c>ProcessJobAsync</c> 的 try/finally 配对 <see cref="BeginJob"/>/<see cref="EndJob"/> 统计在执行任务数。</item>
/// <item>AgentUpdateService：安装前 <see cref="RequestPause"/> → <see cref="WaitForIdleAsync"/> 等归零；
/// 放弃/失败时 <see cref="Resume"/> 恢复认领。</item>
/// </list>
///
/// 门控以可选参数注入（null = 不启用），未接入升级的运行模式行为完全不变，保证零回归。
/// </summary>
public sealed class AgentUpdateGate
{
    private readonly object _sync = new();
    private int _inFlight;
    private bool _pauseRequested;
    private TaskCompletionSource? _idleTcs;

    /// <summary>是否已请求暂停认领（JobWorker 只读，每次轮询检查一次）。</summary>
    public bool IsPauseRequested
    {
        get { lock (_sync) return _pauseRequested; }
    }

    /// <summary>当前在执行的任务数（诊断与测试用）。</summary>
    public int InFlight
    {
        get { lock (_sync) return _inFlight; }
    }

    /// <summary>请求暂停：此后 JobWorker 不再 claim 新任务；已在执行的任务继续跑完。</summary>
    public void RequestPause()
    {
        lock (_sync)
        {
            _pauseRequested = true;
            // 没有在执行任务时直接放行，避免 WaitForIdle 白等一次轮询周期。
            if (_inFlight <= 0)
                CompleteIdleLocked();
        }
    }

    /// <summary>
    /// 恢复认领：升级放弃/失败时调用。是空操作式的幂等方法——没暂停时调用也不会有副作用。
    /// 注意它**不**唤醒等待中的 WaitForIdle：等待方等的是"在执行任务归零"，与是否暂停无关。
    /// </summary>
    public void Resume()
    {
        lock (_sync)
        {
            _pauseRequested = false;
        }
    }

    /// <summary>开始一个任务（JobWorker.ProcessJobAsync 入口）。</summary>
    public void BeginJob()
    {
        lock (_sync)
        {
            _inFlight++;
        }
    }

    /// <summary>任务结束（JobWorker.ProcessJobAsync 的 finally）；归零时唤醒等待方。</summary>
    public void EndJob()
    {
        lock (_sync)
        {
            if (_inFlight > 0)
                _inFlight--;

            if (_inFlight <= 0 && _idleTcs is { } idleTcs && !idleTcs.Task.IsCompleted)
                idleTcs.TrySetResult();
        }
    }

    /// <summary>
    /// 等待在执行任务归零：已归零立即 true；超时 false（调用方应 <see cref="Resume"/> 并推迟本轮升级）。
    /// 传入的 CancellationToken 被取消时抛 <see cref="OperationCanceledException"/>（Agent 退出路径），
    /// 调用方必须在 catch 里 Resume，否则 Agent 会卡在"不认领"状态。
    /// </summary>
    public async Task<bool> WaitForIdleAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task idleTask;
        lock (_sync)
        {
            if (_inFlight <= 0)
                return true;

            idleTask = GetIdleTaskLocked();
        }

        if (timeout <= TimeSpan.Zero)
            return false;

        cancellationToken.ThrowIfCancellationRequested();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeoutTask = Task.Delay(timeout, timeoutCts.Token);

        var completed = await Task.WhenAny(idleTask, timeoutTask).ConfigureAwait(false);
        if (completed == idleTask)
        {
            // 取消计时器，别让 60 秒的定时器一直挂着。
            timeoutCts.Cancel();
            return true;
        }

        // 超时与取消的区分：令牌被取消要抛出去（退出路径），否则返回 false 表示"没等到空闲"。
        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    /// <summary>取等待句柄；上一次等待已完成时复用会造成"立刻返回"的假信号，故重建。</summary>
    private Task GetIdleTaskLocked()
    {
        if (_idleTcs is null || _idleTcs.Task.IsCompleted)
            _idleTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        return _idleTcs.Task;
    }

    private void CompleteIdleLocked()
    {
        if (_idleTcs is { } idleTcs && !idleTcs.Task.IsCompleted)
            idleTcs.TrySetResult();
    }
}
