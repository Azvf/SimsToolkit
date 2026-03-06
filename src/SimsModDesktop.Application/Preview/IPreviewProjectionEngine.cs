using SimsModDesktop.Application.Models;

namespace SimsModDesktop.Application.Preview;

public interface IPreviewProjectionEngine
{
    IReadOnlyList<SimsTrayPreviewItem> ApplyFilters(
        SimsTrayPreviewRequest request,
        IReadOnlyList<SimsTrayPreviewItem> items,
        CancellationToken cancellationToken = default);
}
