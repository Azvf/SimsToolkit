using System.ComponentModel;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class NavigationService : INavigationService
{
    private static readonly IReadOnlyList<NavigationItem> AllItems =
    [
        new(AppSection.Mods, "Mods", "organize"),
        new(AppSection.Tray, "Tray", "traypreview"),
        new(AppSection.Saves, "Saves", "saves"),
        new(AppSection.Settings, "Settings", "settings")
    ];

    private static readonly IReadOnlyDictionary<AppSection, IReadOnlyList<NavigationItem>> SectionModules =
        new Dictionary<AppSection, IReadOnlyList<NavigationItem>>
        {
            [AppSection.Mods] =
            [
                new(AppSection.Mods, "Organize", "organize"),
                new(AppSection.Mods, "Flatten", "flatten"),
                new(AppSection.Mods, "Normalize", "normalize"),
                new(AppSection.Mods, "Merge", "merge"),
                new(AppSection.Mods, "Find Duplicates", "finddup")
            ],
            [AppSection.Tray] =
            [
                new(AppSection.Tray, "Tray Preview", "traypreview"),
                new(AppSection.Tray, "Tray Dependencies", "traydeps")
            ],
            [AppSection.Saves] = [new(AppSection.Saves, "Saves", "saves")],
            [AppSection.Settings] = [new(AppSection.Settings, "Settings", "settings")]
        };

    private AppSection _selectedSection = AppSection.Mods;
    private string _selectedModuleKey = "organize";

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppSection SelectedSection => _selectedSection;
    public string SelectedModuleKey => _selectedModuleKey;
    public IReadOnlyList<NavigationItem> SectionItems => AllItems;

    public IReadOnlyList<NavigationItem> CurrentModules =>
        SectionModules.TryGetValue(_selectedSection, out var modules)
            ? modules
            : Array.Empty<NavigationItem>();

    public void SelectSection(AppSection section)
    {
        if (_selectedSection == section)
        {
            return;
        }

        _selectedSection = section;
        var module = CurrentModules.FirstOrDefault();
        if (module is not null)
        {
            _selectedModuleKey = module.ModuleKey;
        }

        Raise(nameof(SelectedSection));
        Raise(nameof(CurrentModules));
        Raise(nameof(SelectedModuleKey));
    }

    public void SelectModule(string moduleKey)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
        {
            return;
        }

        var normalized = moduleKey.Trim().ToLowerInvariant();
        if (string.Equals(_selectedModuleKey, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var candidate = CurrentModules.FirstOrDefault(item =>
            string.Equals(item.ModuleKey, normalized, StringComparison.OrdinalIgnoreCase));
        if (candidate is null)
        {
            return;
        }

        _selectedModuleKey = candidate.ModuleKey;
        Raise(nameof(SelectedModuleKey));
    }

    private void Raise(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
