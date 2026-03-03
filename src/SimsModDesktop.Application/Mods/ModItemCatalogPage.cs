namespace SimsModDesktop.Application.Mods;

public sealed class ModItemCatalogPage
{
    public required IReadOnlyList<ModItemListRow> Items { get; init; }
    public required int TotalItems { get; init; }
    public required int PageIndex { get; init; }
    public required int PageSize { get; init; }
    public required int TotalPages { get; init; }
}
