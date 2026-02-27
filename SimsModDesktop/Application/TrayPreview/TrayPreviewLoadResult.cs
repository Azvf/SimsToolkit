using SimsModDesktop.Models;

namespace SimsModDesktop.Application.TrayPreview;

public sealed class TrayPreviewLoadResult
{
    public required SimsTrayPreviewDashboard Dashboard { get; init; }
    public required SimsTrayPreviewPage Page { get; init; }
    public int LoadedPageCount { get; init; }
}
