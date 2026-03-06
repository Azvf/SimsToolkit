namespace SimsModDesktop.Application.Caching;

public sealed class CacheWarmupObserver
{
    public Action<CacheWarmupProgress>? ReportProgress { get; init; }
    public Action<string>? AppendLog { get; init; }
}
