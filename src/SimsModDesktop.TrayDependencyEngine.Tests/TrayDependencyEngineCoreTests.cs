using System.Buffers.Binary;
using System.Reflection;
using Microsoft.Data.Sqlite;
using SimsModDesktop.PackageCore;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.TrayDependencyEngine.Tests;

public sealed class TrayDependencyEngineCoreTests
{
    private const ulong FirstInstance = 0x1111111111111111;
    private const ulong SecondInstance = 0x2222222222222222;
    private const ulong HighBitInstance = 0xF123456789ABCDEF;
    private const uint SupportedResourceType = 55242443u;
    private const uint ObjectCatalogResourceType = 832458525u;
    private const uint ObjectDefinitionResourceType = 3235601127u;
    private const uint ImageResourceType = 877907861u;

    [Fact]
    public async Task PackageIndexCache_ReusesSnapshot_WhenPackagesAreUnchanged()
    {
        using var fixture = new TempDirectory("tray-cache");
        var packagePath = Path.Combine(fixture.Path, "sample.package");
        WritePackage(packagePath, [new PackageSpec(SupportedResourceType, 0, FirstInstance, [1, 2, 3, 4])]);

        var cache = new PackageIndexCache();
        var first = await BuildSnapshotAsync(cache, fixture.Path);
        var second = await cache.TryLoadSnapshotAsync(fixture.Path, first.InventoryVersion);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task PackageIndexCache_SmallChange_ReusesUnchangedPackageRows()
    {
        using var modsRoot = new TempDirectory("tray-smallchange-mods");
        using var cacheRoot = new TempDirectory("tray-smallchange-cache");

        var changedPackagePath = Path.Combine(modsRoot.Path, "changed.package");
        var untouchedPackagePath = Path.Combine(modsRoot.Path, "untouched.package");
        WritePackage(changedPackagePath, [new PackageSpec(SupportedResourceType, 0, FirstInstance, [1, 2, 3, 4])]);
        WritePackage(untouchedPackagePath, [new PackageSpec(SupportedResourceType, 0, SecondInstance, [5, 6, 7, 8])]);

        var cache = new PackageIndexCache(cacheRoot.Path);
        var firstRequest = CreateBuildRequest(modsRoot.Path);
        _ = await cache.BuildSnapshotAsync(firstRequest);
        var firstTicks = await LoadPackageUpdatedTicksAsync(Path.Combine(cacheRoot.Path, "cache.db"));

        await Task.Delay(20);
        WritePackage(changedPackagePath, [new PackageSpec(SupportedResourceType, 0, FirstInstance, [9, 10, 11, 12])]);
        File.SetLastWriteTimeUtc(changedPackagePath, DateTime.UtcNow.AddSeconds(1));

        var secondRequest = CreateBuildRequest(modsRoot.Path);
        var changedBuildFile = secondRequest.PackageFiles.Single(file =>
            string.Equals(Path.GetFullPath(file.FilePath), Path.GetFullPath(changedPackagePath), StringComparison.OrdinalIgnoreCase));
        _ = await cache.BuildSnapshotAsync(secondRequest with
        {
            ChangedPackageFiles = [changedBuildFile]
        });
        var secondTicks = await LoadPackageUpdatedTicksAsync(Path.Combine(cacheRoot.Path, "cache.db"));
        var manifestCount = await LoadManifestPackageCountAsync(Path.Combine(cacheRoot.Path, "cache.db"), modsRoot.Path);
        var staleSnapshotInSameCache = await cache.TryLoadSnapshotAsync(modsRoot.Path, firstRequest.InventoryVersion);
        var staleSnapshot = await new PackageIndexCache(cacheRoot.Path).TryLoadSnapshotAsync(modsRoot.Path, firstRequest.InventoryVersion);

        Assert.True(secondTicks.TryGetValue(Path.GetFullPath(changedPackagePath), out var changedUpdated));
        Assert.True(firstTicks.TryGetValue(Path.GetFullPath(changedPackagePath), out var changedBefore));
        Assert.True(changedUpdated > changedBefore);

        Assert.True(secondTicks.TryGetValue(Path.GetFullPath(untouchedPackagePath), out var untouchedUpdated));
        Assert.True(firstTicks.TryGetValue(Path.GetFullPath(untouchedPackagePath), out var untouchedBefore));
        Assert.Equal(untouchedBefore, untouchedUpdated);
        Assert.Equal(2L, manifestCount);
        Assert.Null(staleSnapshotInSameCache);
        Assert.Null(staleSnapshot);

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task PackageIndexCache_RemovedPackagePaths_ExcludesPackageFromManifest()
    {
        using var modsRoot = new TempDirectory("tray-removed-mods");
        using var cacheRoot = new TempDirectory("tray-removed-cache");

        var keepPath = Path.Combine(modsRoot.Path, "keep.package");
        var removePath = Path.Combine(modsRoot.Path, "remove.package");
        WritePackage(keepPath, [new PackageSpec(SupportedResourceType, 0, FirstInstance, [1, 2, 3, 4])]);
        WritePackage(removePath, [new PackageSpec(SupportedResourceType, 0, SecondInstance, [5, 6, 7, 8])]);

        var cache = new PackageIndexCache(cacheRoot.Path);
        var buildRequest = CreateBuildRequest(modsRoot.Path);
        var snapshot = await cache.BuildSnapshotAsync(buildRequest with
        {
            RemovedPackagePaths = [removePath]
        });

        Assert.Single(snapshot.Packages);
        Assert.Equal(Path.GetFullPath(keepPath), snapshot.Packages[0].FilePath);

        var reloaded = await new PackageIndexCache(cacheRoot.Path).TryLoadSnapshotAsync(modsRoot.Path, buildRequest.InventoryVersion);
        Assert.NotNull(reloaded);
        Assert.Single(reloaded!.Packages);
        Assert.Equal(Path.GetFullPath(keepPath), reloaded.Packages[0].FilePath);

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task PackageIndexCache_TryLoadSnapshot_IgnoresLegacyPackageIndexSnapshotsTable()
    {
        using var modsRoot = new TempDirectory("tray-legacy-mods");
        using var cacheRoot = new TempDirectory("tray-legacy-cache");
        var cacheDbPath = Path.Combine(cacheRoot.Path, "cache.db");

        await using (var connection = new SqliteConnection($"Data Source={cacheDbPath};Mode=ReadWriteCreate;Pooling=False;"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS PackageIndexSnapshots(
                    ModsRootPath TEXT NOT NULL,
                    InventoryVersion INTEGER NOT NULL,
                    SnapshotJson TEXT NOT NULL
                );
                INSERT INTO PackageIndexSnapshots(ModsRootPath, InventoryVersion, SnapshotJson)
                VALUES (@ModsRootPath, 1, 'not-json');
                """;
            command.Parameters.AddWithValue("@ModsRootPath", Path.GetFullPath(modsRoot.Path));
            await command.ExecuteNonQueryAsync();
        }

        var cache = new PackageIndexCache(cacheRoot.Path);
        var loaded = await cache.TryLoadSnapshotAsync(modsRoot.Path, 1);

        Assert.Null(loaded);

        await using var verifyConnection = new SqliteConnection($"Data Source={cacheDbPath};Mode=ReadWrite;Pooling=False;");
        await verifyConnection.OpenAsync();
        await using var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = 'PackageIndexSnapshots';";
        var legacyTableCount = Convert.ToInt64(await verifyCommand.ExecuteScalarAsync());
        Assert.Equal(0L, legacyTableCount);

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task PackageIndexCache_CancelledBuild_PersistsCheckpointRowsForResume()
    {
        using var modsRoot = new TempDirectory("tray-cancel-mods");
        using var cacheRoot = new TempDirectory("tray-cancel-cache");
        for (var i = 0; i < 600; i++)
        {
            var packagePath = Path.Combine(modsRoot.Path, $"sample-{i:D4}.package");
            WritePackage(packagePath, [new PackageSpec(SupportedResourceType, 0, FirstInstance + (ulong)i, [1, 2, 3, 4])]);
        }

        var cache = new PackageIndexCache(cacheRoot.Path);
        var request = CreateBuildRequest(modsRoot.Path) with
        {
            WriteBatchSize = 8,
            ParseWorkerCount = 4,
            CommitBatchSize = 8,
            CommitIntervalMs = 100
        };
        using var cancellation = new CancellationTokenSource();
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await cache.BuildSnapshotAsync(
                request,
                new Progress<TrayDependencyExportProgress>(progress =>
                {
                    if (progress.Percent >= 70)
                    {
                        cancellation.Cancel();
                    }
                }),
                cancellation.Token));

        var dbPath = Path.Combine(cacheRoot.Path, "cache.db");
        var checkpointRows = await CountAllCachePackageRowsAsync(dbPath);
        Assert.True(checkpointRows > 0);

        var resumed = await cache.BuildSnapshotAsync(request);
        Assert.Equal(request.PackageFiles.Count, resumed.Packages.Count);
        var reloaded = await cache.TryLoadSnapshotAsync(modsRoot.Path, request.InventoryVersion);
        Assert.NotNull(reloaded);
        Assert.Equal(request.PackageFiles.Count, reloaded!.Packages.Count);

        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task PackageIndexCache_TryLoadSnapshot_MigratesAliasRootToCanonicalRoot()
    {
        using var modsRoot = new TempDirectory("tray-alias-mods");
        using var cacheRoot = new TempDirectory("tray-alias-cache");
        var aliasRoot = Path.Combine(modsRoot.Path, "alias-root");
        Directory.CreateDirectory(aliasRoot);

        var packagePath = Path.Combine(modsRoot.Path, "sample.package");
        WritePackage(packagePath, [new PackageSpec(SupportedResourceType, 0, FirstInstance, [1, 2, 3, 4])]);

        var legacyResolver = new FakePathIdentityResolver();
        var modernResolver = new FakePathIdentityResolver(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [aliasRoot] = modsRoot.Path
        });

        var legacyCache = new PackageIndexCache(cacheRoot.Path, pathIdentityResolver: legacyResolver);
        var buildRequest = CreateBuildRequest(modsRoot.Path) with { ModsRootPath = aliasRoot };
        _ = await legacyCache.BuildSnapshotAsync(buildRequest);

        var modernCache = new PackageIndexCache(cacheRoot.Path, pathIdentityResolver: modernResolver);
        var loaded = await modernCache.TryLoadSnapshotAsync(aliasRoot, buildRequest.InventoryVersion);

        Assert.NotNull(loaded);
        Assert.Equal(modsRoot.Path, loaded!.ModsRootPath, StringComparer.OrdinalIgnoreCase);

        var dbPath = Path.Combine(cacheRoot.Path, "cache.db");
        await using var verifyConnection = new SqliteConnection($"Data Source={dbPath};Mode=ReadWrite;Pooling=False;");
        await verifyConnection.OpenAsync();
        await using var canonicalCountCommand = verifyConnection.CreateCommand();
        canonicalCountCommand.CommandText = "SELECT COUNT(1) FROM TrayRootManifest WHERE ModsRootPath = @ModsRootPath;";
        canonicalCountCommand.Parameters.AddWithValue("@ModsRootPath", modsRoot.Path);
        var canonicalCount = Convert.ToInt64(await canonicalCountCommand.ExecuteScalarAsync());
        Assert.Equal(1L, canonicalCount);

        await using var aliasCountCommand = verifyConnection.CreateCommand();
        aliasCountCommand.CommandText = "SELECT COUNT(1) FROM TrayRootManifest WHERE ModsRootPath = @ModsRootPath;";
        aliasCountCommand.Parameters.AddWithValue("@ModsRootPath", aliasRoot);
        var aliasCount = Convert.ToInt64(await aliasCountCommand.ExecuteScalarAsync());
        Assert.Equal(0L, aliasCount);
    }

    [Fact]
    public async Task TrayDependencyExportService_ReusesSingleReadSession_PerPackageDuringExpansion()
    {
        using var trayRoot = new TempDirectory("tray-session-tray");
        using var modsRoot = new TempDirectory("tray-session-mods");
        using var exportRoot = new TempDirectory("tray-session-out");

        var trayItemPath = Path.Combine(trayRoot.Path, "0xsession.trayitem");
        WriteTrayItem(trayItemPath, FirstInstance, SecondInstance);

        WritePackage(
            Path.Combine(modsRoot.Path, "dual.package"),
            [
                new PackageSpec(SupportedResourceType, 0, FirstInstance, [1, 2, 3, 4]),
                new PackageSpec(SupportedResourceType, 0, SecondInstance, [5, 6, 7, 8])
            ]);

        var packageIndexCache = new PackageIndexCache();
        var preloadedSnapshot = await BuildSnapshotAsync(packageIndexCache, modsRoot.Path);
        var countingReader = new CountingResourceReader();
        var service = new TrayDependencyExportService(packageIndexCache, countingReader);
        var request = new TrayDependencyExportRequest
        {
            ItemTitle = "Session",
            TrayItemKey = "0xsession",
            TrayRootPath = trayRoot.Path,
            TraySourceFiles = [trayItemPath],
            ModsRootPath = modsRoot.Path,
            TrayExportRoot = Path.Combine(exportRoot.Path, "Session_0xsession", "Tray"),
            ModsExportRoot = Path.Combine(exportRoot.Path, "Session_0xsession", "Mods"),
            PreloadedSnapshot = preloadedSnapshot,
            RequireGraphFastPath = false
        };

        var result = await service.ExportAsync(request);

        Assert.True(result.Success);
        Assert.Equal(1, countingReader.OpenCount);
        Assert.Equal(1, result.CopiedModFileCount);
    }

    [Fact]
    public async Task TrayDependencyExportService_MatchesEntries_WhenInstanceUsesHighBit()
    {
        using var trayRoot = new TempDirectory("tray-highbit-tray");
        using var modsRoot = new TempDirectory("tray-highbit-mods");
        using var exportRoot = new TempDirectory("tray-highbit-out");

        var trayItemPath = Path.Combine(trayRoot.Path, "0xhighbit.trayitem");
        WriteTrayItem(trayItemPath, HighBitInstance);

        WritePackage(
            Path.Combine(modsRoot.Path, "highbit.package"),
            [new PackageSpec(SupportedResourceType, 0, HighBitInstance, [7, 8, 9, 10])]);

        var packageIndexCache = new PackageIndexCache();
        var preloadedSnapshot = await BuildSnapshotAsync(packageIndexCache, modsRoot.Path);
        var service = new TrayDependencyExportService(packageIndexCache);
        var request = new TrayDependencyExportRequest
        {
            ItemTitle = "HighBit",
            TrayItemKey = "0xhighbit",
            TrayRootPath = trayRoot.Path,
            TraySourceFiles = [trayItemPath],
            ModsRootPath = modsRoot.Path,
            TrayExportRoot = Path.Combine(exportRoot.Path, "HighBit_0xhighbit", "Tray"),
            ModsExportRoot = Path.Combine(exportRoot.Path, "HighBit_0xhighbit", "Mods"),
            PreloadedSnapshot = preloadedSnapshot
        };

        var result = await service.ExportAsync(request);

        Assert.True(result.Success);
        Assert.Equal(1, result.CopiedModFileCount);
    }

    [Fact]
    public async Task TrayDependencyExportService_ExpandsObjectCatalogSiblingStructuredDependencies()
    {
        using var trayRoot = new TempDirectory("tray-objectcatalog-tray");
        using var modsRoot = new TempDirectory("tray-objectcatalog-mods");
        using var exportRoot = new TempDirectory("tray-objectcatalog-out");
        const ulong objectInstance = 0x3000000000000001;
        const ulong nestedImageInstance = 0x3000000000000002;
        const uint group = 7;

        var trayItemPath = Path.Combine(trayRoot.Path, "0xobjcatalog.trayitem");
        WriteTrayItem(trayItemPath, objectInstance);

        WritePackage(
            Path.Combine(modsRoot.Path, "objectcatalog.package"),
            [new PackageSpec(ObjectCatalogResourceType, group, objectInstance, [0x00])]);
        WritePackage(
            Path.Combine(modsRoot.Path, "objectdefinition.package"),
            [new PackageSpec(ObjectDefinitionResourceType, group, objectInstance, BuildResourceKeyBytes(ImageResourceType, group, nestedImageInstance))]);
        WritePackage(
            Path.Combine(modsRoot.Path, "image.package"),
            [new PackageSpec(ImageResourceType, group, nestedImageInstance, [4, 3, 2, 1])]);

        var packageIndexCache = new PackageIndexCache();
        var preloadedSnapshot = await BuildSnapshotAsync(packageIndexCache, modsRoot.Path);
        var service = new TrayDependencyExportService(packageIndexCache);
        var request = new TrayDependencyExportRequest
        {
            ItemTitle = "ObjectCatalog",
            TrayItemKey = "0xobjcatalog",
            TrayRootPath = trayRoot.Path,
            TraySourceFiles = [trayItemPath],
            ModsRootPath = modsRoot.Path,
            TrayExportRoot = Path.Combine(exportRoot.Path, "ObjectCatalog_0xobjcatalog", "Tray"),
            ModsExportRoot = Path.Combine(exportRoot.Path, "ObjectCatalog_0xobjcatalog", "Mods"),
            PreloadedSnapshot = preloadedSnapshot
        };

        var result = await service.ExportAsync(request);

        Assert.True(result.Success);
        Assert.Equal(3, result.CopiedModFileCount);
        Assert.True(File.Exists(Path.Combine(request.ModsExportRoot, "objectcatalog.package")));
        Assert.True(File.Exists(Path.Combine(request.ModsExportRoot, "objectdefinition.package")));
        Assert.True(File.Exists(Path.Combine(request.ModsExportRoot, "image.package")));
    }

    [Fact]
    public async Task PackageIndexCache_CleanupOrphans_RemovesRowsOutsideCurrentRootCandidates()
    {
        using var orphanRoot = new TempDirectory("tray-orphan-old-root");
        using var activeRoot = new TempDirectory("tray-orphan-active-root");
        using var cacheRoot = new TempDirectory("tray-orphan-cache");
        var orphanPath = Path.Combine(orphanRoot.Path, "orphan.package");
        var activePath = Path.Combine(activeRoot.Path, "active.package");
        WritePackage(orphanPath, [new PackageSpec(SupportedResourceType, 0, FirstInstance, [1, 2, 3, 4])]);
        WritePackage(activePath, [new PackageSpec(SupportedResourceType, 0, SecondInstance, [5, 6, 7, 8])]);

        var cache = new PackageIndexCache(cacheRoot.Path);
        _ = await BuildSnapshotAsync(cache, orphanRoot.Path);
        var cacheDbPath = Path.Combine(cacheRoot.Path, "cache.db");
        await RemoveRootManifestRowsAsync(cacheDbPath, orphanRoot.Path);
        Assert.Equal(1L, await CountCachePackageRowsByPathAsync(cacheDbPath, orphanPath));

        _ = await cache.BuildSnapshotAsync(CreateBuildRequest(activeRoot.Path) with
        {
            ForceFullOrphanCleanup = true
        });

        Assert.Equal(0L, await CountCachePackageRowsByPathAsync(cacheDbPath, orphanPath));
        Assert.Equal(1L, await CountCachePackageRowsByPathAsync(cacheDbPath, activePath));
        Assert.Equal(1L, await LoadCacheEntryRowCountAsync(cacheDbPath));
    }

    [Fact]
    public async Task PackageIndexCache_IncrementalCleanup_SkipsGlobalSweep_WhenNoDelta()
    {
        using var orphanRoot = new TempDirectory("tray-incremental-orphan-root");
        using var activeRoot = new TempDirectory("tray-incremental-active-root");
        using var cacheRoot = new TempDirectory("tray-incremental-cache");
        var orphanPath = Path.Combine(orphanRoot.Path, "orphan.package");
        var activePath = Path.Combine(activeRoot.Path, "active.package");
        WritePackage(orphanPath, [new PackageSpec(SupportedResourceType, 0, FirstInstance, [1, 2, 3, 4])]);
        WritePackage(activePath, [new PackageSpec(SupportedResourceType, 0, SecondInstance, [5, 6, 7, 8])]);

        var cache = new PackageIndexCache(cacheRoot.Path);
        _ = await BuildSnapshotAsync(cache, orphanRoot.Path);
        var cacheDbPath = Path.Combine(cacheRoot.Path, "cache.db");
        await RemoveRootManifestRowsAsync(cacheDbPath, orphanRoot.Path);
        Assert.Equal(1L, await CountCachePackageRowsByPathAsync(cacheDbPath, orphanPath));

        _ = await BuildSnapshotAsync(cache, activeRoot.Path);

        Assert.Equal(1L, await CountCachePackageRowsByPathAsync(cacheDbPath, orphanPath));
        Assert.Equal(1L, await CountCachePackageRowsByPathAsync(cacheDbPath, activePath));

        _ = await cache.BuildSnapshotAsync(CreateBuildRequest(activeRoot.Path) with
        {
            ForceFullOrphanCleanup = true
        });

        Assert.Equal(0L, await CountCachePackageRowsByPathAsync(cacheDbPath, orphanPath));
        Assert.Equal(1L, await CountCachePackageRowsByPathAsync(cacheDbPath, activePath));
    }

    [Fact]
    public async Task TrayDependencyExportService_LoadsCompanionTrayFilesByTrayKey()
    {
        using var trayRoot = new TempDirectory("tray-companion-tray");
        using var modsRoot = new TempDirectory("tray-companion-mods");
        using var exportRoot = new TempDirectory("tray-companion-out");

        var trayItemPath = Path.Combine(trayRoot.Path, "0x123abc.trayitem");
        File.WriteAllBytes(trayItemPath, [0x00, 0x01, 0x02, 0x03]);

        var hhiPath = Path.Combine(trayRoot.Path, "0x00000000!0x123abc.hhi");
        WriteTrayItem(hhiPath, FirstInstance);

        WritePackage(
            Path.Combine(modsRoot.Path, "companion.package"),
            [new PackageSpec(SupportedResourceType, 0, FirstInstance, [3, 4, 5, 6])]);

        var packageIndexCache = new PackageIndexCache();
        var preloadedSnapshot = await BuildSnapshotAsync(packageIndexCache, modsRoot.Path);
        var service = new TrayDependencyExportService(packageIndexCache);
        var request = new TrayDependencyExportRequest
        {
            ItemTitle = "Companion",
            TrayItemKey = "0x123abc",
            TrayRootPath = trayRoot.Path,
            TraySourceFiles = [trayItemPath],
            ModsRootPath = modsRoot.Path,
            TrayExportRoot = Path.Combine(exportRoot.Path, "Companion_0x123abc", "Tray"),
            ModsExportRoot = Path.Combine(exportRoot.Path, "Companion_0x123abc", "Mods"),
            PreloadedSnapshot = preloadedSnapshot
        };

        var result = await service.ExportAsync(request);

        Assert.True(result.Success);
        Assert.Equal(1, result.CopiedModFileCount);
    }

    [Fact]
    public async Task PackageIndexCache_BatchLookup_FallsBackToSingleQueryWhenBatchFails()
    {
        using var modsRoot = new TempDirectory("tray-batch-fallback-mods");
        var packagePath = Path.Combine(modsRoot.Path, "single.package");
        WritePackage(packagePath, [new PackageSpec(SupportedResourceType, 0, FirstInstance, [1, 2, 3, 4])]);

        var cache = new PackageIndexCache();
        var snapshot = await BuildSnapshotAsync(cache, modsRoot.Path);
        var lookup = GetSnapshotLookup(snapshot);
        var session = OpenLookupSession(lookup);
        try
        {
            var connection = GetSessionConnection(session);
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA query_only = ON;";
            _ = pragma.ExecuteNonQuery();

            var exactKey = new TrayResourceKey(SupportedResourceType, 0, FirstInstance);
            var exactResult = InvokeExactBatch(session, [exactKey]);
            Assert.True(exactResult.TryGetValue(exactKey, out var exactMatches));
            Assert.NotNull(exactMatches);
            Assert.NotEmpty(exactMatches);

            var typeKey = new TypeInstanceKey(SupportedResourceType, FirstInstance);
            var typeResult = InvokeTypeInstanceBatch(session, [typeKey]);
            Assert.True(typeResult.TryGetValue(typeKey, out var typeMatches));
            Assert.NotNull(typeMatches);
            Assert.NotEmpty(typeMatches);

            var supportedResult = InvokeSupportedBatch(session, [FirstInstance]);
            Assert.True(supportedResult.TryGetValue(FirstInstance, out var supported));
            Assert.True(supported);
        }
        finally
        {
            var disposeMethod = session.GetType().GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _ = disposeMethod?.Invoke(session, null);
        }
    }

    [Fact]
    public async Task TrayDependencyExportService_FailsFastWithoutPreloadedSnapshot_AndDoesNotCallCache()
    {
        using var trayRoot = new TempDirectory("tray-nosnapshot-tray");
        using var modsRoot = new TempDirectory("tray-nosnapshot-mods");
        using var exportRoot = new TempDirectory("tray-nosnapshot-out");

        var trayItemPath = Path.Combine(trayRoot.Path, "0x1234.trayitem");
        WriteTrayItem(trayItemPath, FirstInstance);
        WritePackage(
            Path.Combine(modsRoot.Path, "sample.package"),
            [new PackageSpec(SupportedResourceType, 0, FirstInstance, [1, 2, 3, 4])]);

        var cache = new ForbiddenPackageIndexCache();
        var service = new TrayDependencyExportService(cache);
        var request = new TrayDependencyExportRequest
        {
            ItemTitle = "NoSnapshot",
            TrayItemKey = "0x1234",
            TrayRootPath = trayRoot.Path,
            TraySourceFiles = [trayItemPath],
            ModsRootPath = modsRoot.Path,
            TrayExportRoot = Path.Combine(exportRoot.Path, "NoSnapshot_0x1234", "Tray"),
            ModsExportRoot = Path.Combine(exportRoot.Path, "NoSnapshot_0x1234", "Mods")
        };

        var result = await service.ExportAsync(request);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Kind == TrayDependencyIssueKind.CacheBuildError);
        Assert.Equal(0, cache.TryLoadCalls);
        Assert.Equal(0, cache.BuildCalls);
    }

    [Fact]
    public async Task TrayDependencyAnalysisService_FailsFastWithoutPreloadedSnapshot()
    {
        using var trayRoot = new TempDirectory("analysis-nosnapshot-tray");
        using var modsRoot = new TempDirectory("analysis-nosnapshot-mods");

        var trayItemPath = Path.Combine(trayRoot.Path, "0x2222.trayitem");
        WriteTrayItem(trayItemPath, FirstInstance);

        var service = new TrayDependencyAnalysisService();
        var request = new TrayDependencyAnalysisRequest
        {
            TrayPath = trayRoot.Path,
            ModsRootPath = modsRoot.Path,
            TrayItemKey = "0x2222"
        };

        var result = await service.AnalyzeAsync(request);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Kind == TrayDependencyIssueKind.CacheBuildError);
    }

    private static void WriteTrayItem(string path, params ulong[] instances)
    {
        using var stream = File.Create(path);
        Span<byte> buffer = stackalloc byte[8];
        foreach (var instance in instances)
        {
            stream.WriteByte(0x09);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer, instance);
            stream.Write(buffer);
        }
    }

    private static byte[] BuildResourceKeyBytes(uint type, uint group, ulong instance)
    {
        var data = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), type);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), group);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8, 8), instance);
        return data;
    }

    private static void WritePackage(string path, IReadOnlyList<PackageSpec> specs)
    {
        const int headerSize = 96;
        const int flagsSize = 4;
        const int indexEntrySize = 32;
        var indexPosition = headerSize;
        var recordSize = flagsSize + (indexEntrySize * specs.Count);
        var resourcePosition = headerSize + recordSize;

        var header = new byte[headerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 1179664964u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), 2u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(36, 4), (uint)specs.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40, 4), (uint)indexPosition);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44, 4), (uint)recordSize);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(64, 8), (ulong)indexPosition);

        var index = new byte[recordSize];
        BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(0, 4), 0u);
        var nextPosition = resourcePosition;
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var entry = index.AsSpan(flagsSize + (i * indexEntrySize), indexEntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(0, 4), spec.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(4, 4), spec.Group);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(8, 4), (uint)(spec.Instance >> 32));
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(12, 4), (uint)(spec.Instance & 0xFFFFFFFF));
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(16, 4), (uint)nextPosition);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(20, 4), (uint)spec.ResourceData.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(24, 4), (uint)spec.ResourceData.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(28, 2), 0);
            nextPosition += spec.ResourceData.Length;
        }

        using var stream = File.Create(path);
        stream.Write(header);
        stream.Write(index);
        foreach (var spec in specs)
        {
            stream.Write(spec.ResourceData);
        }
    }

    private static PackageIndexBuildRequest CreateBuildRequest(string modsRootPath)
    {
        var normalizedRoot = Path.GetFullPath(modsRootPath.Trim());
        var packageFiles = Directory.EnumerateFiles(normalizedRoot, "*.package", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new PackageIndexBuildFile
                {
                    FilePath = path,
                    Length = info.Length,
                    LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks
                };
            })
            .ToArray();

        return new PackageIndexBuildRequest
        {
            ModsRootPath = normalizedRoot,
            InventoryVersion = ComputeInventoryVersion(packageFiles),
            PackageFiles = packageFiles
        };
    }

    private static async Task<PackageIndexSnapshot> BuildSnapshotAsync(PackageIndexCache cache, string modsRootPath)
    {
        return await cache.BuildSnapshotAsync(CreateBuildRequest(modsRootPath));
    }

    private static async Task<Dictionary<string, long>> LoadPackageUpdatedTicksAsync(string cacheDbPath)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqliteConnection($"Data Source={cacheDbPath};Mode=ReadWrite;Pooling=False;");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PackagePath, UpdatedUtcTicks FROM TrayCachePackage;";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var path = Path.GetFullPath(reader.GetString(0));
            var ticks = reader.GetInt64(1);
            result[path] = ticks;
        }

        return result;
    }

    private static async Task<long> LoadManifestPackageCountAsync(string cacheDbPath, string modsRootPath)
    {
        await using var connection = new SqliteConnection($"Data Source={cacheDbPath};Mode=ReadWrite;Pooling=False;");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM TrayRootManifestPackage WHERE ModsRootPath = @ModsRootPath;";
        command.Parameters.AddWithValue("@ModsRootPath", Path.GetFullPath(modsRootPath));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task RemoveRootManifestRowsAsync(string cacheDbPath, string modsRootPath)
    {
        await using var connection = new SqliteConnection($"Data Source={cacheDbPath};Mode=ReadWrite;Pooling=False;");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM TrayRootManifestPackage WHERE ModsRootPath = @ModsRootPath;
            DELETE FROM TrayRootManifest WHERE ModsRootPath = @ModsRootPath;
            """;
        command.Parameters.AddWithValue("@ModsRootPath", Path.GetFullPath(modsRootPath));
        _ = await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountCachePackageRowsByPathAsync(string cacheDbPath, string packagePath)
    {
        await using var connection = new SqliteConnection($"Data Source={cacheDbPath};Mode=ReadWrite;Pooling=False;");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM TrayCachePackage WHERE PackagePath = @PackagePath;";
        command.Parameters.AddWithValue("@PackagePath", Path.GetFullPath(packagePath));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> CountAllCachePackageRowsAsync(string cacheDbPath)
    {
        await using var connection = new SqliteConnection($"Data Source={cacheDbPath};Mode=ReadWrite;Pooling=False;");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM TrayCachePackage;";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> LoadCacheEntryRowCountAsync(string cacheDbPath)
    {
        await using var connection = new SqliteConnection($"Data Source={cacheDbPath};Mode=ReadWrite;Pooling=False;");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM TrayCacheEntry;";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static long ComputeInventoryVersion(IReadOnlyList<PackageIndexBuildFile> packageFiles)
    {
        unchecked
        {
            long hash = 17;
            for (var i = 0; i < packageFiles.Count; i++)
            {
                var item = packageFiles[i];
                hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(item.FilePath);
                hash = (hash * 31) + item.Length.GetHashCode();
                hash = (hash * 31) + item.LastWriteUtcTicks.GetHashCode();
            }

            hash &= long.MaxValue;
            return hash == 0 ? 1 : hash;
        }
    }

    private sealed record PackageSpec(uint Type, uint Group, ulong Instance, byte[] ResourceData);

    private static object GetSnapshotLookup(PackageIndexSnapshot snapshot)
    {
        var lookupProperty = typeof(PackageIndexSnapshot).GetProperty("Lookup", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(lookupProperty);
        var lookup = lookupProperty!.GetValue(snapshot);
        Assert.NotNull(lookup);
        return lookup!;
    }

    private static object OpenLookupSession(object lookup)
    {
        var openSessionMethod = lookup.GetType().GetMethod("OpenSession", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(openSessionMethod);
        var session = openSessionMethod!.Invoke(lookup, null);
        Assert.NotNull(session);
        return session!;
    }

    private static SqliteConnection GetSessionConnection(object session)
    {
        var connectionField = session.GetType().GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(connectionField);
        var value = connectionField!.GetValue(session);
        Assert.NotNull(value);
        return Assert.IsType<SqliteConnection>(value);
    }

    private static IReadOnlyDictionary<TrayResourceKey, ResolvedResourceRef[]> InvokeExactBatch(object session, IReadOnlyCollection<TrayResourceKey> keys)
    {
        var method = session.GetType().GetMethod("QueryExactBatch", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = method!.Invoke(session, [keys]);
        Assert.NotNull(result);
        return Assert.IsAssignableFrom<IReadOnlyDictionary<TrayResourceKey, ResolvedResourceRef[]>>(result);
    }

    private static IReadOnlyDictionary<TypeInstanceKey, ResolvedResourceRef[]> InvokeTypeInstanceBatch(object session, IReadOnlyCollection<TypeInstanceKey> keys)
    {
        var method = session.GetType().GetMethod("QueryTypeInstanceBatch", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = method!.Invoke(session, [keys]);
        Assert.NotNull(result);
        return Assert.IsAssignableFrom<IReadOnlyDictionary<TypeInstanceKey, ResolvedResourceRef[]>>(result);
    }

    private static IReadOnlyDictionary<ulong, bool> InvokeSupportedBatch(object session, IReadOnlyCollection<ulong> instances)
    {
        var method = session.GetType().GetMethod("QuerySupportedInstanceBatch", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = method!.Invoke(session, [instances]);
        Assert.NotNull(result);
        return Assert.IsAssignableFrom<IReadOnlyDictionary<ulong, bool>>(result);
    }

    private sealed class CountingResourceReader : IDbpfResourceReader
    {
        private readonly DbpfResourceReader _inner = new();

        public int OpenCount { get; private set; }

        public DbpfPackageReadSession OpenSession(string packagePath)
        {
            OpenCount++;
            return _inner.OpenSession(packagePath);
        }
    }

    private sealed class ForbiddenPackageIndexCache : IPackageIndexCache
    {
        public int TryLoadCalls { get; private set; }
        public int BuildCalls { get; private set; }

        public Task<PackageIndexSnapshot?> TryLoadSnapshotAsync(
            string modsRootPath,
            long inventoryVersion,
            CancellationToken cancellationToken = default)
        {
            TryLoadCalls++;
            return Task.FromResult<PackageIndexSnapshot?>(null);
        }

        public Task<PackageIndexSnapshot> BuildSnapshotAsync(
            PackageIndexBuildRequest request,
            IProgress<TrayDependencyExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            BuildCalls++;
            throw new InvalidOperationException("BuildSnapshotAsync should not be called in strict snapshot mode.");
        }
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

    private sealed class FakePathIdentityResolver : IPathIdentityResolver
    {
        private readonly IReadOnlyDictionary<string, string> _directoryMap;

        public FakePathIdentityResolver(IReadOnlyDictionary<string, string>? directoryMap = null)
        {
            _directoryMap = directoryMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public ResolvedPathInfo ResolveDirectory(string path)
        {
            var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
            var canonicalPath = _directoryMap.TryGetValue(fullPath, out var mapped)
                ? Path.GetFullPath(mapped.Trim().Trim('"'))
                : fullPath;
            return new ResolvedPathInfo
            {
                InputPath = path,
                FullPath = fullPath,
                CanonicalPath = canonicalPath,
                Exists = Directory.Exists(fullPath) || Directory.Exists(canonicalPath),
                IsReparsePoint = false,
                LinkTarget = null
            };
        }

        public ResolvedPathInfo ResolveFile(string path)
        {
            var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
            return new ResolvedPathInfo
            {
                InputPath = path,
                FullPath = fullPath,
                CanonicalPath = fullPath,
                Exists = File.Exists(fullPath),
                IsReparsePoint = false,
                LinkTarget = null
            };
        }

        public bool EqualsDirectory(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            var resolvedLeft = ResolveDirectory(left);
            var resolvedRight = ResolveDirectory(right);
            return string.Equals(resolvedLeft.CanonicalPath, resolvedRight.CanonicalPath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
