namespace SimsModDesktop.Application.Mods;

public interface IModItemInspectService
{
    Task<ModItemInspectDetail?> TryGetAsync(
        string itemKey,
        CancellationToken cancellationToken = default);
}
