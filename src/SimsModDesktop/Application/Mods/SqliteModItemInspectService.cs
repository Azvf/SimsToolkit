namespace SimsModDesktop.Application.Mods;

public sealed class SqliteModItemInspectService : IModItemInspectService
{
    private readonly IModItemIndexStore _store;

    public SqliteModItemInspectService(IModItemIndexStore store)
    {
        _store = store;
    }

    public Task<ModItemInspectDetail?> TryGetAsync(
        string itemKey,
        CancellationToken cancellationToken = default)
    {
        return _store.TryGetInspectAsync(itemKey, cancellationToken);
    }
}
