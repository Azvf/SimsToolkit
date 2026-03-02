namespace SimsModDesktop.Application.Mods;

public interface IDeepModItemEnrichmentService
{
    Task<ModItemEnrichmentBatch> EnrichPackageAsync(
        string packagePath,
        CancellationToken cancellationToken = default);
}
