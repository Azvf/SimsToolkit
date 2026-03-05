using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Presentation.Diagnostics;

namespace SimsModDesktop.Presentation.Services;

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
    private readonly ILogger<NavigationService> _logger;

    public event PropertyChangedEventHandler? PropertyChanged;

    public NavigationService(ILogger<NavigationService>? logger = null)
    {
        _logger = logger ?? NullLogger<NavigationService>.Instance;
    }

    public AppSection SelectedSection => _selectedSection;
    public IReadOnlyList<NavigationItem> SectionItems => AllItems;

    public void SelectSection(AppSection section)
    {
        if (_selectedSection == section)
        {
            _logger.LogDebug(
                "{Event} status={Status} domain={Domain} fromSection={FromSection} toSection={ToSection} reason={Reason}",
                LogEvents.UiPageSwitchBlocked,
                "blocked",
                "navigation",
                _selectedSection,
                section,
                "already-selected");
            return;
        }

        var previous = _selectedSection;
        _selectedSection = section;
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} fromSection={FromSection} toSection={ToSection}",
            LogEvents.UiPageSwitchDone,
            "done",
            "navigation",
            previous,
            section);
        Raise(nameof(SelectedSection));
    }

    private void Raise(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
