namespace SimsModDesktop.Presentation.Workspaces;

public sealed class ToolkitWorkspaceViewModel;

public sealed class TrayWorkspaceViewModel;

public sealed class ModsWorkspaceViewModel;

public sealed class SavesWorkspaceViewModel;

public sealed class SettingsViewModel;

public sealed class MainShellViewModel
{
    public MainShellViewModel(
        Shell.ShellNavigationState navigation,
        ToolkitWorkspaceViewModel toolkit,
        TrayWorkspaceViewModel tray,
        ModsWorkspaceViewModel mods,
        SavesWorkspaceViewModel saves,
        SettingsViewModel settings)
    {
        Navigation = navigation;
        Toolkit = toolkit;
        Tray = tray;
        Mods = mods;
        Saves = saves;
        Settings = settings;
    }

    public Shell.ShellNavigationState Navigation { get; }

    public ToolkitWorkspaceViewModel Toolkit { get; }

    public TrayWorkspaceViewModel Tray { get; }

    public ModsWorkspaceViewModel Mods { get; }

    public SavesWorkspaceViewModel Saves { get; }

    public SettingsViewModel Settings { get; }
}
