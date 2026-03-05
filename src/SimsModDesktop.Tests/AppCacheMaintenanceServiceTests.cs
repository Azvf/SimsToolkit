
using Microsoft.Data.Sqlite;

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

    [Fact]
    public async Task MaintainAsync_Light_MaintainsExistingDatabases()
    {
        using var cacheDir = new TempDirectory("cache-maint-light");
        var appDbPath = Path.Combine(cacheDir.Path, "app-cache.db");
        var packageDbPath = Path.Combine(cacheDir.Path, "TrayDependencyPackageIndex", "cache.db");
        SeedSqliteDatabaseWithWal(appDbPath);
        SeedSqliteDatabaseWithWal(packageDbPath);

        var service = new AppCacheMaintenanceService(cacheDir.Path);

        var result = await service.MaintainAsync();

        Assert.True(result.Success);
        Assert.Equal(AppCacheMaintenanceMode.Light, result.MaintenanceMode);
        Assert.Equal(2, result.MaintainedDatabaseCount);
        Assert.Equal(2, result.DatabaseDetails.Count);
        Assert.All(result.DatabaseDetails, detail => Assert.True(detail.Success));
        Assert.Contains(result.DatabaseDetails, detail => detail.DatabasePath.EndsWith("app-cache.db", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.DatabaseDetails, detail => detail.DatabasePath.EndsWith(Path.Combine("TrayDependencyPackageIndex", "cache.db"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MaintainAsync_WhenDatabaseFilesMissing_ReturnsSuccessWithSkippedDetails()
    {
        using var cacheDir = new TempDirectory("cache-maint-empty");
        var service = new AppCacheMaintenanceService(cacheDir.Path);

        var result = await service.MaintainAsync(AppCacheMaintenanceMode.Deep);

        Assert.True(result.Success);
        Assert.Equal(AppCacheMaintenanceMode.Deep, result.MaintenanceMode);
        Assert.Equal(2, result.MaintainedDatabaseCount);
        Assert.Equal(2, result.DatabaseDetails.Count);
        Assert.All(result.DatabaseDetails, detail =>
        {
            Assert.True(detail.Success);
            Assert.Contains("skipped", detail.Message, StringComparison.OrdinalIgnoreCase);
        });
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

    private static void SeedSqliteDatabaseWithWal(string dbPath)
    {
        var directory = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS test_data (id INTEGER PRIMARY KEY, value TEXT NOT NULL);
            DELETE FROM test_data;
            INSERT INTO test_data(value)
            VALUES ('a'),('b'),('c'),('d'),('e');
            """;
        command.ExecuteNonQuery();
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
