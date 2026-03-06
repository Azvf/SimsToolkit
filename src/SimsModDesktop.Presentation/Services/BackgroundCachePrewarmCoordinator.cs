using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Caching;

namespace SimsModDesktop.Presentation.Services;

public sealed class BackgroundCachePrewarmCoordinator : IBackgroundCachePrewarmCoordinator
{
    private const string SourceKeyScopeSeparator = "\u001F";
    private readonly ConcurrentDictionary<string, JobEntry> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<BackgroundCachePrewarmCoordinator> _logger;

    public BackgroundCachePrewarmCoordinator(ILogger<BackgroundCachePrewarmCoordinator>? logger = null)
    {
        _logger = logger ?? NullLogger<BackgroundCachePrewarmCoordinator>.Instance;
    }

    public bool TryQueue(
        BackgroundPrewarmJobKey key,
        Func<CancellationToken, Task> work,
        string description)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(work);

        var jobId = key.ToString();
        var entry = new JobEntry(key, description);
        if (!_jobs.TryAdd(jobId, entry))
        {
            return false;
        }

        entry.Task = Task.Run(() => RunJobAsync(entry, work), CancellationToken.None);
        return true;
    }

    public bool TryGetState(
        BackgroundPrewarmJobKey key,
        out BackgroundPrewarmJobState? state)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_jobs.TryGetValue(key.ToString(), out var entry))
        {
            state = entry.ToState();
            return true;
        }

        state = null;
        return false;
    }

    public void Cancel(
        BackgroundPrewarmJobKey key,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_jobs.TryGetValue(key.ToString(), out var entry))
        {
            entry.Cancel(reason);
        }
    }

    public void CancelBySource(
        string sourceKey,
        string reason,
        string? jobType = null)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return;
        }

        foreach (var entry in _jobs.Values.Where(candidate =>
                     MatchesSource(candidate.Key.SourceKey, sourceKey) &&
                     (string.IsNullOrWhiteSpace(jobType) ||
                      string.Equals(candidate.Key.JobType, jobType, StringComparison.OrdinalIgnoreCase))))
        {
            entry.Cancel(reason);
        }
    }

    public void Reset(string reason = "reset")
    {
        foreach (var entry in _jobs.Values)
        {
            entry.Cancel(reason);
        }

        _jobs.Clear();
    }

    private async Task RunJobAsync(
        JobEntry entry,
        Func<CancellationToken, Task> work)
    {
        try
        {
            entry.UpdateState(BackgroundPrewarmJobRunState.Running, "running");
            _logger.LogInformation(
                "cacheprewarm.job.start jobType={JobType} sourceKey={SourceKey} description={Description}",
                entry.Key.JobType,
                entry.Key.SourceKey,
                entry.Description);

            await work(entry.CancellationTokenSource.Token).ConfigureAwait(false);
            entry.UpdateState(BackgroundPrewarmJobRunState.Completed, "completed");
            _logger.LogInformation(
                "cacheprewarm.job.done jobType={JobType} sourceKey={SourceKey}",
                entry.Key.JobType,
                entry.Key.SourceKey);
        }
        catch (OperationCanceledException) when (entry.CancellationTokenSource.IsCancellationRequested)
        {
            entry.UpdateState(BackgroundPrewarmJobRunState.Canceled, entry.CancelMessage);
            _logger.LogInformation(
                "cacheprewarm.job.cancel jobType={JobType} sourceKey={SourceKey} reason={Reason}",
                entry.Key.JobType,
                entry.Key.SourceKey,
                entry.CancelMessage);
        }
        catch (Exception ex)
        {
            entry.UpdateState(BackgroundPrewarmJobRunState.Failed, ex.Message);
            _logger.LogWarning(
                ex,
                "cacheprewarm.job.fail jobType={JobType} sourceKey={SourceKey}",
                entry.Key.JobType,
                entry.Key.SourceKey);
        }
    }

    private sealed class JobEntry
    {
        private readonly object _gate = new();

        public JobEntry(BackgroundPrewarmJobKey key, string description)
        {
            Key = key;
            Description = description;
        }

        public BackgroundPrewarmJobKey Key { get; }
        public string Description { get; }
        public CancellationTokenSource CancellationTokenSource { get; } = new();
        public string CancelMessage { get; private set; } = "canceled";
        public Task? Task { get; set; }
        private BackgroundPrewarmJobRunState RunState { get; set; } = BackgroundPrewarmJobRunState.Scheduled;
        private string Message { get; set; } = "scheduled";
        private DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

        public void Cancel(string reason)
        {
            lock (_gate)
            {
                CancelMessage = string.IsNullOrWhiteSpace(reason) ? "canceled" : reason;
                UpdatedAtUtc = DateTimeOffset.UtcNow;
            }

            try
            {
                CancellationTokenSource.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void UpdateState(BackgroundPrewarmJobRunState runState, string message)
        {
            lock (_gate)
            {
                RunState = runState;
                Message = message;
                UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        }

        public BackgroundPrewarmJobState ToState()
        {
            lock (_gate)
            {
                return new BackgroundPrewarmJobState
                {
                    Key = Key,
                    RunState = RunState,
                    Message = Message,
                    Description = Description,
                    UpdatedAtUtc = UpdatedAtUtc
                };
            }
        }
    }

    private static bool MatchesSource(string candidateSourceKey, string sourceKey)
    {
        if (string.Equals(candidateSourceKey, sourceKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return candidateSourceKey.StartsWith(
            sourceKey + SourceKeyScopeSeparator,
            StringComparison.OrdinalIgnoreCase);
    }
}
