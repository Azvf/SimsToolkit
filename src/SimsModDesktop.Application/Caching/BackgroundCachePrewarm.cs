namespace SimsModDesktop.Application.Caching;

public sealed record BackgroundPrewarmJobKey
{
    public required string JobType { get; init; }
    public required string SourceKey { get; init; }

    public override string ToString()
    {
        return $"{JobType}|{SourceKey}";
    }
}

public enum BackgroundPrewarmJobRunState
{
    Scheduled,
    Running,
    Completed,
    Failed,
    Canceled
}

public sealed record BackgroundPrewarmJobState
{
    public required BackgroundPrewarmJobKey Key { get; init; }
    public BackgroundPrewarmJobRunState RunState { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public interface IBackgroundCachePrewarmCoordinator
{
    bool TryQueue(
        BackgroundPrewarmJobKey key,
        Func<CancellationToken, Task> work,
        string description);

    bool TryGetState(
        BackgroundPrewarmJobKey key,
        out BackgroundPrewarmJobState? state);

    void Cancel(
        BackgroundPrewarmJobKey key,
        string reason);

    void CancelBySource(
        string sourceKey,
        string reason,
        string? jobType = null);

    void Reset(string reason = "reset");
}
