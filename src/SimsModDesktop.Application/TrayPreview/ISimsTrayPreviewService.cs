using SimsModDesktop.Models;

namespace SimsModDesktop.Application.TrayPreview;

public interface ISimsTrayPreviewService
{
    Task<SimsTrayPreviewSummary> BuildSummaryAsync(
        SimsTrayPreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<SimsTrayPreviewPage> BuildPageAsync(
        SimsTrayPreviewRequest request,
        int pageIndex,
        CancellationToken cancellationToken = default);

    void Invalidate(string? trayRootPath = null);
}

