using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Infrastructure.Persistence;

namespace SimsModDesktop.Infrastructure.Mods;

internal sealed class SqliteModPackageInventoryService : IModPackageInventoryService
{
    private const int SchemaVersion = 2;
    private readonly AppCacheDatabase _database;
    private readonly ILogger<SqliteModPackageInventoryService> _logger;

    public SqliteModPackageInventoryService()
        : this(new AppCacheDatabase(), NullLogger<SqliteModPackageInventoryService>.Instance)
    {
    }

    public SqliteModPackageInventoryService(string cacheRootPath)
        : this(new AppCacheDatabase(cacheRootPath), NullLogger<SqliteModPackageInventoryService>.Instance)
    {
    }

    public SqliteModPackageInventoryService(ILogger<SqliteModPackageInventoryService> logger)
        : this(new AppCacheDatabase(), logger)
    {
    }

    private SqliteModPackageInventoryService(
        AppCacheDatabase database,
        ILogger<SqliteModPackageInventoryService> logger)
    {
        _database = database;
        _logger = logger;
    }

    public Task<ModPackageInventoryRefreshResult> RefreshAsync(
        string modsRoot,
        IProgress<ModPackageInventoryRefreshProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(modsRoot);
        var startedAt = DateTime.UtcNow;

        var normalizedRoot = Path.GetFullPath(modsRoot.Trim());
        var currentEntries = ScanEntries(normalizedRoot, progress, cancellationToken);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

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

        var inventoryVersion = DateTime.UtcNow.Ticks;
        foreach (var path in removed)
        {
            connection.Execute(
                """
                DELETE FROM ModPackageInventoryEntries
                WHERE ModsRootPath = @ModsRootPath
                  AND PackagePath = @PackagePath;
                """,
                new
                {
                    ModsRootPath = normalizedRoot,
                    PackagePath = path
                },
                transaction);
        }

        foreach (var entry in changed)
        {
            connection.Execute(
                """
                UPDATE ModPackageInventoryEntries
                SET FileLength = @FileLength,
                    LastWriteUtcTicks = @LastWriteUtcTicks,
                    PackageType = @PackageType,
                    ScopeHint = @ScopeHint,
                    InventoryVersion = @InventoryVersion,
                    PackageFingerprintKey = @PackageFingerprintKey
                WHERE ModsRootPath = @ModsRootPath
                  AND PackagePath = @StoredPackagePath;
                """,
                new
                {
                    ModsRootPath = normalizedRoot,
                    StoredPackagePath = existingEntries[entry.PackagePath].PackagePath,
                    entry.FileLength,
                    entry.LastWriteUtcTicks,
                    entry.PackageType,
                    entry.ScopeHint,
                    InventoryVersion = inventoryVersion,
                    PackageFingerprintKey = BuildPackageFingerprintKey(entry)
                },
                transaction);
        }

        foreach (var entry in added)
        {
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
                ) VALUES (
                    @ModsRootPath,
                    @PackagePath,
                    @FileLength,
                    @LastWriteUtcTicks,
                    @PackageType,
                    @ScopeHint,
                    @InventoryVersion,
                    @PackageFingerprintKey
                );
                """,
                new
                {
                    ModsRootPath = normalizedRoot,
                    entry.PackagePath,
                    entry.FileLength,
                    entry.LastWriteUtcTicks,
                    entry.PackageType,
                    entry.ScopeHint,
                    InventoryVersion = inventoryVersion,
                    PackageFingerprintKey = BuildPackageFingerprintKey(entry)
                },
                transaction);
        }

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
                LastValidatedUtcTicks = inventoryVersion,
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
                LastValidatedUtcTicks = inventoryVersion
            },
            AddedEntries = added,
            ChangedEntries = changed,
            UnchangedEntries = unchanged,
            RemovedPackagePaths = removed
        });
    }

    private static IReadOnlyList<ModPackageInventoryEntry> ScanEntries(
        string normalizedRoot,
        IProgress<ModPackageInventoryRefreshProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(normalizedRoot))
        {
            return Array.Empty<ModPackageInventoryEntry>();
        }

        var packagePaths = EnumeratePackageFiles(normalizedRoot, cancellationToken)
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

    private static IEnumerable<string> EnumeratePackageFiles(string root, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            string[] childDirectories;
            string[] files;
            try
            {
                childDirectories = Directory.GetDirectories(current);
                files = Directory.GetFiles(current, "*.package");
            }
            catch
            {
                continue;
            }

            foreach (var child in childDirectories)
            {
                pending.Push(child);
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
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
}
