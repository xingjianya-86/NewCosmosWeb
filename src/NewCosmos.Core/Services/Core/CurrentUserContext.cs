namespace NewCosmos.Services.Core;

/// <summary>
/// 当前请求/当前进程的用户快照。
/// Web 服务端每个请求通过 <see cref="Begin"/> 设置 AsyncLocal 快照；
/// 桌面端单用户进程可通过 <see cref="SetDefault"/> 设置进程级默认值。
/// 业务服务只读取这里，不再直接依赖 MAUI 的 App 静态属性。
/// </summary>
public sealed record CurrentUserSnapshot(
    int? UserId,
    Guid? SessionId,
    string UserName,
    string FullName,
    string Phone,
    int? OrganizationId);

public static class CurrentUserContext
{
    private static readonly AsyncLocal<CurrentUserSnapshot?> Ambient = new();
    private static CurrentUserSnapshot? _default;

    public static CurrentUserSnapshot? Current => Ambient.Value ?? _default;

    public static int? UserId => Current?.UserId;
    public static Guid? SessionId => Current?.SessionId;
    public static string UserName => Current?.UserName ?? string.Empty;
    public static string FullName => Current?.FullName ?? string.Empty;
    public static string Phone => Current?.Phone ?? string.Empty;
    public static int? OrganizationId => Current?.OrganizationId;

    /// <summary>请求级快照，Dispose 后恢复前一个值。</summary>
    public static IDisposable Begin(CurrentUserSnapshot snapshot)
    {
        var previous = Ambient.Value;
        Ambient.Value = snapshot;
        return new Scope(previous);
    }

    /// <summary>桌面端单用户进程的默认快照。</summary>
    public static void SetDefault(CurrentUserSnapshot? snapshot) => _default = snapshot;

    public static void ClearDefault() => _default = null;

    private sealed class Scope(CurrentUserSnapshot? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
