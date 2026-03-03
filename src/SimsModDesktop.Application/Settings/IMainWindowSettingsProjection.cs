namespace SimsModDesktop.Application.Settings;

public interface IMainWindowSettingsProjection
{
    MainWindowResolvedSettings Resolve(AppSettings settings, IReadOnlyList<SimsAction> availableToolkitActions);
}
