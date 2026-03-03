
namespace SimsModDesktop.Infrastructure.Tray;

public sealed class TrayThumbnailService : ITrayThumbnailService
{
    private readonly TrayThumbnailCacheStore _cacheStore;
    private readonly TrayEmbeddedImageExtractor _embeddedImageExtractor;

    public TrayThumbnailService()
        : this(
            new TrayThumbnailCacheStore(),
            new TrayEmbeddedImageExtractor())
    {
    }

    public TrayThumbnailService(
        TrayThumbnailCacheStore cacheStore,
        TrayEmbeddedImageExtractor embeddedImageExtractor)
    {
        _cacheStore = cacheStore;
        _embeddedImageExtractor = embeddedImageExtractor;
    }

    public async Task<TrayThumbnailResult> GetThumbnailAsync(
        SimsTrayPreviewItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_cacheStore.TryGetValidEntry(item, out var cached))
        {
            return new TrayThumbnailResult
            {
                CacheFilePath = cached.CacheFilePath,
                FromCache = true,
                SourceKind = cached.SourceKind,
                Success = true
            };
        }

        cancellationToken.ThrowIfCancellationRequested();

        var embeddedImage = _embeddedImageExtractor.TryExtractBestImage(item, cancellationToken);
        if (embeddedImage is null)
        {
            return new TrayThumbnailResult
            {
                SourceKind = TrayThumbnailSourceKind.Placeholder,
                Success = false
            };
        }

        var stored = await _cacheStore.StoreAsync(
            item,
            embeddedImage.Data,
            TrayThumbnailSourceKind.Embedded,
            cancellationToken);

        if (stored is null)
        {
            return new TrayThumbnailResult
            {
                SourceKind = TrayThumbnailSourceKind.Placeholder,
                Success = false
            };
        }

        return new TrayThumbnailResult
        {
            CacheFilePath = stored.CacheFilePath,
            FromCache = false,
            SourceKind = TrayThumbnailSourceKind.Embedded,
            Success = true
        };
    }

    public Task CleanupStaleEntriesAsync(
        string trayRootPath,
        IReadOnlyCollection<string> liveItemKeys,
        CancellationToken cancellationToken = default)
    {
        return _cacheStore.CleanupStaleEntriesAsync(trayRootPath, liveItemKeys, cancellationToken);
    }

    public void ResetMemoryCache(string? trayRootPath = null)
    {
        _cacheStore.ResetMemoryCache(trayRootPath);
    }
}
