using SimsModDesktop.Models;

namespace SimsModDesktop.Application.TrayPreview;

public sealed class TrayPreviewPageResult
{
    public required SimsTrayPreviewPage Page { get; init; }
    public int LoadedPageCount { get; init; }
    public bool FromCache { get; init; }
}
