namespace SimsModDesktop.Application.Mods;

public interface IModPackageScanService
{
    Task<IReadOnlyList<ModPackageScanResult>> ScanAsync(
        string modsRoot,
        CancellationToken cancellationToken = default);
}
