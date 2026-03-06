using SimsModDesktop.Application.Caching;

namespace SimsModDesktop.Application.Warmup;

public enum WarmupRunState
{
    Idle,
    Running,
    Paused,
    Completed,
    Failed
}

public sealed record WarmupStateSnapshot
{
    public WarmupRunState State { get; init; }
    public CacheWarmupProgress Progress { get; init; } = new();
    public long InventoryVersion { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
