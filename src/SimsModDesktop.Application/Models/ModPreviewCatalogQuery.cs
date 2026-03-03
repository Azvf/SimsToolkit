namespace SimsModDesktop.Application.Models;

public sealed record ModPreviewCatalogQuery
{
    public required string ModsRoot { get; init; }
    public string PackageTypeFilter { get; init; } = "All";
    public string ScopeFilter { get; init; } = "All";
    public string SortBy { get; init; } = "Last Updated";
    public string SearchQuery { get; init; } = string.Empty;
    public bool ShowOverridesOnly { get; init; }
    public bool BypassCache { get; init; }
}
