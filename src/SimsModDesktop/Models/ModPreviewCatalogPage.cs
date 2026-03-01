namespace SimsModDesktop.Models;

public sealed record ModPreviewCatalogPage
{
    public required IReadOnlyList<ModPreviewCatalogItem> Items { get; init; }
    public bool ReplaceExisting { get; init; }
    public bool IsFinal { get; init; }
    public int ScannedCount { get; init; }
    public int MatchedCount { get; init; }
    public int PackageCount { get; init; }
    public int ScriptCount { get; init; }
}
