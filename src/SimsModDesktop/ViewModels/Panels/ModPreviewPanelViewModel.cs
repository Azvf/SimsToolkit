using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Panels;

public sealed class ModPreviewPanelViewModel : ObservableObject
{
    private string _modsRoot = string.Empty;
    private string _packageTypeFilter = "All";
    private string _scopeFilter = "All";
    private string _sortBy = "Last Updated";
    private string _searchQuery = string.Empty;
    private bool _showOverridesOnly;

    public string ModsRoot
    {
        get => _modsRoot;
        set => SetProperty(ref _modsRoot, value);
    }

    public string PackageTypeFilter
    {
        get => _packageTypeFilter;
        set => SetProperty(ref _packageTypeFilter, value);
    }

    public string ScopeFilter
    {
        get => _scopeFilter;
        set => SetProperty(ref _scopeFilter, value);
    }

    public string SortBy
    {
        get => _sortBy;
        set => SetProperty(ref _sortBy, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public bool ShowOverridesOnly
    {
        get => _showOverridesOnly;
        set => SetProperty(ref _showOverridesOnly, value);
    }
}
