using NewCosmos.Services.Core;

namespace NewCosmos.Server.Infrastructure;

/// <summary>
/// 服务端请求级会话闸门。
/// 状态放在 AsyncLocal 中：同一请求的后续异步流可见，不同请求互不影响。
/// </summary>
public sealed class RequestSessionGuard : ISessionGuard
{
    private readonly AsyncLocal<GuardState?> _state = new();

    public Guid? SessionId => _state.Value?.SessionId;

    public bool IsArmed => _state.Value?.Armed == true;

    public bool IsBlocked
    {
        get
        {
            var state = _state.Value;
            if (state is null || !state.Armed || state.BypassDepth > 0 || state.RevokedAtUtc is null)
            {
                return false;
            }

            if (state.Grace is { } grace && DateTime.UtcNow < state.RevokedAtUtc.Value + grace)
            {
                return false;
            }

            return true;
        }
    }

    public void Arm(Guid sessionId)
    {
        var previous = _state.Value;
        _state.Value = new GuardState
        {
            SessionId = sessionId,
            Armed = true,
            BypassDepth = previous?.BypassDepth ?? 0
        };
    }

    public void Disarm()
    {
        var previous = _state.Value;
        _state.Value = new GuardState
        {
            SessionId = null,
            Armed = false,
            BypassDepth = previous?.BypassDepth ?? 0
        };
    }

    public void Revoke(TimeSpan? grace = null)
    {
        var previous = _state.Value;
        if (previous is null || !previous.Armed)
        {
            return;
        }

        _state.Value = new GuardState
        {
            SessionId = previous.SessionId,
            Armed = true,
            RevokedAtUtc = DateTime.UtcNow,
            Grace = grace ?? TimeSpan.Zero,
            BypassDepth = previous.BypassDepth
        };
    }

    public IDisposable Bypass()
    {
        var previous = _state.Value ?? new GuardState();
        _state.Value = new GuardState
        {
            SessionId = previous.SessionId,
            Armed = previous.Armed,
            RevokedAtUtc = previous.RevokedAtUtc,
            Grace = previous.Grace,
            BypassDepth = previous.BypassDepth + 1
        };
        return new BypassScope(this);
    }

    private sealed class BypassScope(RequestSessionGuard owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var current = owner._state.Value;
            if (current is null)
            {
                return;
            }

            owner._state.Value = new GuardState
            {
                SessionId = current.SessionId,
                Armed = current.Armed,
                RevokedAtUtc = current.RevokedAtUtc,
                Grace = current.Grace,
                BypassDepth = Math.Max(0, current.BypassDepth - 1)
            };
        }
    }

    private sealed class GuardState
    {
        public Guid? SessionId { get; init; }
        public bool Armed { get; init; }
        public DateTime? RevokedAtUtc { get; init; }
        public TimeSpan? Grace { get; init; }
        public int BypassDepth { get; init; }
    }
}
