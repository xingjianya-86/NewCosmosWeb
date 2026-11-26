using System.Collections.Concurrent;

namespace NewCosmos.Server.Services.DataCenter;

public enum DataCenterJobState
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>数据中心后台任务快照（跨请求只读 DTO）。</summary>
public sealed record DataCenterJobSnapshot(
    string JobId,
    string Kind,
    string Title,
    DataCenterJobState State,
    int Percent,
    string Message,
    int StartedBy,
    DateTime StartedAt,
    DateTime? FinishedAt,
    string? Error,
    IReadOnlyList<string> Logs);

/// <summary>
/// 数据中心后台任务注册表（单实例内存）：导入、备份、还原、Schema 修复、软删除清理共用。
/// 同一时刻只允许一个任务运行，避免对数据库做并发破坏性操作。
/// </summary>
public sealed class DataCenterJobRegistry
{
    private readonly ConcurrentDictionary<string, JobEntry> _jobs = new(StringComparer.Ordinal);

    /// <summary>已完成任务保留 2 小时，最多保留 50 条。</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromHours(2);
    private const int MaxLogLines = 400;

    public bool TryStart(
        string kind,
        string title,
        int userId,
        out DataCenterJobSnapshot job,
        out string? error)
    {
        Prune();

        if (_jobs.Values.Any(entry => entry.State is DataCenterJobState.Pending or DataCenterJobState.Running))
        {
            job = null!;
            error = "已有数据中心任务正在执行，请等待完成后再试。";
            return false;
        }

        var id = $"dcj_{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}";
        var entry = new JobEntry(id, kind, title, userId);
        _jobs[id] = entry;
        job = entry.ToSnapshot();
        error = null;
        return true;
    }

    public CancellationToken GetCancellationToken(string jobId)
        => GetEntry(jobId)?.Cts.Token ?? CancellationToken.None;

    public void Start(string jobId, string message = "开始执行…")
        => Mutate(jobId, entry =>
        {
            entry.State = DataCenterJobState.Running;
            entry.Percent = 1;
            entry.Message = message;
            entry.AddLog(message);
        });

    public void Report(string jobId, string message, int? percent = null)
        => Mutate(jobId, entry =>
        {
            entry.Message = message;
            entry.AddLog(message);
            if (percent.HasValue)
            {
                entry.Percent = Math.Clamp(percent.Value, 0, 100);
            }
        });

    public void Complete(string jobId, string message)
        => Mutate(jobId, entry =>
        {
            entry.State = DataCenterJobState.Succeeded;
            entry.Percent = 100;
            entry.Message = message;
            entry.FinishedAt = DateTime.UtcNow;
            entry.AddLog(message);
        });

    public void Fail(string jobId, string error)
        => Mutate(jobId, entry =>
        {
            entry.State = DataCenterJobState.Failed;
            entry.Message = error;
            entry.Error = error;
            entry.FinishedAt = DateTime.UtcNow;
            entry.AddLog($"失败：{error}");
        });

    public void Cancel(string jobId, string message = "已取消")
        => Mutate(jobId, entry =>
        {
            entry.State = DataCenterJobState.Cancelled;
            entry.Message = message;
            entry.FinishedAt = DateTime.UtcNow;
            entry.AddLog(message);
            entry.Cts.Cancel();
        });

    public bool TryRequestCancel(string jobId, int userId, out string? error)
    {
        if (!_jobs.TryGetValue(jobId, out var entry))
        {
            error = "任务不存在或已过期。";
            return false;
        }

        if (entry.StartedBy != userId)
        {
            error = "只能取消自己发起的任务。";
            return false;
        }

        if (entry.State is not (DataCenterJobState.Pending or DataCenterJobState.Running))
        {
            error = "任务已结束，无需取消。";
            return false;
        }

        entry.Cts.Cancel();
        entry.Message = "正在取消…";
        entry.AddLog("收到取消请求，正在停止…");
        error = null;
        return true;
    }

    public DataCenterJobSnapshot? Get(string jobId, int userId)
    {
        if (!_jobs.TryGetValue(jobId, out var entry))
        {
            return null;
        }

        return entry.StartedBy == userId ? entry.ToSnapshot() : null;
    }

    public DataCenterJobSnapshot? Get(string jobId) =>
        _jobs.TryGetValue(jobId, out var entry) ? entry.ToSnapshot() : null;

    public IReadOnlyList<DataCenterJobSnapshot> ListActive()
        => _jobs.Values
            .Where(entry => entry.State is DataCenterJobState.Pending or DataCenterJobState.Running)
            .OrderByDescending(entry => entry.StartedAt)
            .Select(entry => entry.ToSnapshot())
            .ToList();

    private JobEntry? GetEntry(string jobId)
        => _jobs.TryGetValue(jobId, out var entry) ? entry : null;

    private void Mutate(string jobId, Action<JobEntry> action)
    {
        if (!_jobs.TryGetValue(jobId, out var entry))
        {
            return;
        }

        lock (entry.Sync)
        {
            action(entry);
        }
    }

    private void Prune()
    {
        var cutoff = DateTime.UtcNow - Retention;
        foreach (var pair in _jobs)
        {
            if (pair.Value.FinishedAt is { } finished && finished < cutoff)
            {
                _jobs.TryRemove(pair.Key, out _);
            }
        }

        var finishedJobs = _jobs.Values
            .Where(entry => entry.FinishedAt.HasValue)
            .OrderByDescending(entry => entry.FinishedAt)
            .Skip(50)
            .ToList();
        foreach (var entry in finishedJobs)
        {
            _jobs.TryRemove(entry.JobId, out _);
        }
    }

    private sealed class JobEntry(string jobId, string kind, string title, int startedBy)
    {
        public string JobId { get; } = jobId;
        public string Kind { get; } = kind;
        public string Title { get; } = title;
        public int StartedBy { get; } = startedBy;
        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public DateTime? FinishedAt { get; set; }
        public DataCenterJobState State { get; set; } = DataCenterJobState.Pending;
        public int Percent { get; set; }
        public string Message { get; set; } = "等待开始…";
        public string? Error { get; set; }
        public CancellationTokenSource Cts { get; } = new();
        public object Sync { get; } = new();
        public List<string> Logs { get; } = [];

        public void AddLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            Logs.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
            if (Logs.Count > MaxLogLines)
            {
                Logs.RemoveRange(0, Logs.Count - MaxLogLines);
            }
        }

        public DataCenterJobSnapshot ToSnapshot()
        {
            lock (Sync)
            {
                return new DataCenterJobSnapshot(
                    JobId,
                    Kind,
                    Title,
                    State,
                    Percent,
                    Message,
                    StartedBy,
                    StartedAt,
                    FinishedAt,
                    Error,
                    Logs.ToArray());
            }
        }
    }
}
