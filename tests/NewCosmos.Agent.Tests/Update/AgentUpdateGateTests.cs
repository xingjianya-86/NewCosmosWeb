using NewCosmos.Agent.Services.Update;

namespace NewCosmos.Agent.Tests.Update;

/// <summary>空闲门控：暂停/恢复、在执行任务计数、等待归零与超时两条路径。</summary>
public class AgentUpdateGateTests
{
    [Fact]
    public async Task WaitForIdle_ReturnsImmediately_WhenNothingInFlight()
    {
        var gate = new AgentUpdateGate();
        gate.RequestPause();

        // 没有在执行任务时不该等：即使超时给 0 也算"已空闲"。
        Assert.True(await gate.WaitForIdleAsync(TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public async Task WaitForIdle_ReturnsFalse_WhileJobStillRunning()
    {
        var gate = new AgentUpdateGate();
        gate.BeginJob();
        gate.RequestPause();

        Assert.True(gate.IsPauseRequested);
        Assert.Equal(1, gate.InFlight);
        Assert.False(await gate.WaitForIdleAsync(TimeSpan.FromMilliseconds(80), CancellationToken.None));

        gate.EndJob();
        Assert.Equal(0, gate.InFlight);
    }

    [Fact]
    public async Task WaitForIdle_CompletesAsSoonAsJobEnds()
    {
        var gate = new AgentUpdateGate();
        gate.BeginJob();
        gate.RequestPause();

        var waiting = gate.WaitForIdleAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(50);
        Assert.False(waiting.IsCompleted); // 任务还在跑，不能提前放行

        gate.EndJob();
        Assert.True(await waiting);
    }

    [Fact]
    public async Task WaitForIdle_AfterTimeout_StillObservesLaterJobEnd()
    {
        var gate = new AgentUpdateGate();
        gate.BeginJob();
        gate.RequestPause();

        Assert.False(await gate.WaitForIdleAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));

        gate.EndJob();
        // 超时那次的等待句柄已被 EndJob 关闭，后续等待要么立即归零、要么重建等待句柄。
        Assert.True(await gate.WaitForIdleAsync(TimeSpan.Zero, CancellationToken.None));
    }

    [Fact]
    public void Resume_RestoresClaiming_AndEndJobKeepsCountBalanced()
    {
        var gate = new AgentUpdateGate();
        gate.BeginJob();
        gate.BeginJob();
        gate.RequestPause();
        Assert.True(gate.IsPauseRequested);

        gate.Resume();
        Assert.False(gate.IsPauseRequested);

        gate.EndJob();
        gate.EndJob();
        Assert.Equal(0, gate.InFlight);
    }

    [Fact]
    public void PauseIsIdempotent_AndResumeWithoutPauseIsHarmless()
    {
        var gate = new AgentUpdateGate();
        gate.Resume(); // 没暂停就恢复：不能有副作用
        gate.RequestPause();
        gate.RequestPause();
        Assert.True(gate.IsPauseRequested);

        gate.Resume();
        Assert.False(gate.IsPauseRequested);
    }

    [Fact]
    public async Task WaitForIdle_Throws_WhenCancellationTokenCancelled()
    {
        var gate = new AgentUpdateGate();
        gate.BeginJob();
        gate.RequestPause();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => gate.WaitForIdleAsync(TimeSpan.FromSeconds(5), cts.Token));

        gate.EndJob();
    }

    [Fact]
    public async Task WaitForIdle_DoesNotCompleteEarly_WithoutPause()
    {
        // 门控不依赖"是否暂停"，只等在执行任务归零（调用方通常先 RequestPause）。
        var gate = new AgentUpdateGate();
        gate.BeginJob();

        var waiting = gate.WaitForIdleAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        await Task.Delay(30);
        Assert.False(waiting.IsCompleted);

        gate.EndJob();
        Assert.True(await waiting);
    }
}
