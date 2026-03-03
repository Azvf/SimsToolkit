
namespace SimsModDesktop.Tests;

public sealed class TrayThumbnailServiceTests
{
    [Fact]
    public async Task GetThumbnailAsync_UsesEmbeddedImageOnly()
    {
        using var fixture = new TempTrayFixture();
        var trayItemPath = Path.Combine(fixture.TrayPath, "item-1.trayitem");
        var embeddedPath = Path.Combine(fixture.TrayPath, "item-1.sgi");

        File.WriteAllBytes(trayItemPath, [0x01, 0x02, 0x03, 0x04]);
        File.WriteAllBytes(
            embeddedPath,
            ImageTestHelpers.CreateEncodedTrayImageBytes(
                ImageTestHelpers.CreateJpegBytes(3, 2)));

        var item = new SimsTrayPreviewItem
        {
            TrayItemKey = "item-1",
            PresetType = "Household",
            TrayRootPath = fixture.TrayPath,
            TrayInstanceId = "0x0000000000000042",
            ContentFingerprint = "fp-item-1",
            SourceFilePaths = [trayItemPath, embeddedPath]
        };
        var cacheStore = new TrayThumbnailCacheStore(fixture.CacheRootPath);
        var service = new TrayThumbnailService(
            cacheStore,
            new TrayEmbeddedImageExtractor());

        var result = await service.GetThumbnailAsync(item);

        Assert.True(result.Success);
        Assert.Equal(TrayThumbnailSourceKind.Embedded, result.SourceKind);
        Assert.True(cacheStore.TryGetValidEntry(item, out var cacheEntry));
        Assert.Equal(TrayThumbnailSourceKind.Embedded, cacheEntry.SourceKind);
        Assert.Equal(3, cacheEntry.Width);
        Assert.Equal(2, cacheEntry.Height);
    }

    private sealed class TempTrayFixture : IDisposable
    {
        public TempTrayFixture()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"tray-thumb-service-{Guid.NewGuid():N}");
            CacheRootPath = Path.Combine(RootPath, "Cache");
            TrayPath = Path.Combine(RootPath, "Tray");
            Directory.CreateDirectory(CacheRootPath);
            Directory.CreateDirectory(TrayPath);
        }

        public string RootPath { get; }
        public string CacheRootPath { get; }
        public string TrayPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
