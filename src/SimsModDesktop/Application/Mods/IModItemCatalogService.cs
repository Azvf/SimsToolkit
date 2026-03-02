namespace SimsModDesktop.Application.Mods;

public interface IModItemCatalogService
{
    Task<ModItemCatalogPage> QueryPageAsync(
        ModItemCatalogQuery query,
        CancellationToken cancellationToken = default);
}
