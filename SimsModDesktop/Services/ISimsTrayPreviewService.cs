using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public interface ISimsTrayPreviewService
{
    Task<SimsTrayPreviewDashboard> BuildDashboardAsync(
        SimsTrayPreviewRequest request,
        CancellationToken cancellationToken = default);

    Task<SimsTrayPreviewPage> BuildPageAsync(
        SimsTrayPreviewRequest request,
        int pageIndex,
        CancellationToken cancellationToken = default);
}
