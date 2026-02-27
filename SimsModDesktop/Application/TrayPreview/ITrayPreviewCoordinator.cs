using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.TrayPreview;

public interface ITrayPreviewCoordinator
{
    bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result);
    Task<TrayPreviewLoadResult> LoadAsync(TrayPreviewInput input, CancellationToken cancellationToken = default);
    Task<TrayPreviewPageResult> LoadPageAsync(int requestedPageIndex, CancellationToken cancellationToken = default);
    void Reset();
}
