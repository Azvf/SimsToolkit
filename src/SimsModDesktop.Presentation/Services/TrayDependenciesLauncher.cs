using SimsModDesktop.ViewModels;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.Presentation.Services;

public sealed class TrayDependenciesLauncher : ITrayDependenciesLauncher
{
    private readonly MainWindowViewModel _workspaceVm;
    private readonly TrayDependenciesPanelViewModel _trayDependencies;
    private readonly INavigationService _navigation;

    public TrayDependenciesLauncher(
        MainWindowViewModel workspaceVm,
        TrayDependenciesPanelViewModel trayDependencies,
        INavigationService navigation)
    {
        _workspaceVm = workspaceVm;
        _trayDependencies = trayDependencies;
        _navigation = navigation;
    }

    public async Task RunForTrayItemAsync(
        string trayRootPath,
        string trayItemKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(trayRootPath))
        {
            throw new ArgumentException("Tray path is required.", nameof(trayRootPath));
        }

        if (string.IsNullOrWhiteSpace(trayItemKey))
        {
            throw new ArgumentException("Tray item key is required.", nameof(trayItemKey));
        }

        _trayDependencies.TrayPath = Path.GetFullPath(trayRootPath.Trim());
        _trayDependencies.TrayItemKey = trayItemKey.Trim();
        _navigation.SelectSection(AppSection.Mods);
        await _workspaceVm.RunTrayDependenciesForTrayItemAsync(_trayDependencies.TrayPath, _trayDependencies.TrayItemKey);
    }
}
