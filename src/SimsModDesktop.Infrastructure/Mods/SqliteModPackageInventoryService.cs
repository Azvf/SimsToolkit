using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Infrastructure.Persistence;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Infrastructure.Mods;

internal sealed class SqliteModPackageInventoryService : IModPackageInventoryService
{
    private const int SchemaVersion = 3;
    private readonly AppCacheDatabase _database;
    private readonly ILogger<SqliteModPackageInventoryService> _logger;
    private readonly IPathIdentityResolver _pathIdentityResolver;

    public SqliteModPackageInventoryService()
        : this(new AppCacheDatabase(), NullLogger<SqliteModPackageInventoryService>.Instance, new SystemPathIdentityResolver())
    {
    }

    public SqliteModPackageInventoryService(string cacheRootPath)
        : this(new AppCacheDatabase(cacheRootPath), NullLogger<SqliteModPackageInventoryService>.Instance, new SystemPathIdentityResolver())
    {
    }

    public SqliteModPackageInventoryService(ILogger<SqliteModPackageInventoryService> logger)
        : this(new AppCacheDatabase(), logger, new SystemPathIdentityResolver())
    {
    }

    public SqliteModPackageInventoryService(
        ILogger<SqliteModPackageInventoryService> logger,
        IPathIdentityResolver pathIdentityResolver)
        : this(new AppCacheDatabase(), logger, pathIdentityResolver)
    {
    }

    private SqliteModPackageInventoryService(
        AppCacheDatabase database,
        ILogger<SqliteModPackageInventoryService> logger,
        IPathIdentityResolver pathIdentityResolver)
    {
        _database = database;
        _logger = logger;
        _pathIdentityResolver = pathIdentityResolver;
    }

    public Task<ModPackageInventoryRefreshResult> RefreshAsync(
        string modsRoot,
        IProgress<ModPackageInventoryRefreshProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(modsRoot);
        var startedAt = DateTime.UtcNow;

        var resolvedRoot = _pathIdentityResolver.ResolveDirectory(modsRoot);
        var rawRoot = !string.IsNullOrWhiteSpace(resolvedRoot.FullPath)
            ? resolvedRoot.FullPath
            : modsRoot.Trim().Trim('"');
        var normalizedRoot = !string.IsNullOrWhiteSpace(resolvedRoot.CanonicalPath)
            ? resolvedRoot.CanonicalPath
            : rawRoot;
        _logger.LogInformation(
            "path.resolve component={Component} rawPath={RawPath} canonicalPath={CanonicalPath} exists={Exists} isReparse={IsReparse} linkTarget={LinkTarget}",
            "modcache.inventory",
            rawRoot,
            normalizedRoot,
            resolvedRoot.Exists,
            resolvedRoot.IsReparsePoint,
            resolvedRoot.LinkTarget ?? string.Empty);
        var currentEntries = ScanEntries(normalizedRoot, progress, cancellationToken);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var aliasMigrationStarted = Stopwatch.StartNew();
        var aliasMovedRows = MigrateRootAlias(connection, transaction, rawRoot, normalizedRoot);
        aliasMigrationStarted.Stop();
        if (aliasMovedRows > 0)
        {
            _logger.LogInformation(
                "path.alias.migrate component={Component} fromRoot={FromRoot} toRoot={ToRoot} movedRows={MovedRows} elapsedMs={ElapsedMs}",
                "modcache.inventory",
                rawRoot,
                normalizedRoot,
                aliasMovedRows,
                aliasMigrationStarted.ElapsedMilliseconds);
        }

        var existingEntries = connection.Query<InventoryEntryRow>(
                """
                SELECT *
                FROM ModPackageInventoryEntries
                WHERE ModsRootPath = @ModsRootPath;
                """,
                new { ModsRootPath = normalizedRoot },
                transaction)
            .ToDictionary(row => row.PackagePath, StringComparer.OrdinalIgnoreCase);

        var added = new List<ModPackageInventoryEntry>();
        var changed = new List<ModPackageInventoryEntry>();
        var unchanged = new List<ModPackageInventoryEntry>();
        foreach (var entry in currentEntries)
        {
            if (!existingEntries.TryGetValue(entry.PackagePath, out var previous))
            {
                added.Add(entry);
                continue;
            }

            if (string.Equals(previous.PackageFingerprintKey, BuildPackageFingerprintKey(entry), StringComparison.Ordinal) &&
                string.Equals(previous.PackageType, entry.PackageType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(previous.ScopeHint, entry.ScopeHint, StringComparison.OrdinalIgnoreCase))
            {
                unchanged.Add(entry);
                continue;
            }

            changed.Add(entry);
        }

        var removed = existingEntries.Keys
            .Except(currentEntries.Select(entry => entry.PackagePath), StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var (inventoryVersion, hashInputBytes) = ComputeInventoryVersion(currentEntries, normalizedRoot);
        var validatedUtcTicks = DateTime.UtcNow.Ticks;
        _logger.LogInformation(
            "modcache.inventory.version mode={Mode} packageCount={PackageCount} hashInputBytes={HashInputBytes} inventoryVersion={InventoryVersion}",
            "stablehash",
            currentEntries.Count,
            hashInputBytes,
            inventoryVersion);
        var persistStartedAt = DateTime.UtcNow;
        connection.Execute(
            """
            DROP TABLE IF EXISTS TempInventoryScan;
            CREATE TEMP TABLE TempInventoryScan (
                PackagePath TEXT NOT NULL PRIMARY KEY,
                FileLength INTEGER NOT NULL,
                LastWriteUtcTicks INTEGER NOT NULL,
                PackageType TEXT NOT NULL,
                ScopeHint TEXT NOT NULL,
                PackageFingerprintKey TEXT NOT NULL
            );
            """,
            transaction: transaction);
        if (currentEntries.Count > 0)
        {
            connection.Execute(
                """
                INSERT INTO TempInventoryScan (
                    PackagePath,
                    FileLength,
                    LastWriteUtcTicks,
                    PackageType,
                    ScopeHint,
                    PackageFingerprintKey
                ) VALUES (
                    @PackagePath,
                    @FileLength,
                    @LastWriteUtcTicks,
                    @PackageType,
                    @ScopeHint,
                    @PackageFingerprintKey
                );
                """,
                currentEntries.Select(entry => new
                {
                    entry.PackagePath,
                    entry.FileLength,
                    entry.LastWriteUtcTicks,
                    entry.PackageType,
                    entry.ScopeHint,
                    PackageFingerprintKey = BuildPackageFingerprintKey(entry)
                }),
                transaction);
        }

        connection.Execute(
            """
            DELETE FROM ModPackageInventoryEntries
            WHERE ModsRootPath = @ModsRootPath
              AND NOT EXISTS (
                SELECT 1
                FROM TempInventoryScan src
                WHERE src.PackagePath = ModPackageInventoryEntries.PackagePath
              );
            """,
            new { ModsRootPath = normalizedRoot },
            transaction);

        connection.Execute(
            """
            UPDATE ModPackageInventoryEntries
            SET FileLength = (
                    SELECT src.FileLength
                    FROM TempInventoryScan src
                    WHERE src.PackagePath = ModPackageInventoryEntries.PackagePath
                ),
                LastWriteUtcTicks = (
                    SELECT src.LastWriteUtcTicks
                    FROM TempInventoryScan src
                    WHERE src.PackagePath = ModPackageInventoryEntries.PackagePath
                ),
                PackageType = (
                    SELECT src.PackageType
                    FROM TempInventoryScan src
                    WHERE src.PackagePath = ModPackageInventoryEntries.PackagePath
                ),
                ScopeHint = (
                    SELECT src.ScopeHint
                    FROM TempInventoryScan src
                    WHERE src.PackagePath = ModPackageInventoryEntries.PackagePath
                ),
                InventoryVersion = @InventoryVersion,
                PackageFingerprintKey = (
                    SELECT src.PackageFingerprintKey
                    FROM TempInventoryScan src
                    WHERE src.PackagePath = ModPackageInventoryEntries.PackagePath
                )
            WHERE ModsRootPath = @ModsRootPath
              AND EXISTS (
                    SELECT 1
                    FROM TempInventoryScan src
                    WHERE src.PackagePath = ModPackageInventoryEntries.PackagePath
                      AND (
                        ModPackageInventoryEntries.PackageFingerprintKey <> src.PackageFingerprintKey
                        OR ModPackageInventoryEntries.PackageType <> src.PackageType
                        OR ModPackageInventoryEntries.ScopeHint <> src.ScopeHint
                      )
                );
            """,
            new
            {
                ModsRootPath = normalizedRoot,
                InventoryVersion = inventoryVersion
            },
            transaction);

        connection.Execute(
            """
            INSERT INTO ModPackageInventoryEntries (
                ModsRootPath,
                PackagePath,
                FileLength,
                LastWriteUtcTicks,
                PackageType,
                ScopeHint,
                InventoryVersion,
                PackageFingerprintKey
            )
            SELECT
                @ModsRootPath,
                src.PackagePath,
                src.FileLength,
                src.LastWriteUtcTicks,
                src.PackageType,
                src.ScopeHint,
                @InventoryVersion,
                src.PackageFingerprintKey
            FROM TempInventoryScan src
            LEFT JOIN ModPackageInventoryEntries existing
                ON existing.ModsRootPath = @ModsRootPath
               AND existing.PackagePath = src.PackagePath
            WHERE existing.PackagePath IS NULL;
            """,
            new
            {
                ModsRootPath = normalizedRoot,
                InventoryVersion = inventoryVersion
            },
            transaction);

        connection.Execute("DROP TABLE IF EXISTS TempInventoryScan;", transaction: transaction);
        _logger.LogInformation(
            "modcache.inventory.persist.batch ModsRoot={ModsRoot} InventoryVersion={InventoryVersion} AddedCount={AddedCount} ChangedCount={ChangedCount} RemovedCount={RemovedCount} ElapsedMs={ElapsedMs}",
            normalizedRoot,
            inventoryVersion,
            added.Count,
            changed.Count,
            removed.Length,
            (DateTime.UtcNow - persistStartedAt).TotalMilliseconds);

        connection.Execute(
            """
            INSERT INTO ModPackageInventoryRoots (
                ModsRootPath,
                LastValidatedUtcTicks,
                InventoryVersion,
                PackageCount,
                Status
            ) VALUES (
                @ModsRootPath,
                @LastValidatedUtcTicks,
                @InventoryVersion,
                @PackageCount,
                @Status
            )
            ON CONFLICT(ModsRootPath) DO UPDATE SET
                LastValidatedUtcTicks = excluded.LastValidatedUtcTicks,
                InventoryVersion = excluded.InventoryVersion,
                PackageCount = excluded.PackageCount,
                Status = excluded.Status;
            """,
            new
            {
                ModsRootPath = normalizedRoot,
                LastValidatedUtcTicks = validatedUtcTicks,
                InventoryVersion = inventoryVersion,
                PackageCount = currentEntries.Count,
                Status = "Ready"
            },
            transaction);

        transaction.Commit();
        _logger.LogInformation(
            "modcache.inventory.delta ModsRoot={ModsRoot} InventoryVersion={InventoryVersion} PackageCount={PackageCount} AddedCount={AddedCount} ChangedCount={ChangedCount} RemovedCount={RemovedCount} ElapsedMs={ElapsedMs}",
            normalizedRoot,
            inventoryVersion,
            currentEntries.Count,
            added.Count,
            changed.Count,
            removed.Length,
            (DateTime.UtcNow - startedAt).TotalMilliseconds);

        progress?.Report(new ModPackageInventoryRefreshProgress
        {
            Stage = "persist",
            Percent = 100,
            Current = currentEntries.Count,
            Total = currentEntries.Count,
            Detail = $"Validated {currentEntries.Count} package(s)."
        });

        return Task.FromResult(new ModPackageInventoryRefreshResult
        {
            Snapshot = new ModPackageInventorySnapshot
            {
                ModsRootPath = normalizedRoot,
                InventoryVersion = inventoryVersion,
                Entries = currentEntries,
                LastValidatedUtcTicks = validatedUtcTicks
            },
            AddedEntries = added,
            ChangedEntries = changed,
            UnchangedEntries = unchanged,
            RemovedPackagePaths = removed
        });
    }

    private IReadOnlyList<ModPackageInventoryEntry> ScanEntries(
        string normalizedRoot,
        IProgress<ModPackageInventoryRefreshProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(normalizedRoot))
        {
            return Array.Empty<ModPackageInventoryEntry>();
        }

        var packageEnumeration = EnumeratePackageFiles(normalizedRoot, cancellationToken);
        if (packageEnumeration.SkippedReparseDirectoryCount > 0)
        {
            _logger.LogInformation(
                "path.reparse.skip component={Component} root={Root} skippedCount={SkippedCount} samplePath={SamplePath}",
                "modcache.inventory.scan",
                normalizedRoot,
                packageEnumeration.SkippedReparseDirectoryCount,
                packageEnumeration.SampleSkippedPath ?? string.Empty);
        }

        var packagePaths = packageEnumeration.Paths
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var entries = new List<ModPackageInventoryEntry>(packagePaths.Length);

        for (var index = 0; index < packagePaths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = packagePaths[index];
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(normalizedRoot, fileInfo.FullName);
            entries.Add(new ModPackageInventoryEntry
            {
                PackagePath = fileInfo.FullName,
                FileLength = fileInfo.Length,
                LastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
                PackageType = ResolvePackageType(fileInfo.Name),
                ScopeHint = ResolveScope(relativePath)
            });

            progress?.Report(new ModPackageInventoryRefreshProgress
            {
                Stage = "scan",
                Percent = ScaleProgress(0, 90, index + 1, packagePaths.Length),
                Current = index + 1,
                Total = packagePaths.Length,
                Detail = $"Validating package inventory {index + 1}/{packagePaths.Length}"
            });
        }

        if (packagePaths.Length == 0)
        {
            progress?.Report(new ModPackageInventoryRefreshProgress
            {
                Stage = "scan",
                Percent = 90,
                Current = 0,
                Total = 0,
                Detail = "No packages were found under the configured Mods path."
            });
        }

        return entries;
    }

    private System.Data.IDbConnection OpenConnection()
    {
        var connection = _database.OpenConnection();
        EnsureSchema(connection);
        return connection;
    }

    private static void EnsureSchema(System.Data.IDbConnection connection)
    {
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS ModPackageInventorySchemaMeta (
                SchemaVersion INTEGER NOT NULL
            );
            """);

        var version = connection.QuerySingleOrDefault<int?>("SELECT SchemaVersion FROM ModPackageInventorySchemaMeta LIMIT 1;");
        if (version != SchemaVersion)
        {
            connection.Execute(
                """
                DROP TABLE IF EXISTS ModPackageInventoryEntries;
                DROP TABLE IF EXISTS ModPackageInventoryRoots;
                DELETE FROM ModPackageInventorySchemaMeta;
                """);
        }

        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS ModPackageInventoryRoots (
                ModsRootPath TEXT PRIMARY KEY,
                LastValidatedUtcTicks INTEGER NOT NULL,
                InventoryVersion INTEGER NOT NULL,
                PackageCount INTEGER NOT NULL,
                Status TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ModPackageInventoryEntries (
                ModsRootPath TEXT NOT NULL,
                PackagePath TEXT NOT NULL,
                FileLength INTEGER NOT NULL,
                LastWriteUtcTicks INTEGER NOT NULL,
                PackageType TEXT NOT NULL,
                ScopeHint TEXT NOT NULL,
                InventoryVersion INTEGER NOT NULL,
                PackageFingerprintKey TEXT NOT NULL,
                PRIMARY KEY (ModsRootPath, PackagePath)
            );

            CREATE INDEX IF NOT EXISTS IX_ModPackageInventoryEntries_RootVersion
                ON ModPackageInventoryEntries (ModsRootPath, InventoryVersion);

            CREATE INDEX IF NOT EXISTS IX_ModPackageInventoryEntries_RootFingerprint
                ON ModPackageInventoryEntries (ModsRootPath, PackageFingerprintKey);
            """);

        if (version != SchemaVersion)
        {
            connection.Execute(
                "INSERT INTO ModPackageInventorySchemaMeta (SchemaVersion) VALUES (@SchemaVersion);",
                new { SchemaVersion });
        }
    }

    private static PackageEnumerationResult EnumeratePackageFiles(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        var collectedFiles = new List<string>();
        var skippedCount = 0;
        string? sampleSkippedPath = null;
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            string[] childDirectories;
            string[] packageFiles;
            try
            {
                childDirectories = Directory.GetDirectories(current);
                packageFiles = Directory.GetFiles(current, "*.package");
            }
            catch
            {
                continue;
            }

            foreach (var child in childDirectories)
            {
                if (IsReparseDirectory(child))
                {
                    skippedCount++;
                    sampleSkippedPath ??= child;
                    continue;
                }

                pending.Push(child);
            }

            foreach (var file in packageFiles)
            {
                collectedFiles.Add(file);
            }
        }

        return new PackageEnumerationResult(collectedFiles.ToArray(), skippedCount, sampleSkippedPath);
    }

    private static bool IsReparseDirectory(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch
        {
            return false;
        }
    }

    private static int MigrateRootAlias(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        string fromRoot,
        string toRoot)
    {
        if (string.IsNullOrWhiteSpace(fromRoot) ||
            string.IsNullOrWhiteSpace(toRoot) ||
            string.Equals(fromRoot, toRoot, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var aliasRootExists = connection.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM ModPackageInventoryRoots WHERE ModsRootPath = @ModsRootPath;",
            new { ModsRootPath = fromRoot },
            transaction) > 0;
        if (!aliasRootExists)
        {
            return 0;
        }

        var canonicalRootExists = connection.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM ModPackageInventoryRoots WHERE ModsRootPath = @ModsRootPath;",
            new { ModsRootPath = toRoot },
            transaction) > 0;
        if (canonicalRootExists)
        {
            var deletedEntries = connection.Execute(
                "DELETE FROM ModPackageInventoryEntries WHERE ModsRootPath = @ModsRootPath;",
                new { ModsRootPath = fromRoot },
                transaction);
            var deletedRoots = connection.Execute(
                "DELETE FROM ModPackageInventoryRoots WHERE ModsRootPath = @ModsRootPath;",
                new { ModsRootPath = fromRoot },
                transaction);
            return deletedEntries + deletedRoots;
        }

        var movedEntries = connection.Execute(
            "UPDATE ModPackageInventoryEntries SET ModsRootPath = @ToRoot WHERE ModsRootPath = @FromRoot;",
            new { FromRoot = fromRoot, ToRoot = toRoot },
            transaction);
        var movedRoots = connection.Execute(
            "UPDATE ModPackageInventoryRoots SET ModsRootPath = @ToRoot WHERE ModsRootPath = @FromRoot;",
            new { FromRoot = fromRoot, ToRoot = toRoot },
            transaction);
        return movedEntries + movedRoots;
    }

    private static string ResolvePackageType(string fileName)
    {
        return fileName.Contains("override", StringComparison.OrdinalIgnoreCase)
            ? "Override"
            : ".package";
    }

    private static string ResolveScope(string relativePath)
    {
        if (relativePath.Contains("cas", StringComparison.OrdinalIgnoreCase))
        {
            return "CAS";
        }

        if (relativePath.Contains("buildbuy", StringComparison.OrdinalIgnoreCase) ||
            relativePath.Contains("build_buy", StringComparison.OrdinalIgnoreCase) ||
            relativePath.Contains("build-buy", StringComparison.OrdinalIgnoreCase) ||
            relativePath.Contains("bb", StringComparison.OrdinalIgnoreCase))
        {
            return "BuildBuy";
        }

        return "All";
    }

    private static string BuildPackageFingerprintKey(ModPackageInventoryEntry entry)
    {
        return $"{entry.FileLength}:{entry.LastWriteUtcTicks}";
    }

    private static (long Version, int HashInputBytes) ComputeInventoryVersion(
        IReadOnlyList<ModPackageInventoryEntry> entries,
        string modsRootPath)
    {
        var builder = new StringBuilder(modsRootPath.Length + Math.Max(64, entries.Count * 96));
        builder.Append(modsRootPath);
        builder.Append('\n');
        foreach (var entry in entries.OrderBy(item => item.PackagePath, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(entry.PackagePath)
                .Append('|')
                .Append(BuildPackageFingerprintKey(entry))
                .Append('|')
                .Append(entry.PackageType)
                .Append('|')
                .Append(entry.ScopeHint)
                .Append('\n');
        }

        var payload = Encoding.UTF8.GetBytes(builder.ToString());
        var hash = SHA256.HashData(payload);
        var version = BinaryPrimitives.ReadInt64LittleEndian(hash.AsSpan(0, sizeof(long))) & long.MaxValue;
        if (version == 0)
        {
            version = 1;
        }

        return (version, payload.Length);
    }

    private static int ScaleProgress(int start, int end, int current, int total)
    {
        if (total <= 0)
        {
            return end;
        }

        var safeCurrent = Math.Clamp(current, 0, total);
        return start + (int)Math.Round((end - start) * (safeCurrent / (double)total), MidpointRounding.AwayFromZero);
    }

    private sealed class InventoryEntryRow
    {
        public string PackagePath { get; set; } = string.Empty;
        public long FileLength { get; set; }
        public long LastWriteUtcTicks { get; set; }
        public string PackageType { get; set; } = string.Empty;
        public string ScopeHint { get; set; } = string.Empty;
        public string PackageFingerprintKey { get; set; } = string.Empty;
    }

    private sealed record PackageEnumerationResult(
        IReadOnlyList<string> Paths,
        int SkippedReparseDirectoryCount,
        string? SampleSkippedPath);
}
