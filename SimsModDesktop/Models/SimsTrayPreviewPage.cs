namespace SimsModDesktop.Models;

public sealed class SimsTrayPreviewPage
{
    public int PageIndex { get; init; }
    public int PageSize { get; init; }
    public int TotalItems { get; init; }
    public int TotalPages { get; init; }
    public IReadOnlyList<SimsTrayPreviewItem> Items { get; init; } = Array.Empty<SimsTrayPreviewItem>();
}
