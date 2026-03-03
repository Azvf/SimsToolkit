namespace SimsModDesktop.Application.Mods;

public sealed class SqliteModItemCatalogService : IModItemCatalogService
{
    private readonly IModItemIndexStore _store;

    public SqliteModItemCatalogService(IModItemIndexStore store)
    {
        _store = store;
    }

    public Task<ModItemCatalogPage> QueryPageAsync(
        ModItemCatalogQuery query,
        CancellationToken cancellationToken = default)
    {
        return _store.QueryPageAsync(query, cancellationToken);
    }
}
