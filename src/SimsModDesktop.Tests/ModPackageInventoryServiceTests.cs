using SimsModDesktop.Application.Mods;
using SimsModDesktop.Infrastructure.Persistence;
using SimsModDesktop.Infrastructure.ServiceRegistration;

namespace SimsModDesktop.Tests;

public sealed class ModPackageInventoryServiceTests
{
    [Fact]
    public async Task RefreshAsync_RevalidatingUnchangedRoot_DoesNotRewriteExistingRows()
    {
        using var cacheDir = new TempDirectory("inventory-cache");
        using var modsDir = new TempDirectory("inventory-mods");
        var packagePath = CreatePackage(modsDir.Path, "alpha.package", [1, 2, 3], new DateTime(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc));
        var service = CreateService(cacheDir.Path);

        await service.RefreshAsync(modsDir.Path);
        var initialEntry = GetEntry(cacheDir.Path, modsDir.Path, packagePath);
        Assert.NotNull(initialEntry);

        await Task.Delay(20);
        var secondResult = await service.RefreshAsync(modsDir.Path);
        var revalidatedEntry = GetEntry(cacheDir.Path, modsDir.Path, packagePath);

        Assert.NotNull(revalidatedEntry);
        Assert.Equal(initialEntry!.InventoryVersion, revalidatedEntry!.InventoryVersion);
        Assert.Equal(initialEntry.InventoryVersion, secondResult.Snapshot.InventoryVersion);
        Assert.Empty(secondResult.AddedEntries);
        Assert.Empty(secondResult.ChangedEntries);
        Assert.Empty(secondResult.RemovedPackagePaths);
        Assert.Equal(1, CountEntries(cacheDir.Path, modsDir.Path));
    }

    [Fact]
    public async Task RefreshAsync_WhenPackageChanges_UpdatesRowInPlace()
    {
        using var cacheDir = new TempDirectory("inventory-cache");
        using var modsDir = new TempDirectory("inventory-mods");
        var packagePath = CreatePackage(modsDir.Path, "alpha.package", [1, 2, 3], new DateTime(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc));
        var service = CreateService(cacheDir.Path);

        await service.RefreshAsync(modsDir.Path);
        var initialEntry = GetEntry(cacheDir.Path, modsDir.Path, packagePath);
        Assert.NotNull(initialEntry);

        CreatePackage(modsDir.Path, "alpha.package", [1, 2, 3, 4, 5], new DateTime(2026, 3, 4, 10, 5, 0, DateTimeKind.Utc));
        await Task.Delay(20);

        var refreshResult = await service.RefreshAsync(modsDir.Path);
        var updatedEntry = GetEntry(cacheDir.Path, modsDir.Path, packagePath);

        Assert.NotNull(updatedEntry);
        Assert.Single(refreshResult.ChangedEntries);
        Assert.Equal(packagePath, refreshResult.ChangedEntries[0].PackagePath);
        Assert.Equal(1, CountEntries(cacheDir.Path, modsDir.Path));
        Assert.NotEqual(initialEntry!.InventoryVersion, updatedEntry!.InventoryVersion);
        Assert.NotEqual(initialEntry.InventoryVersion, refreshResult.Snapshot.InventoryVersion);
        Assert.Equal(5, updatedEntry.FileLength);
        Assert.Equal($"{updatedEntry.FileLength}:{updatedEntry.LastWriteUtcTicks}", updatedEntry.PackageFingerprintKey);
    }

    [Fact]
    public async Task RefreshAsync_WhenPackageRemoved_DeletesOnlyStaleRows()
    {
        using var cacheDir = new TempDirectory("inventory-cache");
        using var modsDir = new TempDirectory("inventory-mods");
        var alphaPath = CreatePackage(modsDir.Path, "alpha.package", [1, 2, 3], new DateTime(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc));
        var betaPath = CreatePackage(modsDir.Path, "beta.package", [9, 8, 7], new DateTime(2026, 3, 4, 10, 1, 0, DateTimeKind.Utc));
        var service = CreateService(cacheDir.Path);

        await service.RefreshAsync(modsDir.Path);
        var betaEntry = GetEntry(cacheDir.Path, modsDir.Path, betaPath);
        Assert.NotNull(betaEntry);

        File.Delete(alphaPath);
        await Task.Delay(20);

        var refreshResult = await service.RefreshAsync(modsDir.Path);
        var preservedBeta = GetEntry(cacheDir.Path, modsDir.Path, betaPath);

        Assert.Single(refreshResult.RemovedPackagePaths);
        Assert.Equal(alphaPath, refreshResult.RemovedPackagePaths[0]);
        Assert.NotNull(preservedBeta);
        Assert.Equal(betaEntry!.InventoryVersion, preservedBeta!.InventoryVersion);
        Assert.Equal(1, CountEntries(cacheDir.Path, modsDir.Path));
        Assert.Null(GetEntry(cacheDir.Path, modsDir.Path, alphaPath));
    }

    [Fact]
    public async Task RefreshAsync_WithEmptyModsRoot_ReturnsValidSnapshot()
    {
        using var cacheDir = new TempDirectory("inventory-cache");
        using var modsDir = new TempDirectory("inventory-mods");
        var service = CreateService(cacheDir.Path);

        var result = await service.RefreshAsync(modsDir.Path);
        var rootRow = GetRoot(cacheDir.Path, modsDir.Path);

        Assert.Empty(result.Snapshot.Entries);
        Assert.Empty(result.AddedEntries);
        Assert.Empty(result.ChangedEntries);
        Assert.Empty(result.RemovedPackagePaths);
        Assert.NotNull(rootRow);
        Assert.Equal(0, rootRow!.PackageCount);
        Assert.Equal("Ready", rootRow.Status);
        Assert.Equal(0, CountEntries(cacheDir.Path, modsDir.Path));
    }

    [Fact]
    public async Task RefreshAsync_WithEmptyModsRoot_ProducesStableInventoryVersion()
    {
        using var cacheDir = new TempDirectory("inventory-cache");
        using var modsDir = new TempDirectory("inventory-mods");
        var service = CreateService(cacheDir.Path);

        var first = await service.RefreshAsync(modsDir.Path);
        await Task.Delay(20);
        var second = await service.RefreshAsync(modsDir.Path);

        Assert.Equal(first.Snapshot.InventoryVersion, second.Snapshot.InventoryVersion);
    }

    [Fact]
    public async Task RefreshAsync_CancelledDuringScan_DoesNotThrowSynchronously_AndDoesNotPersistPartialRows()
    {
        using var cacheDir = new TempDirectory("inventory-cache");
        using var modsDir = new TempDirectory("inventory-mods");
        var service = CreateService(cacheDir.Path);
        using var cts = new CancellationTokenSource();

        for (var index = 0; index < 200; index++)
        {
            CreatePackage(
                modsDir.Path,
                $"pkg-{index:D4}.package",
                [1, 2, 3, 4],
                new DateTime(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc).AddSeconds(index));
        }

        var progress = new Progress<ModPackageInventoryRefreshProgress>(value =>
        {
            if (string.Equals(value.Stage, "scan", StringComparison.Ordinal) && value.Current >= 1)
            {
                cts.Cancel();
            }
        });

        Task<ModPackageInventoryRefreshResult>? refreshTask = null;
        Exception? invocationException = null;
        try
        {
            refreshTask = service.RefreshAsync(modsDir.Path, progress, cts.Token);
        }
        catch (Exception ex)
        {
            invocationException = ex;
        }

        Assert.Null(invocationException);
        Assert.NotNull(refreshTask);
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await refreshTask!);
        Assert.Equal(0, CountEntries(cacheDir.Path, modsDir.Path));
        Assert.Null(GetRoot(cacheDir.Path, modsDir.Path));
    }

    private static IModPackageInventoryService CreateService(string cacheRootPath)
    {
        var type = typeof(InfrastructureServiceRegistration).Assembly.GetType("SimsModDesktop.Infrastructure.Mods.SqliteModPackageInventoryService");
        Assert.NotNull(type);

        var instance = Activator.CreateInstance(
            type!,
            bindingAttr: System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            args: [cacheRootPath],
            culture: null);
        Assert.NotNull(instance);

        return Assert.IsAssignableFrom<IModPackageInventoryService>(instance);
    }

    private static string CreatePackage(string rootPath, string fileName, byte[] bytes, DateTime lastWriteUtc)
    {
        var path = Path.Combine(rootPath, fileName);
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private static InventoryEntryRow? GetEntry(string cacheRootPath, string modsRootPath, string packagePath)
    {
        using var connection = new AppCacheDatabase(cacheRootPath).OpenConnection();
        if (!TableExists(connection, "ModPackageInventoryEntries"))
        {
            return null;
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT PackagePath, FileLength, LastWriteUtcTicks, InventoryVersion, PackageFingerprintKey
            FROM ModPackageInventoryEntries
            WHERE ModsRootPath = $modsRootPath
              AND PackagePath = $packagePath;
            """;
        command.Parameters.AddWithValue("$modsRootPath", Path.GetFullPath(modsRootPath));
        command.Parameters.AddWithValue("$packagePath", Path.GetFullPath(packagePath));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new InventoryEntryRow
        {
            PackagePath = reader.GetString(0),
            FileLength = reader.GetInt64(1),
            LastWriteUtcTicks = reader.GetInt64(2),
            InventoryVersion = reader.GetInt64(3),
            PackageFingerprintKey = reader.GetString(4)
        };
    }

    private static InventoryRootRow? GetRoot(string cacheRootPath, string modsRootPath)
    {
        using var connection = new AppCacheDatabase(cacheRootPath).OpenConnection();
        if (!TableExists(connection, "ModPackageInventoryRoots"))
        {
            return null;
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT PackageCount, Status
            FROM ModPackageInventoryRoots
            WHERE ModsRootPath = $modsRootPath;
            """;
        command.Parameters.AddWithValue("$modsRootPath", Path.GetFullPath(modsRootPath));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new InventoryRootRow
        {
            PackageCount = reader.GetInt32(0),
            Status = reader.GetString(1)
        };
    }

    private static int CountEntries(string cacheRootPath, string modsRootPath)
    {
        using var connection = new AppCacheDatabase(cacheRootPath).OpenConnection();
        if (!TableExists(connection, "ModPackageInventoryEntries"))
        {
            return 0;
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM ModPackageInventoryEntries
            WHERE ModsRootPath = $modsRootPath;
            """;
        command.Parameters.AddWithValue("$modsRootPath", Path.GetFullPath(modsRootPath));
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static bool TableExists(System.Data.IDbConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name = $tableName;
            """;
        var tableNameParameter = command.CreateParameter();
        tableNameParameter.ParameterName = "$tableName";
        tableNameParameter.Value = tableName;
        _ = command.Parameters.Add(tableNameParameter);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private sealed class InventoryEntryRow
    {
        public string PackagePath { get; init; } = string.Empty;
        public long FileLength { get; init; }
        public long LastWriteUtcTicks { get; init; }
        public long InventoryVersion { get; init; }
        public string PackageFingerprintKey { get; init; } = string.Empty;
    }

    private sealed class InventoryRootRow
    {
        public int PackageCount { get; init; }
        public string Status { get; init; } = string.Empty;
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
