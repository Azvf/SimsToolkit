using System.ComponentModel;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class NavigationService : INavigationService
{
    private static readonly IReadOnlyList<NavigationItem> AllItems =
    [
        new(AppSection.Toolkit, "Toolkit"),
        new(AppSection.Mods, "Mods"),
        new(AppSection.Tray, "Tray"),
        new(AppSection.Saves, "Saves"),
        new(AppSection.Settings, "Settings")
    ];

    private AppSection _selectedSection = AppSection.Toolkit;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppSection SelectedSection => _selectedSection;
    public IReadOnlyList<NavigationItem> SectionItems => AllItems;

    public void SelectSection(AppSection section)
    {
        if (_selectedSection == section)
        {
            return;
        }

        _selectedSection = section;
        Raise(nameof(SelectedSection));
    }

    private void Raise(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
