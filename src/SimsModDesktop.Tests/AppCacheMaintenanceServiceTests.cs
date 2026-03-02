using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class AppCacheMaintenanceServiceTests
{
    [Fact]
    public async Task ClearAsync_RemovesUnifiedCacheAndDerivedFolders_LeavesPackageIndex()
    {
        using var cacheDir = new TempDirectory("cache-maint");
        SeedCommonCacheLayout(cacheDir.Path);
        SeedPackageIndexCache(cacheDir.Path);
        var service = new AppCacheMaintenanceService(cacheDir.Path);

        var result = await service.ClearAsync();

        Assert.True(result.Success);
        Assert.False(File.Exists(Path.Combine(cacheDir.Path, "app-cache.db")));
        Assert.False(Directory.Exists(Path.Combine(cacheDir.Path, "SavePreview")));
        Assert.False(Directory.Exists(Path.Combine(cacheDir.Path, "TrayPreviewThumbnails", "thumbs")));
        Assert.True(File.Exists(Path.Combine(cacheDir.Path, "TrayDependencyPackageIndex", "cache.db")));
    }

    [Fact]
    public async Task ClearAllAsync_RemovesPackageIndexToo()
    {
        using var cacheDir = new TempDirectory("cache-maint-all");
        SeedCommonCacheLayout(cacheDir.Path);
        SeedPackageIndexCache(cacheDir.Path);
        var service = new AppCacheMaintenanceService(cacheDir.Path);

        var result = await service.ClearAllAsync();

        Assert.True(result.Success);
        Assert.False(File.Exists(Path.Combine(cacheDir.Path, "app-cache.db")));
        Assert.False(File.Exists(Path.Combine(cacheDir.Path, "TrayDependencyPackageIndex", "cache.db")));
    }

    private static void SeedCommonCacheLayout(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "SavePreview", "hash"));
        Directory.CreateDirectory(Path.Combine(root, "TrayPreviewThumbnails", "thumbs", "abc"));
        File.WriteAllText(Path.Combine(root, "app-cache.db"), "db");
        File.WriteAllText(Path.Combine(root, "app-cache.db-wal"), "wal");
        File.WriteAllText(Path.Combine(root, "app-cache.db-shm"), "shm");
        File.WriteAllText(Path.Combine(root, "SavePreview", "hash", "file.txt"), "cache");
        File.WriteAllText(Path.Combine(root, "TrayPreviewThumbnails", "thumbs", "abc", "file.png"), "png");
    }

    private static void SeedPackageIndexCache(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "TrayDependencyPackageIndex"));
        File.WriteAllText(Path.Combine(root, "TrayDependencyPackageIndex", "cache.db"), "pkg");
        File.WriteAllText(Path.Combine(root, "TrayDependencyPackageIndex", "cache.db-wal"), "pkg");
        File.WriteAllText(Path.Combine(root, "TrayDependencyPackageIndex", "cache.db-shm"), "pkg");
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
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
