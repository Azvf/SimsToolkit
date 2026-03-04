namespace SimsModDesktop.Application.Mods;

public interface IModItemIndexStore
{
    Task<ModPackageIndexState?> TryGetPackageStateAsync(string packagePath, CancellationToken cancellationToken = default);
    Task ReplacePackagesFastAsync(IReadOnlyList<ModItemFastIndexBuildResult> buildResults, CancellationToken cancellationToken = default);
    Task ReplacePackageFastAsync(ModItemFastIndexBuildResult buildResult, CancellationToken cancellationToken = default);
    Task ApplyItemEnrichmentBatchesAsync(IReadOnlyList<ModItemEnrichmentBatch> batches, CancellationToken cancellationToken = default);
    Task ApplyItemEnrichmentBatchAsync(ModItemEnrichmentBatch batch, CancellationToken cancellationToken = default);
    Task ReplacePackageAsync(ModItemIndexBuildResult buildResult, CancellationToken cancellationToken = default);
    Task DeletePackageAsync(string packagePath, CancellationToken cancellationToken = default);
    Task<ModItemCatalogPage> QueryPageAsync(ModItemCatalogQuery query, CancellationToken cancellationToken = default);
    Task<ModItemInspectDetail?> TryGetInspectAsync(string itemKey, CancellationToken cancellationToken = default);
    Task<int> CountIndexedPackagesAsync(CancellationToken cancellationToken = default);
}
