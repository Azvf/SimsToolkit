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

        var embeddedImage = TryExtractEmbeddedImage(item, cancellationToken);
        var localthumbcacheImage = embeddedImage is null
            ? _localthumbcacheThumbnailReader.TryExtractBestImage(
                item.TrayRootPath,
                item.TrayInstanceId,
                cancellationToken)
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

    private ExtractedTrayImage? TryExtractEmbeddedImage(
        SimsTrayPreviewItem item,
        CancellationToken cancellationToken)
    {
        var trayItemImage = _embeddedImageExtractor.TryExtractBestImage(item, cancellationToken);
        if (trayItemImage is not null)
        {
            return trayItemImage;
        }

        foreach (var candidatePath in OrderSourcePaths(item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = _embeddedImageExtractor.TryExtractBestImage(candidatePath, cancellationToken);
            if (image is not null)
            {
                return image;
            }
        }

        return null;
    }

    private static IEnumerable<string> OrderSourcePaths(SimsTrayPreviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.SourceFilePaths.Count == 0)
        {
            return Array.Empty<string>();
        }

        var rank = item.PresetType switch
        {
            "Lot" => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [".bpi"] = 0,
                [".blueprint"] = 1,
                [".trayitem"] = 2
            },
            "Room" => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [".rmi"] = 0,
                [".room"] = 1,
                [".trayitem"] = 2
            },
            "Household" => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [".hhi"] = 0,
                [".sgi"] = 1,
                [".trayitem"] = 2
            },
            _ => new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [".bpi"] = 0,
                [".rmi"] = 1,
                [".hhi"] = 2,
                [".sgi"] = 3,
                [".trayitem"] = 4
            }
        };

        return item.SourceFilePaths
            .OrderBy(path =>
            {
                var extension = Path.GetExtension(path);
                return rank.TryGetValue(extension, out var value)
                    ? value
                    : 99;
            })
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);
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

        if (embeddedImage is null)
        {
            sourceKind = TrayThumbnailSourceKind.Localthumbcache;
            return localthumbcacheImage;
        }

        if (localthumbcacheImage is null)
        {
            sourceKind = TrayThumbnailSourceKind.Embedded;
            return embeddedImage;
        }

        if (localthumbcacheImage.PixelArea > embeddedImage.PixelArea)
        {
            sourceKind = TrayThumbnailSourceKind.Localthumbcache;
            return localthumbcacheImage;
        }

        sourceKind = TrayThumbnailSourceKind.Embedded;
        return embeddedImage;
    }
}
