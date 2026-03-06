using SimsModDesktop.Application.Models;
using SimsModDesktop.Application.TrayPreview;

namespace SimsModDesktop.Application.Preview;

public interface IPreviewSourceReader
{
    bool CanRead(PreviewSourceRef source);

    IReadOnlyList<SimsTrayPreviewItem> ReadItems(SimsTrayPreviewRequest request, CancellationToken cancellationToken = default);
}
