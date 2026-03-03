using SimsModDesktop.Application.Requests;
using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Preview;

public class TrayLikePreviewFilterViewModel : ObservableObject
{
    private string _presetTypeFilter = "All";
    private string _buildSizeFilter = "All";
    private string _householdSizeFilter = "All";
    private string _authorFilter = string.Empty;
    private string _timeFilter = "All";
    private string _searchQuery = string.Empty;
    private string _layoutMode = "Entry";
    private bool _enableDebugPreview;
    private bool _showPresetTypeFilter = true;
    private bool _showBuildSizeFilter;
    private bool _showHouseholdSizeFilter = true;
    private bool _showAuthorFilter = true;
    private bool _showTimeFilter = true;
    private bool _showLayoutMode = true;
    private bool _showDebugPreview = true;
    private string _searchWatermark = "Search";
    private int _pageSize = 50;

    public IReadOnlyList<string> PresetTypeFilterOptions => ["All", "Lot", "Room", "Household"];
    public IReadOnlyList<string> BuildSizeFilterOptions => ["All", "15 x 20", "20 x 20", "30 x 20", "30 x 30", "40 x 30", "40 x 40", "50 x 40", "50 x 50", "64 x 64"];
    public IReadOnlyList<string> HouseholdSizeFilterOptions => ["All", "1", "2", "3", "4", "5", "6", "7", "8"];
    public IReadOnlyList<string> TimeFilterOptions => ["All", "Last24h", "Last7d", "Last30d", "Last90d"];
    public IReadOnlyList<string> LayoutModeOptions => ["Entry", "Grid"];

    public string PresetTypeFilter
    {
        get => _presetTypeFilter;
        set => SetProperty(ref _presetTypeFilter, NormalizeFilter(value));
    }

    public string BuildSizeFilter
    {
        get => _buildSizeFilter;
        set => SetProperty(ref _buildSizeFilter, NormalizeFilter(value));
    }

    public string HouseholdSizeFilter
    {
        get => _householdSizeFilter;
        set => SetProperty(ref _householdSizeFilter, NormalizeFilter(value));
    }

    public string AuthorFilter
    {
        get => _authorFilter;
        set => SetProperty(ref _authorFilter, value ?? string.Empty);
    }

    public string TimeFilter
    {
        get => _timeFilter;
        set => SetProperty(ref _timeFilter, NormalizeFilter(value));
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value ?? string.Empty);
    }

    public string LayoutMode
    {
        get => _layoutMode;
        set => SetProperty(ref _layoutMode, NormalizeLayoutMode(value));
    }

    public bool EnableDebugPreview
    {
        get => _enableDebugPreview;
        set => SetProperty(ref _enableDebugPreview, value);
    }

    public bool ShowPresetTypeFilter
    {
        get => _showPresetTypeFilter;
        protected set => SetProperty(ref _showPresetTypeFilter, value);
    }

    public bool ShowBuildSizeFilter
    {
        get => _showBuildSizeFilter;
        protected set => SetProperty(ref _showBuildSizeFilter, value);
    }

    public bool ShowHouseholdSizeFilter
    {
        get => _showHouseholdSizeFilter;
        protected set => SetProperty(ref _showHouseholdSizeFilter, value);
    }

    public bool ShowAuthorFilter
    {
        get => _showAuthorFilter;
        protected set => SetProperty(ref _showAuthorFilter, value);
    }

    public bool ShowTimeFilter
    {
        get => _showTimeFilter;
        protected set => SetProperty(ref _showTimeFilter, value);
    }

    public bool ShowLayoutMode
    {
        get => _showLayoutMode;
        protected set => SetProperty(ref _showLayoutMode, value);
    }

    public bool ShowDebugPreview
    {
        get => _showDebugPreview;
        protected set => SetProperty(ref _showDebugPreview, value);
    }

    public string SearchWatermark
    {
        get => _searchWatermark;
        protected set => SetProperty(ref _searchWatermark, string.IsNullOrWhiteSpace(value) ? "Search" : value);
    }

    public int PageSize
    {
        get => _pageSize;
        protected set => SetProperty(ref _pageSize, Math.Max(1, value));
    }

    public virtual TrayPreviewInput BuildInput(string trayPath)
    {
        return new TrayPreviewInput
        {
            TrayPath = trayPath,
            PageSize = PageSize,
            PresetTypeFilter = NormalizeFilter(PresetTypeFilter),
            BuildSizeFilter = NormalizeFilter(BuildSizeFilter),
            HouseholdSizeFilter = NormalizeFilter(HouseholdSizeFilter),
            AuthorFilter = AuthorFilter?.Trim() ?? string.Empty,
            TimeFilter = NormalizeFilter(TimeFilter),
            SearchQuery = SearchQuery?.Trim() ?? string.Empty
        };
    }

    protected static string NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "All"
            : value.Trim();
    }

    protected static string NormalizeLayoutMode(string? value)
    {
        return string.Equals(value, "Grid", StringComparison.OrdinalIgnoreCase)
            ? "Grid"
            : "Entry";
    }
}
