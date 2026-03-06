using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.TrayPreview;

namespace SimsModDesktop.Application.Preview;

public interface IPreviewQueryService
{
    bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result);

    Task<TrayPreviewLoadResult> LoadAsync(TrayPreviewInput input, CancellationToken cancellationToken = default);

    Task<TrayPreviewPageResult> LoadPageAsync(
        TrayPreviewInput input,
        int requestedPageIndex,
        CancellationToken cancellationToken = default);

    void Invalidate(PreviewSourceRef? source = null);

    void Reset();
}
