
namespace SimsModDesktop.Application.TrayPreview;

public interface ITrayThumbnailService
{
    Task<TrayThumbnailResult> GetThumbnailAsync(
        SimsTrayPreviewItem item,
        CancellationToken cancellationToken = default);

    Task CleanupStaleEntriesAsync(
        string trayRootPath,
        IReadOnlyCollection<string> liveItemKeys,
        CancellationToken cancellationToken = default);

    void ResetMemoryCache(string? trayRootPath = null);
}
