using SimsModDesktop.Application.Modules;
using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Panels;

public sealed class TrayPreviewPanelViewModel : ObservableObject, ITrayPreviewModuleState
{
    private string _trayRoot = string.Empty;
    private string _presetTypeFilter = "All";
    private string _buildSizeFilter = "All";
    private string _householdSizeFilter = "All";
    private string _authorFilter = string.Empty;
    private string _timeFilter = "All";
    private string _searchQuery = string.Empty;
    private string _layoutMode = "Entry";
    private bool _enableDebugPreview;

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

    public string BuildSizeFilter
    {
        get => _buildSizeFilter;
        set => SetProperty(ref _buildSizeFilter, value);
    }

    public string HouseholdSizeFilter
    {
        get => _householdSizeFilter;
        set => SetProperty(ref _householdSizeFilter, value);
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

    public string LayoutMode
    {
        get => _layoutMode;
        set => SetProperty(ref _layoutMode, value);
    }

    public bool EnableDebugPreview
    {
        get => _enableDebugPreview;
        set => SetProperty(ref _enableDebugPreview, value);
    }
}
