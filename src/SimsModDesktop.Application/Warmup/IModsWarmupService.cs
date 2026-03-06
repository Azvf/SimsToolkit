using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Mods;

namespace SimsModDesktop.Application.Warmup;

public interface IModsWarmupService
{
    Task<ModPackageInventoryRefreshResult> EnsureWorkspaceReadyAsync(
        string modsRootPath,
        CacheWarmupObserver? observer = null,
        CancellationToken cancellationToken = default);

    void PauseWarmup(string modsRootPath, string reason);

    bool TryGetWarmupState(string modsRootPath, out WarmupStateSnapshot? state);

    bool QueueQueryIdlePrewarm(ModItemCatalogQuery query, string trigger);

    void QueuePriorityDeepEnrichment(string modsRootPath, IReadOnlyCollection<string> itemKeys);

    void Reset();
}
