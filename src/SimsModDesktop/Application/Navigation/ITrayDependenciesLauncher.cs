namespace SimsModDesktop.Services;

public interface ITrayDependenciesLauncher
{
    Task RunForTrayItemAsync(
        string trayRootPath,
        string trayItemKey,
        CancellationToken cancellationToken = default);
}
