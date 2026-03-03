
namespace SimsModDesktop.Tests;

public sealed class TrayThumbnailCacheStoreTests
{
    [Fact]
    public async Task StoreAsync_ThenTryGetValidEntry_HitsCacheForSameFingerprint()
    {
        using var cacheDir = new TempDirectory();
        var store = new TrayThumbnailCacheStore(cacheDir.Path);
        var item = CreateItem(cacheDir.Path, "item-1", "fp-1");

        var stored = await store.StoreAsync(
            item,
            ImageTestHelpers.CreatePngBytes(2, 2),
            TrayThumbnailSourceKind.Embedded);

        Assert.NotNull(stored);
        Assert.True(store.TryGetValidEntry(item, out var cacheEntry));
        Assert.Equal(stored!.CacheFilePath, cacheEntry.CacheFilePath);
        Assert.True(File.Exists(cacheEntry.CacheFilePath));
        Assert.True(TrayImageCodec.TryMeasure(File.ReadAllBytes(cacheEntry.CacheFilePath), out var width, out var height));
        Assert.Equal(768, width);
        Assert.Equal(576, height);
        Assert.True(File.Exists(Path.Combine(cacheDir.Path, "app-cache.db")));
        Assert.False(File.Exists(Path.Combine(cacheDir.Path, "manifest.json")));
    }

    [Fact]
    public async Task CleanupStaleEntriesAsync_RemovesDeletedKeysAndOrphanFiles()
    {
        using var cacheDir = new TempDirectory();
        var store = new TrayThumbnailCacheStore(cacheDir.Path);
        var stale = CreateItem(cacheDir.Path, "item-stale", "fp-stale");
        var live = CreateItem(cacheDir.Path, "item-live", "fp-live");

        var staleEntry = await store.StoreAsync(
            stale,
            ImageTestHelpers.CreatePngBytes(2, 2),
            TrayThumbnailSourceKind.Embedded);
        await store.StoreAsync(
            live,
            ImageTestHelpers.CreatePngBytes(2, 2),
            TrayThumbnailSourceKind.Embedded);

        Assert.NotNull(staleEntry);
        Assert.True(File.Exists(staleEntry!.CacheFilePath));

        await store.CleanupStaleEntriesAsync(stale.TrayRootPath, [live.TrayItemKey]);

        Assert.False(File.Exists(staleEntry.CacheFilePath));
        Assert.False(store.TryGetValidEntry(stale, out _));
        Assert.True(store.TryGetValidEntry(live, out _));
    }

    [Fact]
    public async Task ResetMemoryCache_WithTrayRoot_RemovesLoadedEntries()
    {
        using var cacheDir = new TempDirectory();
        var store = new TrayThumbnailCacheStore(cacheDir.Path);
        var item = CreateItem(cacheDir.Path, "item-1", "fp-1");

        var stored = await store.StoreAsync(
            item,
            ImageTestHelpers.CreatePngBytes(2, 2),
            TrayThumbnailSourceKind.Embedded);

        Assert.NotNull(stored);
        Assert.True(store.TryGetValidEntry(item, out _));

        store.ResetMemoryCache(item.TrayRootPath);

        Assert.False(store.TryGetValidEntry(item, out _));
    }

    [Fact]
    public async Task StoreAsync_PersistsMultipleEntriesForFreshStoreReload()
    {
        using var cacheDir = new TempDirectory();
        var writer = new TrayThumbnailCacheStore(cacheDir.Path);
        var first = CreateItem(cacheDir.Path, "item-1", "fp-1");
        var second = CreateItem(cacheDir.Path, "item-2", "fp-2");

        await writer.StoreAsync(
            first,
            ImageTestHelpers.CreatePngBytes(2, 2),
            TrayThumbnailSourceKind.Embedded);
        await writer.StoreAsync(
            second,
            ImageTestHelpers.CreatePngBytes(2, 2),
            TrayThumbnailSourceKind.Localthumbcache);

        var reloaded = new TrayThumbnailCacheStore(cacheDir.Path);

        Assert.True(reloaded.TryGetValidEntry(first, out var firstEntry));
        Assert.True(reloaded.TryGetValidEntry(second, out var secondEntry));
        Assert.NotEqual(firstEntry.CacheFilePath, secondEntry.CacheFilePath);
        Assert.True(File.Exists(firstEntry.CacheFilePath));
        Assert.True(File.Exists(secondEntry.CacheFilePath));
    }

    private static SimsTrayPreviewItem CreateItem(string trayRootPath, string key, string fingerprint)
    {
        return new SimsTrayPreviewItem
        {
            TrayItemKey = key,
            PresetType = "Lot",
            TrayRootPath = trayRootPath,
            TrayInstanceId = "0x0000000000000001",
            ContentFingerprint = fingerprint
        };
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tray-cache-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
