using SimsModDesktop.Application.Caching;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Application.Warmup;

public interface ITrayWarmupService
{
    Task<PackageIndexSnapshot> EnsureDependencyReadyAsync(
        string modsRootPath,
        CacheWarmupObserver? observer = null,
        CancellationToken cancellationToken = default);

    Task<PackageIndexSnapshot?> AttachToInflightDependencyWarmupIfAny(
        string modsRootPath,
        CacheWarmupObserver? observer = null,
        CancellationToken cancellationToken = default);

    bool QueueDependencyIdlePrewarm(string modsRootPath, string trigger);

    bool TryGetWarmupState(string modsRootPath, out WarmupStateSnapshot? state);

    bool TryGetReadySnapshot(string modsRootPath, out PackageIndexSnapshot? snapshot);

    void Reset();
}
