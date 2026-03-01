using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class TrayThumbnailService : ITrayThumbnailService
{
    private readonly TrayThumbnailCacheStore _cacheStore;
    private readonly TrayEmbeddedImageExtractor _embeddedImageExtractor;
    private readonly LocalthumbcacheThumbnailReader _localthumbcacheThumbnailReader;

    public TrayThumbnailService()
        : this(
            new TrayThumbnailCacheStore(),
            new TrayEmbeddedImageExtractor(),
            new LocalthumbcacheThumbnailReader())
    {
    }

    public TrayThumbnailService(
        TrayThumbnailCacheStore cacheStore,
        TrayEmbeddedImageExtractor embeddedImageExtractor,
        LocalthumbcacheThumbnailReader localthumbcacheThumbnailReader)
    {
        _cacheStore = cacheStore;
        _embeddedImageExtractor = embeddedImageExtractor;
        _localthumbcacheThumbnailReader = localthumbcacheThumbnailReader;
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

        var localthumbcacheImage = _localthumbcacheThumbnailReader.TryExtractBestImage(
            item.TrayRootPath,
            item.TrayInstanceId,
            cancellationToken);
        var embeddedImage = localthumbcacheImage is null
            ? _embeddedImageExtractor.TryExtractBestImage(item, cancellationToken)
            : null;

        var sourceKind = TrayThumbnailSourceKind.Placeholder;
        var selectedImage = SelectBestImage(embeddedImage, localthumbcacheImage, ref sourceKind);
        if (selectedImage is null)
        {
            return new TrayThumbnailResult
            {
                SourceKind = TrayThumbnailSourceKind.Placeholder,
                Success = false
            };
        }

        var stored = await _cacheStore.StoreAsync(
            item,
            selectedImage.Data,
            sourceKind,
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
            SourceKind = sourceKind,
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

    private static ExtractedTrayImage? SelectBestImage(
        ExtractedTrayImage? embeddedImage,
        ExtractedTrayImage? localthumbcacheImage,
        ref TrayThumbnailSourceKind sourceKind)
    {
        if (embeddedImage is null && localthumbcacheImage is null)
        {
            sourceKind = TrayThumbnailSourceKind.Placeholder;
            return null;
        }

        if (localthumbcacheImage is null)
        {
            sourceKind = TrayThumbnailSourceKind.Embedded;
            return embeddedImage;
        }

        if (embeddedImage is null)
        {
            sourceKind = TrayThumbnailSourceKind.Localthumbcache;
            return localthumbcacheImage;
        }

        // Prefer the game's own localthumbcache preview when available.
        sourceKind = TrayThumbnailSourceKind.Localthumbcache;
        return localthumbcacheImage;
    }
}
