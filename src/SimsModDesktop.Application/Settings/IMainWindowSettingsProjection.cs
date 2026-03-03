using SimsModDesktop.Application.Modules;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Settings;

public interface IMainWindowSettingsProjection
{
    AppSettings Capture(MainWindowSettingsSnapshot snapshot, IActionModuleRegistry moduleRegistry);

    MainWindowResolvedSettings Resolve(AppSettings settings, IReadOnlyList<SimsAction> availableToolkitActions);

    void LoadModuleSettings(AppSettings settings, IActionModuleRegistry moduleRegistry);
}
