using SimsModDesktop.Application.Modules;
using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Panels;

public sealed class TrayPreviewPanelViewModel : ObservableObject, ITrayPreviewModuleState
{
    private string _trayRoot = string.Empty;
    private string _presetTypeFilter = "All";
    private string _authorFilter = string.Empty;
    private string _timeFilter = "All";
    private string _searchQuery = string.Empty;

    public string TrayRoot
    {
        get => _trayRoot;
        set => SetProperty(ref _trayRoot, value);
    }

    public string PresetTypeFilter
    {
        get => _presetTypeFilter;
        set => SetProperty(ref _presetTypeFilter, value);
    }

    public string AuthorFilter
    {
        get => _authorFilter;
        set => SetProperty(ref _authorFilter, value);
    }

    public string TimeFilter
    {
        get => _timeFilter;
        set => SetProperty(ref _timeFilter, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }
}
