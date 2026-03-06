using System.Collections.Concurrent;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Warmup;
using SimsModDesktop.Presentation.ViewModels;

namespace SimsModDesktop.Presentation.Warmup;

internal sealed class WarmupTaskSession<T>
{
    private readonly ConcurrentDictionary<int, MainWindowCacheWarmupHost> _hosts = new();
    private readonly object _stateSync = new();
    private int _hostSequence;

    public required string WarmupKey { get; init; }
    public required string ModsRoot { get; init; }
    public required long InventoryVersion { get; init; }
    public required CacheWarmupDomain Domain { get; init; }
    public required CancellationTokenSource WorkerCts { get; init; }
    public required Task<T> Task { get; set; }
    public WarmupRunState State { get; private set; } = WarmupRunState.Running;
    public CacheWarmupProgress LastProgress { get; private set; } = new();
    public string LastMessage { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public Exception? LastError { get; private set; }

    public int AttachHost(MainWindowCacheWarmupHost host)
    {
        var handle = Interlocked.Increment(ref _hostSequence);
        _hosts[handle] = host;
        var progress = LastProgress;
        if (!string.IsNullOrWhiteSpace(progress.Stage) || !string.IsNullOrWhiteSpace(progress.Detail))
        {
            WarmupProgressPublisher.ReportProgress(host, progress);
        }

        return handle;
    }

    public void DetachHost(int handle)
    {
        _hosts.TryRemove(handle, out _);
    }

    public void PublishProgress(CacheWarmupProgress progress)
    {
        lock (_stateSync)
        {
            LastProgress = progress;
            UpdatedAtUtc = DateTimeOffset.UtcNow;
            if (State == WarmupRunState.Running && progress.Percent >= 100)
            {
                State = WarmupRunState.Completed;
            }
        }

        foreach (var host in _hosts.Values)
        {
            WarmupProgressPublisher.ReportProgress(host, progress);
        }
    }

    public void PublishLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        lock (_stateSync)
        {
            LastMessage = message;
            UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        foreach (var host in _hosts.Values)
        {
            WarmupProgressPublisher.AppendLog(host, message);
        }
    }

    public void MarkRunning(string message)
    {
        lock (_stateSync)
        {
            State = WarmupRunState.Running;
            LastMessage = message;
            UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public bool RequestPause(string reason)
    {
        lock (_stateSync)
        {
            if (State != WarmupRunState.Running)
            {
                return false;
            }

            State = WarmupRunState.Paused;
            LastMessage = reason;
            UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        MainWindowCacheWarmupController.SafeCancelToken(WorkerCts);
        return true;
    }

    public void MarkPaused(string message)
    {
        lock (_stateSync)
        {
            State = WarmupRunState.Paused;
            LastMessage = message;
            UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void MarkCompleted(CacheWarmupProgress readyProgress, string message)
    {
        lock (_stateSync)
        {
            State = WarmupRunState.Completed;
            LastMessage = message;
            LastProgress = readyProgress;
            UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public void MarkFailed(Exception exception, string message)
    {
        lock (_stateSync)
        {
            State = WarmupRunState.Failed;
            LastError = exception;
            LastMessage = message;
            UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public WarmupStateSnapshot ToStateSnapshot()
    {
        lock (_stateSync)
        {
            return new WarmupStateSnapshot
            {
                State = State,
                Progress = LastProgress,
                InventoryVersion = InventoryVersion,
                Message = LastMessage,
                UpdatedAtUtc = UpdatedAtUtc
            };
        }
    }
}
