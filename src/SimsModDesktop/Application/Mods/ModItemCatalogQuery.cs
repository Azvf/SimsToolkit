namespace SimsModDesktop.Application.Mods;

public sealed class ModItemCatalogQuery
{
    public string ModsRoot { get; init; } = string.Empty;
    public string SearchQuery { get; init; } = string.Empty;
    public string EntityKindFilter { get; init; } = "All";
    public string SubTypeFilter { get; init; } = "All";
    public string SortBy { get; init; } = "Last Indexed";
    public int PageIndex { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}
