using System.Collections.Concurrent;

namespace NewCosmos.Server.Infrastructure;

/// <summary>
/// 登录失败限流（进程内）：同一 IP + 用户名 5 分钟内失败 5 次后锁定 5 分钟。
/// 仅用于挡住暴力猜密码；成功登录后清除计数。多实例部署时需改为共享存储。
/// </summary>
public sealed class LoginThrottle
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public bool IsBlocked(string ip, string username, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (!_entries.TryGetValue(BuildKey(ip, username), out var entry))
        {
            return false;
        }

        lock (entry.Sync)
        {
            var now = DateTime.UtcNow;
            if (entry.LockedUntil > now)
            {
                retryAfter = entry.LockedUntil - now;
                return true;
            }

            if (entry.LockedUntil != default && entry.LockedUntil <= now)
            {
                entry.Failures = 0;
                entry.FirstFailureAt = default;
                entry.LockedUntil = default;
            }

            return false;
        }
    }

    public void RecordFailure(string ip, string username)
    {
        var key = BuildKey(ip, username);
        var entry = _entries.GetOrAdd(key, _ => new Entry());
        lock (entry.Sync)
        {
            var now = DateTime.UtcNow;
            if (entry.FirstFailureAt == default || now - entry.FirstFailureAt > FailureWindow)
            {
                entry.FirstFailureAt = now;
                entry.Failures = 0;
            }

            entry.Failures++;
            if (entry.Failures >= MaxFailures)
            {
                entry.LockedUntil = now + LockDuration;
            }
        }
    }

    public void RecordSuccess(string ip, string username)
    {
        _entries.TryRemove(BuildKey(ip, username), out _);
    }

    /// <summary>清理过期条目，避免长期运行内存增长。</summary>
    public void Prune()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _entries)
        {
            var entry = pair.Value;
            lock (entry.Sync)
            {
                var expired = entry.LockedUntil != default
                    ? entry.LockedUntil <= now
                    : entry.FirstFailureAt != default && now - entry.FirstFailureAt > FailureWindow;
                if (expired)
                {
                    _entries.TryRemove(pair.Key, out _);
                }
            }
        }
    }

    private static string BuildKey(string ip, string username) =>
        $"{(string.IsNullOrWhiteSpace(ip) ? "unknown" : ip.Trim())}|{username.Trim().ToLowerInvariant()}";

    private sealed class Entry
    {
        public object Sync { get; } = new();
        public int Failures { get; set; }
        public DateTime FirstFailureAt { get; set; }
        public DateTime LockedUntil { get; set; }
    }
}
