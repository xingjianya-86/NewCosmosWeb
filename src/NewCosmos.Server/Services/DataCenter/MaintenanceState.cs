namespace NewCosmos.Server.Services.DataCenter;

/// <summary>
/// 数据库还原期间的全站维护开关。中间件在每个请求前检查；
/// 只放行健康检查与数据中心任务轮询，避免还原过程中其它业务连接继续读写。
/// </summary>
public sealed class MaintenanceState
{
    private volatile bool _active;
    private long _startedAtTicks;

    public bool IsActive => _active;

    public DateTime? StartedAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _startedAtTicks);
            return ticks <= 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
    }

    public void Enter()
    {
        Interlocked.Exchange(ref _startedAtTicks, DateTime.UtcNow.Ticks);
        _active = true;
    }

    public void Exit()
    {
        _active = false;
        Interlocked.Exchange(ref _startedAtTicks, 0);
    }
}
