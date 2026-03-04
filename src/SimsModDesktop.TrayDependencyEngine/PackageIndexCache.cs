using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.TrayDependencyEngine;

public sealed class PackageIndexCache : IPackageIndexCache
{
    private const int SchemaVersion = 2;
    private static readonly HashSet<uint> StructuredTypes = KnownResourceTypes.StructuredReferenceTypes.ToHashSet();
    private static readonly PackageIndexEntry[] EmptyEntries = Array.Empty<PackageIndexEntry>();
    private static readonly IReadOnlyDictionary<uint, PackageTypeIndex> EmptyTypeIndexes = new Dictionary<uint, PackageTypeIndex>();

    private readonly object _sync = new();
    private readonly Dictionary<string, PackageIndexSnapshot> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly SqliteCacheDatabase _database;
    private readonly ILogger<PackageIndexCache> _logger;
    private readonly string _databasePath;

    public PackageIndexCache(IDbpfPackageCatalog? packageCatalog = null, ILogger<PackageIndexCache>? logger = null)
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimsModDesktop",
                "Cache",
                "TrayDependencyPackageIndex"),
            packageCatalog,
            logger)
    {
    }

    public PackageIndexCache(string cacheRootPath, IDbpfPackageCatalog? packageCatalog = null, ILogger<PackageIndexCache>? logger = null)
    {
        var normalizedCacheRoot = Path.GetFullPath(cacheRootPath.Trim());
        _databasePath = Path.Combine(normalizedCacheRoot, "cache.db");
        _database = new SqliteCacheDatabase(_databasePath);
        _logger = logger ?? NullLogger<PackageIndexCache>.Instance;
    }

    public async Task<PackageIndexSnapshot?> TryLoadSnapshotAsync(
        string modsRootPath,
        long inventoryVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modsRootPath) || inventoryVersion <= 0)
        {
            return null;
        }

        var normalizedRoot = Path.GetFullPath(modsRootPath.Trim());
        var cacheKey = BuildCacheKey(normalizedRoot, inventoryVersion);
        lock (_sync)
        {
            if (_states.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        try
        {
            return await ExecuteWithCorruptionRecoveryAsync(async ct =>
            {
                await using var connection = await _database.OpenConnectionAsync(ct).ConfigureAwait(false);
                await EnsureSchemaAsync(connection, ct).ConfigureAwait(false);
                var snapshot = await LoadSnapshotAsync(connection, normalizedRoot, inventoryVersion, ct).ConfigureAwait(false);
                if (snapshot is null)
                {
                    return null;
                }

                lock (_sync)
                {
                    _states[cacheKey] = snapshot;
                }

                return snapshot;
            }, normalizedRoot, inventoryVersion, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "traycache.snapshot.load-failed modsRoot={ModsRoot} inventoryVersion={InventoryVersion}", normalizedRoot, inventoryVersion);
            return null;
        }
    }

    public async Task<PackageIndexSnapshot> BuildSnapshotAsync(
        PackageIndexBuildRequest request,
        IProgress<TrayDependencyExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ModsRootPath))
        {
            throw new ArgumentException("Mods root path is required.", nameof(request));
        }

        var normalizedRoot = Path.GetFullPath(request.ModsRootPath.Trim());
        var removedSet = request.RemovedPackagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changedSet = request.ChangedPackageFiles
            .Where(file => !string.IsNullOrWhiteSpace(file.FilePath))
            .Select(file => Path.GetFullPath(file.FilePath.Trim()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var files = request.PackageFiles
            .Where(file => !string.IsNullOrWhiteSpace(file.FilePath))
            .Select(file => new BuildFile(Path.GetFullPath(file.FilePath.Trim()), file.Length, file.LastWriteUtcTicks))
            .Where(file => !removedSet.Contains(file.Path))
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Report(progress, 5, "Preparing package cache...");
        var timer = Stopwatch.StartNew();
        _logger.LogInformation(
            "traycache.snapshot.build.start modsRoot={ModsRoot} inventoryVersion={InventoryVersion} packageCount={PackageCount} changedCount={ChangedCount} removedCount={RemovedCount}",
            normalizedRoot,
            request.InventoryVersion,
            files.Length,
            changedSet.Count,
            removedSet.Count);

        var snapshot = await ExecuteWithCorruptionRecoveryAsync(async ct =>
        {
            await using var connection = await _database.OpenConnectionAsync(ct).ConfigureAwait(false);
            await EnsureSchemaAsync(connection, ct).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

            var manifestRows = new List<ManifestRow>(files.Length);
            var hitCount = 0;
            var missCount = 0;
            var failCount = 0;
            var lastHeartbeatMs = 0L;

            for (var i = 0; i < files.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var file = files[i];
                var fingerprint = ComputeFingerprint(file);
                var forceReparse = changedSet.Contains(file.Path);
                var hasCache = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    "SELECT COUNT(1) FROM TrayCachePackage WHERE FingerprintKey = @FingerprintKey;",
                    new { FingerprintKey = fingerprint },
                    transaction,
                    cancellationToken: ct)).ConfigureAwait(false) > 0;

                if (hasCache && !forceReparse)
                {
                    hitCount++;
                }
                else
                {
                    missCount++;
                    var success = await UpsertPackageCacheAsync(connection, transaction, file, fingerprint, ct).ConfigureAwait(false);
                    if (!success)
                    {
                        failCount++;
                    }
                }

                manifestRows.Add(new ManifestRow
                {
                    ModsRootPath = normalizedRoot,
                    InventoryVersion = request.InventoryVersion,
                    PackagePath = file.Path,
                    FingerprintKey = fingerprint,
                    Length = file.Length,
                    LastWriteUtcTicks = file.LastWriteTicks
                });

                var processed = i + 1;
                Report(progress, ProgressScale.Scale(10, 70, processed, files.Length), $"Indexing packages... {processed}/{files.Length}");
                if (timer.ElapsedMilliseconds - lastHeartbeatMs >= 5000)
                {
                    lastHeartbeatMs = timer.ElapsedMilliseconds;
                    _logger.LogInformation(
                        "traycache.stage.heartbeat modsRoot={ModsRoot} inventoryVersion={InventoryVersion} packageCount={PackageCount} changedCount={ChangedCount} removedCount={RemovedCount} hitCount={HitCount} missCount={MissCount} parseFailCount={ParseFailCount} stage={Stage} processed={Processed} elapsedMs={ElapsedMs}",
                        normalizedRoot,
                        request.InventoryVersion,
                        files.Length,
                        changedSet.Count,
                        removedSet.Count,
                        hitCount,
                        missCount,
                        failCount,
                        "package-cache",
                        processed,
                        timer.ElapsedMilliseconds);
                }
            }

            _logger.LogInformation(
                "traycache.packagecache.hit modsRoot={ModsRoot} inventoryVersion={InventoryVersion} packageCount={PackageCount} changedCount={ChangedCount} removedCount={RemovedCount} hitCount={HitCount} missCount={MissCount} elapsedMs={ElapsedMs}",
                normalizedRoot,
                request.InventoryVersion,
                files.Length,
                changedSet.Count,
                removedSet.Count,
                hitCount,
                missCount,
                timer.ElapsedMilliseconds);
            _logger.LogInformation(
                "traycache.packagecache.miss modsRoot={ModsRoot} inventoryVersion={InventoryVersion} packageCount={PackageCount} changedCount={ChangedCount} removedCount={RemovedCount} hitCount={HitCount} missCount={MissCount} parseFailCount={ParseFailCount} elapsedMs={ElapsedMs}",
                normalizedRoot,
                request.InventoryVersion,
                files.Length,
                changedSet.Count,
                removedSet.Count,
                hitCount,
                missCount,
                failCount,
                timer.ElapsedMilliseconds);

            Report(progress, 70, "Rebuilding root manifest...");
            var manifestTiming = Stopwatch.StartNew();
            await RebuildManifestAsync(connection, transaction, normalizedRoot, request.InventoryVersion, manifestRows, ct).ConfigureAwait(false);
            manifestTiming.Stop();
            _logger.LogInformation(
                "traycache.manifest.rebuild modsRoot={ModsRoot} inventoryVersion={InventoryVersion} packageCount={PackageCount} changedCount={ChangedCount} removedCount={RemovedCount} elapsedMs={ElapsedMs}",
                normalizedRoot,
                request.InventoryVersion,
                manifestRows.Count,
                changedSet.Count,
                removedSet.Count,
                manifestTiming.ElapsedMilliseconds);
            Report(progress, 85, "Root manifest rebuilt.");

            Report(progress, 85, "Cleaning orphan package rows...");
            await CleanupOrphansAsync(connection, transaction, ct).ConfigureAwait(false);
            Report(progress, 92, "Orphan package rows cleaned.");

            await transaction.CommitAsync(ct).ConfigureAwait(false);
            Report(progress, 92, "Materializing lightweight snapshot...");
            var materializeTiming = Stopwatch.StartNew();
            var loaded = await LoadSnapshotAsync(connection, normalizedRoot, request.InventoryVersion, ct).ConfigureAwait(false)
                ?? new PackageIndexSnapshot
                {
                    ModsRootPath = normalizedRoot,
                    InventoryVersion = request.InventoryVersion,
                    Packages = Array.Empty<IndexedPackageFile>(),
                    Lookup = new SqliteTrayDependencyLookup(_database, normalizedRoot, request.InventoryVersion)
                };
            materializeTiming.Stop();
            _logger.LogInformation(
                "traycache.snapshot.materialize modsRoot={ModsRoot} inventoryVersion={InventoryVersion} packageCount={PackageCount} changedCount={ChangedCount} removedCount={RemovedCount} elapsedMs={ElapsedMs}",
                normalizedRoot,
                request.InventoryVersion,
                loaded.Packages.Count,
                changedSet.Count,
                removedSet.Count,
                materializeTiming.ElapsedMilliseconds);
            Report(progress, 99, "Snapshot materialized.");
            return loaded;
        }, normalizedRoot, request.InventoryVersion, cancellationToken).ConfigureAwait(false);

        lock (_sync)
        {
            _states[BuildCacheKey(normalizedRoot, request.InventoryVersion)] = snapshot;
        }

        Report(progress, 100, "Tray dependency cache is ready.");
        _logger.LogInformation(
            "traycache.snapshot.build.done modsRoot={ModsRoot} inventoryVersion={InventoryVersion} packageCount={PackageCount} changedCount={ChangedCount} removedCount={RemovedCount} elapsedMs={ElapsedMs}",
            normalizedRoot,
            request.InventoryVersion,
            snapshot.Packages.Count,
            changedSet.Count,
            removedSet.Count,
            timer.ElapsedMilliseconds);
        return snapshot;
    }
    private async Task<bool> UpsertPackageCacheAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        BuildFile file,
        string fingerprint,
        CancellationToken cancellationToken)
    {
        try
        {
            var package = DbpfPackageIndexReader.ReadPackageIndex(file.Path);
            var entries = new List<EntryRow>();
            for (var i = 0; i < package.Entries.Length; i++)
            {
                var entry = package.Entries[i];
                if (!StructuredTypes.Contains(entry.Type))
                {
                    continue;
                }

                entries.Add(new EntryRow
                {
                    FingerprintKey = fingerprint,
                    EntryIndex = i,
                    Type = entry.Type,
                    Group = entry.Group,
                    Instance = entry.Instance,
                    IsDeleted = entry.IsDeleted,
                    DataOffset = entry.DataOffset,
                    CompressedSize = entry.CompressedSize,
                    UncompressedSize = entry.UncompressedSize,
                    CompressionType = entry.CompressionType
                });
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO TrayCachePackage (
                    FingerprintKey,
                    PackagePath,
                    Length,
                    LastWriteUtcTicks,
                    EntryCount,
                    ParseStatus,
                    ParseError,
                    UpdatedUtcTicks
                ) VALUES (
                    @FingerprintKey,
                    @PackagePath,
                    @Length,
                    @LastWriteUtcTicks,
                    @EntryCount,
                    @ParseStatus,
                    @ParseError,
                    @UpdatedUtcTicks
                )
                ON CONFLICT(FingerprintKey) DO UPDATE SET
                    PackagePath = excluded.PackagePath,
                    Length = excluded.Length,
                    LastWriteUtcTicks = excluded.LastWriteUtcTicks,
                    EntryCount = excluded.EntryCount,
                    ParseStatus = excluded.ParseStatus,
                    ParseError = excluded.ParseError,
                    UpdatedUtcTicks = excluded.UpdatedUtcTicks;
                """,
                new
                {
                    FingerprintKey = fingerprint,
                    PackagePath = file.Path,
                    Length = file.Length,
                    LastWriteUtcTicks = file.LastWriteTicks,
                    EntryCount = entries.Count,
                    ParseStatus = 1,
                    ParseError = (string?)null,
                    UpdatedUtcTicks = DateTime.UtcNow.Ticks
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM TrayCacheEntry WHERE FingerprintKey = @FingerprintKey;",
                new { FingerprintKey = fingerprint },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (entries.Count > 0)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO TrayCacheEntry (
                        FingerprintKey,
                        EntryIndex,
                        Type,
                        [Group],
                        Instance,
                        IsDeleted,
                        DataOffset,
                        CompressedSize,
                        UncompressedSize,
                        CompressionType
                    ) VALUES (
                        @FingerprintKey,
                        @EntryIndex,
                        @Type,
                        @Group,
                        @Instance,
                        @IsDeleted,
                        @DataOffset,
                        @CompressedSize,
                        @UncompressedSize,
                        @CompressionType
                    );
                    """,
                    entries,
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
            }

            return true;
        }
        catch (Exception ex)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO TrayCachePackage (
                    FingerprintKey,
                    PackagePath,
                    Length,
                    LastWriteUtcTicks,
                    EntryCount,
                    ParseStatus,
                    ParseError,
                    UpdatedUtcTicks
                ) VALUES (
                    @FingerprintKey,
                    @PackagePath,
                    @Length,
                    @LastWriteUtcTicks,
                    @EntryCount,
                    @ParseStatus,
                    @ParseError,
                    @UpdatedUtcTicks
                )
                ON CONFLICT(FingerprintKey) DO UPDATE SET
                    PackagePath = excluded.PackagePath,
                    Length = excluded.Length,
                    LastWriteUtcTicks = excluded.LastWriteUtcTicks,
                    EntryCount = excluded.EntryCount,
                    ParseStatus = excluded.ParseStatus,
                    ParseError = excluded.ParseError,
                    UpdatedUtcTicks = excluded.UpdatedUtcTicks;
                """,
                new
                {
                    FingerprintKey = fingerprint,
                    PackagePath = file.Path,
                    Length = file.Length,
                    LastWriteUtcTicks = file.LastWriteTicks,
                    EntryCount = 0,
                    ParseStatus = 0,
                    ParseError = ex.Message,
                    UpdatedUtcTicks = DateTime.UtcNow.Ticks
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM TrayCacheEntry WHERE FingerprintKey = @FingerprintKey;",
                new { FingerprintKey = fingerprint },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return false;
        }
    }

    private static async Task RebuildManifestAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string modsRootPath,
        long inventoryVersion,
        IReadOnlyList<ManifestRow> rows,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM TrayRootManifestPackage WHERE ModsRootPath = @ModsRootPath AND InventoryVersion = @InventoryVersion;",
            new { ModsRootPath = modsRootPath, InventoryVersion = inventoryVersion },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM TrayRootManifest WHERE ModsRootPath = @ModsRootPath AND InventoryVersion = @InventoryVersion;",
            new { ModsRootPath = modsRootPath, InventoryVersion = inventoryVersion },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM TrayRootManifestPackage WHERE ModsRootPath = @ModsRootPath AND InventoryVersion <> @InventoryVersion;",
            new { ModsRootPath = modsRootPath, InventoryVersion = inventoryVersion },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM TrayRootManifest WHERE ModsRootPath = @ModsRootPath AND InventoryVersion <> @InventoryVersion;",
            new { ModsRootPath = modsRootPath, InventoryVersion = inventoryVersion },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO TrayRootManifest (
                ModsRootPath,
                InventoryVersion,
                PackageCount,
                UpdatedUtcTicks
            ) VALUES (
                @ModsRootPath,
                @InventoryVersion,
                @PackageCount,
                @UpdatedUtcTicks
            );
            """,
            new
            {
                ModsRootPath = modsRootPath,
                InventoryVersion = inventoryVersion,
                PackageCount = rows.Count,
                UpdatedUtcTicks = DateTime.UtcNow.Ticks
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (rows.Count > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO TrayRootManifestPackage (
                    ModsRootPath,
                    InventoryVersion,
                    PackagePath,
                    FingerprintKey,
                    Length,
                    LastWriteUtcTicks
                ) VALUES (
                    @ModsRootPath,
                    @InventoryVersion,
                    @PackagePath,
                    @FingerprintKey,
                    @Length,
                    @LastWriteUtcTicks
                );
                """,
                rows,
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    private static async Task CleanupOrphansAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM TrayCacheEntry
            WHERE FingerprintKey NOT IN (
                SELECT FingerprintKey FROM TrayRootManifestPackage
            );
            """,
            transaction: transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            DELETE FROM TrayCachePackage
            WHERE FingerprintKey NOT IN (
                SELECT FingerprintKey FROM TrayRootManifestPackage
            );
            """,
            transaction: transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    private async Task<PackageIndexSnapshot?> LoadSnapshotAsync(
        SqliteConnection connection,
        string modsRootPath,
        long inventoryVersion,
        CancellationToken cancellationToken)
    {
        var hasManifest = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(1) FROM TrayRootManifest WHERE ModsRootPath = @ModsRootPath AND InventoryVersion = @InventoryVersion;",
            new { ModsRootPath = modsRootPath, InventoryVersion = inventoryVersion },
            cancellationToken: cancellationToken)).ConfigureAwait(false) > 0;
        if (!hasManifest)
        {
            return null;
        }

        var rows = (await connection.QueryAsync<ManifestRow>(new CommandDefinition(
            """
            SELECT
                ModsRootPath,
                InventoryVersion,
                PackagePath,
                FingerprintKey,
                Length,
                LastWriteUtcTicks
            FROM TrayRootManifestPackage
            WHERE ModsRootPath = @ModsRootPath
              AND InventoryVersion = @InventoryVersion
            ORDER BY PackagePath COLLATE NOCASE;
            """,
            new { ModsRootPath = modsRootPath, InventoryVersion = inventoryVersion },
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        return new PackageIndexSnapshot
        {
            ModsRootPath = modsRootPath,
            InventoryVersion = inventoryVersion,
            Packages = rows.Select(row => new IndexedPackageFile
            {
                FilePath = row.PackagePath,
                Length = row.Length,
                LastWriteTimeUtc = new DateTime(row.LastWriteUtcTicks, DateTimeKind.Utc),
                Entries = EmptyEntries,
                TypeIndexes = EmptyTypeIndexes
            }).ToArray(),
            Lookup = new SqliteTrayDependencyLookup(_database, modsRootPath, inventoryVersion)
        };
    }

    private async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition("DROP TABLE IF EXISTS PackageIndexSnapshots;", cancellationToken: cancellationToken)).ConfigureAwait(false);
        var version = await connection.ExecuteScalarAsync<long>(new CommandDefinition("PRAGMA user_version;", cancellationToken: cancellationToken)).ConfigureAwait(false);
        if (version != SchemaVersion)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                DROP TABLE IF EXISTS TrayCacheEntry;
                DROP TABLE IF EXISTS TrayCachePackage;
                DROP TABLE IF EXISTS TrayRootManifestPackage;
                DROP TABLE IF EXISTS TrayRootManifest;
                """,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            CREATE TABLE IF NOT EXISTS TrayCachePackage (
                FingerprintKey TEXT NOT NULL PRIMARY KEY,
                PackagePath TEXT NOT NULL,
                Length INTEGER NOT NULL,
                LastWriteUtcTicks INTEGER NOT NULL,
                EntryCount INTEGER NOT NULL,
                ParseStatus INTEGER NOT NULL,
                ParseError TEXT NULL,
                UpdatedUtcTicks INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS TrayCacheEntry (
                FingerprintKey TEXT NOT NULL,
                EntryIndex INTEGER NOT NULL,
                Type INTEGER NOT NULL,
                [Group] INTEGER NOT NULL,
                Instance INTEGER NOT NULL,
                IsDeleted INTEGER NOT NULL,
                DataOffset INTEGER NOT NULL,
                CompressedSize INTEGER NOT NULL,
                UncompressedSize INTEGER NOT NULL,
                CompressionType INTEGER NOT NULL,
                PRIMARY KEY (FingerprintKey, EntryIndex)
            );

            CREATE TABLE IF NOT EXISTS TrayRootManifest (
                ModsRootPath TEXT NOT NULL,
                InventoryVersion INTEGER NOT NULL,
                PackageCount INTEGER NOT NULL,
                UpdatedUtcTicks INTEGER NOT NULL,
                PRIMARY KEY (ModsRootPath, InventoryVersion)
            );

            CREATE TABLE IF NOT EXISTS TrayRootManifestPackage (
                ModsRootPath TEXT NOT NULL,
                InventoryVersion INTEGER NOT NULL,
                PackagePath TEXT NOT NULL,
                FingerprintKey TEXT NOT NULL,
                Length INTEGER NOT NULL,
                LastWriteUtcTicks INTEGER NOT NULL,
                PRIMARY KEY (ModsRootPath, InventoryVersion, PackagePath)
            );

            CREATE INDEX IF NOT EXISTS IX_TrayCacheEntry_Exact
                ON TrayCacheEntry(Type, [Group], Instance, FingerprintKey, EntryIndex)
                WHERE IsDeleted = 0;

            CREATE INDEX IF NOT EXISTS IX_TrayCacheEntry_TypeInstance
                ON TrayCacheEntry(Type, Instance, FingerprintKey, EntryIndex)
                WHERE IsDeleted = 0;

            CREATE INDEX IF NOT EXISTS IX_TrayCacheEntry_Instance
                ON TrayCacheEntry(Instance, Type, FingerprintKey, EntryIndex)
                WHERE IsDeleted = 0;

            CREATE INDEX IF NOT EXISTS IX_TrayRootManifestPackage_Fingerprint
                ON TrayRootManifestPackage(FingerprintKey);
            """,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (version != SchemaVersion)
        {
            await connection.ExecuteAsync(new CommandDefinition($"PRAGMA user_version = {SchemaVersion};", cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }
    private async Task<T> ExecuteWithCorruptionRecoveryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string modsRoot,
        long inventoryVersion,
        CancellationToken cancellationToken)
    {
        var retried = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException ex) when (!retried && IsCorruption(ex))
            {
                retried = true;
                RecoverCorruptedDatabase(ex, modsRoot, inventoryVersion);
            }
        }
    }

    private void RecoverCorruptedDatabase(SqliteException ex, string modsRoot, long inventoryVersion)
    {
        _logger.LogWarning(
            ex,
            "traycache.sqlite.corrupt modsRoot={ModsRoot} inventoryVersion={InventoryVersion} dbPath={DbPath}",
            modsRoot,
            inventoryVersion,
            _databasePath);

        lock (_sync)
        {
            _states.Clear();
        }

        var suffix = DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MoveIfExists(_databasePath, _databasePath + ".bad." + suffix);
        MoveIfExists(_databasePath + "-wal", _databasePath + ".bad." + suffix + ".wal");
        MoveIfExists(_databasePath + "-shm", _databasePath + ".bad." + suffix + ".shm");
    }

    private static bool IsCorruption(SqliteException ex)
    {
        return ex.SqliteErrorCode == 11 || ex.SqliteErrorCode == 26;
    }

    private static void MoveIfExists(string source, string target)
    {
        if (!File.Exists(source))
        {
            return;
        }

        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Move(source, target, overwrite: true);
    }

    private static string ComputeFingerprint(BuildFile file)
    {
        var payload = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{file.Path}|{file.Length}|{file.LastWriteTicks}");
        var bytes = Encoding.UTF8.GetBytes(payload);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string BuildCacheKey(string root, long inventoryVersion)
    {
        return root + "|" + inventoryVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void Report(IProgress<TrayDependencyExportProgress>? progress, int percent, string detail)
    {
        progress?.Report(new TrayDependencyExportProgress
        {
            Stage = TrayDependencyExportStage.IndexingPackages,
            Percent = Math.Clamp(percent, 0, 100),
            Detail = detail
        });
    }

    private sealed record BuildFile(string Path, long Length, long LastWriteTicks);

    private sealed class EntryRow
    {
        public string FingerprintKey { get; set; } = string.Empty;
        public int EntryIndex { get; set; }
        public uint Type { get; set; }
        public uint Group { get; set; }
        public ulong Instance { get; set; }
        public bool IsDeleted { get; set; }
        public long DataOffset { get; set; }
        public int CompressedSize { get; set; }
        public int UncompressedSize { get; set; }
        public ushort CompressionType { get; set; }
    }

    private sealed class ManifestRow
    {
        public string ModsRootPath { get; set; } = string.Empty;
        public long InventoryVersion { get; set; }
        public string PackagePath { get; set; } = string.Empty;
        public string FingerprintKey { get; set; } = string.Empty;
        public long Length { get; set; }
        public long LastWriteUtcTicks { get; set; }
    }
}

internal sealed class SqliteTrayDependencyLookup : ITrayDependencyLookup
{
    private readonly SqliteCacheDatabase _database;
    private readonly string _modsRootPath;
    private readonly long _inventoryVersion;

    public SqliteTrayDependencyLookup(SqliteCacheDatabase database, string modsRootPath, long inventoryVersion)
    {
        _database = database;
        _modsRootPath = modsRootPath;
        _inventoryVersion = inventoryVersion;
    }

    public ITrayDependencyLookupSession OpenSession()
    {
        return new SqliteTrayDependencyLookupSession(_database.OpenConnection(), _modsRootPath, _inventoryVersion);
    }
}

internal sealed class SqliteTrayDependencyLookupSession : ITrayDependencyLookupSession
{
    private static readonly uint[] SupportedTypes = KnownResourceTypes.Supported;

    private readonly SqliteConnection _connection;
    private readonly string _modsRootPath;
    private readonly long _inventoryVersion;
    private readonly LruCache<TrayResourceKey, ResolvedResourceRef[]> _exactCache = new(4096);
    private readonly LruCache<TypeInstanceKey, ResolvedResourceRef[]> _typeInstanceCache = new(8192);
    private readonly LruCache<ulong, bool> _supportedCache = new(8192);

    public SqliteTrayDependencyLookupSession(SqliteConnection connection, string modsRootPath, long inventoryVersion)
    {
        _connection = connection;
        _modsRootPath = modsRootPath;
        _inventoryVersion = inventoryVersion;
    }

    public bool TryGetExact(TrayResourceKey key, out ResolvedResourceRef[] matches)
    {
        if (_exactCache.TryGetValue(key, out matches))
        {
            return matches.Length > 0;
        }

        matches = QueryExact(key);
        _exactCache.Set(key, matches);
        return matches.Length > 0;
    }

    public bool TryGetTypeInstance(TypeInstanceKey key, out ResolvedResourceRef[] matches)
    {
        if (_typeInstanceCache.TryGetValue(key, out matches))
        {
            return matches.Length > 0;
        }

        matches = QueryTypeInstance(key);
        _typeInstanceCache.Set(key, matches);
        return matches.Length > 0;
    }

    public bool HasSupportedInstance(ulong instance)
    {
        if (_supportedCache.TryGetValue(instance, out var value))
        {
            return value;
        }

        var found = _connection.ExecuteScalar<long>(
            """
            SELECT COUNT(1)
            FROM TrayRootManifestPackage manifest
            INNER JOIN TrayCacheEntry entry ON entry.FingerprintKey = manifest.FingerprintKey
            INNER JOIN TrayCachePackage package ON package.FingerprintKey = manifest.FingerprintKey
            WHERE manifest.ModsRootPath = @ModsRootPath
              AND manifest.InventoryVersion = @InventoryVersion
              AND package.ParseStatus = 1
              AND entry.IsDeleted = 0
              AND entry.Instance = @Instance
              AND entry.Type IN @SupportedTypes;
            """,
            new
            {
                ModsRootPath = _modsRootPath,
                InventoryVersion = _inventoryVersion,
                Instance = instance,
                SupportedTypes
            }) > 0;

        _supportedCache.Set(instance, found);
        return found;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private ResolvedResourceRef[] QueryExact(TrayResourceKey key)
    {
        return _connection.Query<LookupRow>(
                """
                SELECT
                    manifest.PackagePath AS FilePath,
                    entry.Type,
                    entry.[Group],
                    entry.Instance,
                    entry.IsDeleted,
                    entry.DataOffset,
                    entry.CompressedSize,
                    entry.UncompressedSize,
                    entry.CompressionType
                FROM TrayRootManifestPackage manifest
                INNER JOIN TrayCacheEntry entry ON entry.FingerprintKey = manifest.FingerprintKey
                INNER JOIN TrayCachePackage package ON package.FingerprintKey = manifest.FingerprintKey
                WHERE manifest.ModsRootPath = @ModsRootPath
                  AND manifest.InventoryVersion = @InventoryVersion
                  AND package.ParseStatus = 1
                  AND entry.IsDeleted = 0
                  AND entry.Type = @Type
                  AND entry.[Group] = @Group
                  AND entry.Instance = @Instance
                ORDER BY manifest.PackagePath COLLATE NOCASE, entry.EntryIndex;
                """,
                new
                {
                    ModsRootPath = _modsRootPath,
                    InventoryVersion = _inventoryVersion,
                    key.Type,
                    key.Group,
                    key.Instance
                })
            .Select(ToResolved)
            .ToArray();
    }

    private ResolvedResourceRef[] QueryTypeInstance(TypeInstanceKey key)
    {
        return _connection.Query<LookupRow>(
                """
                SELECT
                    manifest.PackagePath AS FilePath,
                    entry.Type,
                    entry.[Group],
                    entry.Instance,
                    entry.IsDeleted,
                    entry.DataOffset,
                    entry.CompressedSize,
                    entry.UncompressedSize,
                    entry.CompressionType
                FROM TrayRootManifestPackage manifest
                INNER JOIN TrayCacheEntry entry ON entry.FingerprintKey = manifest.FingerprintKey
                INNER JOIN TrayCachePackage package ON package.FingerprintKey = manifest.FingerprintKey
                WHERE manifest.ModsRootPath = @ModsRootPath
                  AND manifest.InventoryVersion = @InventoryVersion
                  AND package.ParseStatus = 1
                  AND entry.IsDeleted = 0
                  AND entry.Type = @Type
                  AND entry.Instance = @Instance
                ORDER BY manifest.PackagePath COLLATE NOCASE, entry.EntryIndex;
                """,
                new
                {
                    ModsRootPath = _modsRootPath,
                    InventoryVersion = _inventoryVersion,
                    key.Type,
                    key.Instance
                })
            .Select(ToResolved)
            .ToArray();
    }

    private static ResolvedResourceRef ToResolved(LookupRow row)
    {
        return new ResolvedResourceRef
        {
            Key = new TrayResourceKey(row.Type, row.Group, row.Instance),
            FilePath = row.FilePath,
            Entry = new PackageIndexEntry
            {
                Type = row.Type,
                Group = row.Group,
                Instance = row.Instance,
                IsDeleted = row.IsDeleted,
                DataOffset = row.DataOffset,
                CompressedSize = row.CompressedSize,
                UncompressedSize = row.UncompressedSize,
                CompressionType = row.CompressionType
            }
        };
    }

    private sealed class LookupRow
    {
        public string FilePath { get; set; } = string.Empty;
        public uint Type { get; set; }
        public uint Group { get; set; }
        public ulong Instance { get; set; }
        public bool IsDeleted { get; set; }
        public long DataOffset { get; set; }
        public int CompressedSize { get; set; }
        public int UncompressedSize { get; set; }
        public ushort CompressionType { get; set; }
    }
}

internal sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _list = new();

    public LruCache(int capacity)
    {
        _capacity = Math.Max(1, capacity);
        _map = new Dictionary<TKey, LinkedListNode<Entry>>(_capacity);
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _list.Remove(node);
            _list.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            existing.Value = new Entry(key, value);
            _list.Remove(existing);
            _list.AddFirst(existing);
            return;
        }

        var node = new LinkedListNode<Entry>(new Entry(key, value));
        _list.AddFirst(node);
        _map[key] = node;

        if (_map.Count <= _capacity)
        {
            return;
        }

        var tail = _list.Last;
        if (tail is null)
        {
            return;
        }

        _list.RemoveLast();
        _map.Remove(tail.Value.Key);
    }

    private readonly record struct Entry(TKey Key, TValue Value);
}
