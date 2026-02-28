namespace SimsModDesktop.Models;

public sealed class SimsTrayPreviewRequest
{
    public required string TrayPath { get; init; }
    public int PageSize { get; init; } = 50;
    public string PresetTypeFilter { get; init; } = "All";
    public string AuthorFilter { get; init; } = string.Empty;
    public string TimeFilter { get; init; } = "All";
    public string SearchQuery { get; init; } = string.Empty;
}
