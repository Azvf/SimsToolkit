using SimsModDesktop.Application.TrayPreview;

namespace SimsModDesktop.Application.Models;

public sealed class SimsTrayPreviewRequest
{
    public required PreviewSourceRef PreviewSource { get; init; }
    public int PageSize { get; init; } = 50;
    public int? PageBuildWorkerCount { get; init; }
    public string PresetTypeFilter { get; init; } = "All";
    public string BuildSizeFilter { get; init; } = "All";
    public string HouseholdSizeFilter { get; init; } = "All";
    public string AuthorFilter { get; init; } = string.Empty;
    public string TimeFilter { get; init; } = "All";
    public string SearchQuery { get; init; } = string.Empty;
}
