using SimsModDesktop.Application.Caching;

namespace SimsModDesktop.Presentation.ViewModels;

internal sealed class MainWindowCacheWarmupHost
{
    public required Action<CacheWarmupProgress> ReportProgress { get; init; }
    public required Action<string> AppendLog { get; init; }
}
