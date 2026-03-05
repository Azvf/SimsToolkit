using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Presentation.Diagnostics;
using SimsModDesktop.Presentation.ViewModels;
using SimsModDesktop.Presentation.ViewModels.Panels;

namespace SimsModDesktop.Presentation.Services;

public sealed class TrayDependenciesLauncher : ITrayDependenciesLauncher
{
    private readonly MainWindowViewModel _workspaceVm;
    private readonly TrayDependenciesPanelViewModel _trayDependencies;
    private readonly INavigationService _navigation;
    private readonly ILogger<TrayDependenciesLauncher> _logger;

    public TrayDependenciesLauncher(
        MainWindowViewModel workspaceVm,
        TrayDependenciesPanelViewModel trayDependencies,
        INavigationService navigation,
        ILogger<TrayDependenciesLauncher>? logger = null)
    {
        _workspaceVm = workspaceVm;
        _trayDependencies = trayDependencies;
        _navigation = navigation;
        _logger = logger ?? NullLogger<TrayDependenciesLauncher>.Instance;
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
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} source={Source} target={Target} trayItemKey={TrayItemKey}",
            LogEvents.UiPageSwitchMark,
            "mark",
            "traydependencies",
            "TrayPreview",
            AppSection.Mods,
            _trayDependencies.TrayItemKey);
        _navigation.SelectSection(AppSection.Mods);
        await _workspaceVm.RunTrayDependenciesForTrayItemAsync(_trayDependencies.TrayPath, _trayDependencies.TrayItemKey);
    }
}
