using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public interface ITrayThumbnailService
{
    Task<TrayThumbnailResult> GetThumbnailAsync(
        SimsTrayPreviewItem item,
        CancellationToken cancellationToken = default);

    Task CleanupStaleEntriesAsync(
        string trayRootPath,
        IReadOnlyCollection<string> liveItemKeys,
        CancellationToken cancellationToken = default);
}

internal sealed class NullTrayThumbnailService : ITrayThumbnailService
{
    public static NullTrayThumbnailService Instance { get; } = new();

    private NullTrayThumbnailService()
    {
    }

    public Task<TrayThumbnailResult> GetThumbnailAsync(
        SimsTrayPreviewItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        return Task.FromResult(new TrayThumbnailResult
        {
            SourceKind = TrayThumbnailSourceKind.Placeholder,
            Success = false
        });
    }

    public Task CleanupStaleEntriesAsync(
        string trayRootPath,
        IReadOnlyCollection<string> liveItemKeys,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
