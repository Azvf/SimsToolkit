namespace SimsModDesktop.Application.Mods;

public interface IModPackageInventoryService
{
    Task<ModPackageInventoryRefreshResult> RefreshAsync(
        string modsRoot,
        IProgress<ModPackageInventoryRefreshProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
