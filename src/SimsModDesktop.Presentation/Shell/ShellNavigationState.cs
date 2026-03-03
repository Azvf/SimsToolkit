namespace SimsModDesktop.Presentation.Shell;

public enum AppSection
{
    Toolkit,
    Mods,
    Tray,
    Saves,
    Settings
}

public sealed record NavigationItem(AppSection Section, string Title);

public sealed class ShellNavigationState
{
    private static readonly IReadOnlyList<NavigationItem> DefaultSections =
    [
        new(AppSection.Toolkit, "Toolkit"),
        new(AppSection.Mods, "Mods"),
        new(AppSection.Tray, "Tray"),
        new(AppSection.Saves, "Saves"),
        new(AppSection.Settings, "Settings")
    ];

    public AppSection SelectedSection { get; private set; } = AppSection.Toolkit;

    public bool IsSidebarExpanded { get; private set; } = true;

    public IReadOnlyList<NavigationItem> Sections => DefaultSections;

    public void Select(AppSection section)
    {
        SelectedSection = section;
    }

    public void SetSidebarExpanded(bool expanded)
    {
        IsSidebarExpanded = expanded;
    }
}
