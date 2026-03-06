using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Infrastructure.Persistence;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Infrastructure.Tray;

public sealed class TrayPreviewRootSnapshotStore : ITrayPreviewRootSnapshotStore
{
    private const string FormatVersion = "tray-preview-root-v1";
    private const int DefaultMaxPersistedRoots = 8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly object _gate = new();
    private readonly AppCacheDatabase _database;
    private readonly IPathIdentityResolver _pathIdentityResolver;
    private readonly ILogger<TrayPreviewRootSnapshotStore> _logger;
    private readonly int _maxPersistedRoots;
    private bool _schemaEnsured;

    public TrayPreviewRootSnapshotStore()
        : this(
            cacheRootPath: Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimsModDesktop",
                "Cache"))
    {
    }

    public TrayPreviewRootSnapshotStore(
        string cacheRootPath,
        IConfigurationProvider? configurationProvider = null,
        IPathIdentityResolver? pathIdentityResolver = null,
        ILogger<TrayPreviewRootSnapshotStore>? logger = null)
    {
        _database = new AppCacheDatabase(cacheRootPath);
        _pathIdentityResolver = pathIdentityResolver ?? new SystemPathIdentityResolver();
        _logger = logger ?? NullLogger<TrayPreviewRootSnapshotStore>.Instance;
        _maxPersistedRoots = Math.Max(
            1,
            configurationProvider?.GetConfigurationAsync<int?>("Performance.TrayPreviewRootSnapshot.MaxPersistedRoots")
                .GetAwaiter()
                .GetResult() ?? DefaultMaxPersistedRoots);
    }

    public bool TryLoad(
        string trayRootPath,
        long directoryWriteUtcTicks,
        out TrayPreviewRootSnapshotRecord snapshot)
    {
        snapshot = null!;
        var normalizedTrayRoot = NormalizeTrayRoot(trayRootPath);
        if (string.IsNullOrWhiteSpace(normalizedTrayRoot))
        {
            return false;
        }

        lock (_gate)
        {
            EnsureSchemaLocked();

            try
            {
                using var connection = _database.OpenConnection();
                var header = connection.QuerySingleOrDefault<SnapshotHeaderRow>(
                    """
                    SELECT
                        TrayRootPath,
                        DirectoryWriteUtcTicks,
                        RootFingerprint,
                        CachedAtUtcTicks,
                        ItemCount
                    FROM TrayPreviewRootSnapshot
                    WHERE TrayRootPath = @TrayRootPath
                      AND DirectoryWriteUtcTicks = @DirectoryWriteUtcTicks
                      AND FormatVersion = @FormatVersion;
                    """,
                    new
                    {
                        TrayRootPath = normalizedTrayRoot,
                        DirectoryWriteUtcTicks = directoryWriteUtcTicks,
                        FormatVersion
                    });
                if (header is null)
                {
                    return false;
                }

                var itemRows = connection.Query<SnapshotItemRow>(
                        """
                        SELECT
                            Ordinal,
                            GroupJson,
                            ChildGroupsJson,
                            PresetType,
                            ItemName,
                            FileListPreview,
                            NormalizedFallbackSearchText,
                            FileCount,
                            TotalBytes,
                            LatestWriteUtcTicks
                        FROM TrayPreviewRootSnapshotItem
                        WHERE TrayRootPath = @TrayRootPath
                          AND DirectoryWriteUtcTicks = @DirectoryWriteUtcTicks
                          AND FormatVersion = @FormatVersion
                        ORDER BY Ordinal ASC;
                        """,
                        new
                        {
                            TrayRootPath = normalizedTrayRoot,
                            DirectoryWriteUtcTicks = directoryWriteUtcTicks,
                            FormatVersion
                        })
                    .ToArray();
                if (itemRows.Length == 0 && header.ItemCount > 0)
                {
                    return false;
                }

                snapshot = new TrayPreviewRootSnapshotRecord
                {
                    TrayRootPath = normalizedTrayRoot,
                    DirectoryWriteUtcTicks = header.DirectoryWriteUtcTicks,
                    RootFingerprint = header.RootFingerprint,
                    CachedAtUtc = new DateTime(header.CachedAtUtcTicks, DateTimeKind.Utc),
                    Items = itemRows
                        .Select(ToItemRecord)
                        .ToArray()
                };

                _logger.LogDebug(
                    "traypreview.rootsnapshot.persist.hit trayRoot={TrayRoot} fingerprint={Fingerprint} rowCount={RowCount}",
                    normalizedTrayRoot,
                    snapshot.RootFingerprint,
                    snapshot.Items.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "traypreview.rootsnapshot.persist.load.fail trayRoot={TrayRoot}",
                    normalizedTrayRoot);
                return false;
            }
        }
    }

    public void Save(TrayPreviewRootSnapshotRecord snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var normalizedTrayRoot = NormalizeTrayRoot(snapshot.TrayRootPath);
        if (string.IsNullOrWhiteSpace(normalizedTrayRoot))
        {
            return;
        }

        lock (_gate)
        {
            EnsureSchemaLocked();

            using var connection = _database.OpenConnection();
            using var transaction = connection.BeginTransaction();

            connection.Execute(
                """
                DELETE FROM TrayPreviewRootSnapshotItem
                WHERE TrayRootPath = @TrayRootPath
                  AND FormatVersion = @FormatVersion;
                DELETE FROM TrayPreviewRootSnapshot
                WHERE TrayRootPath = @TrayRootPath
                  AND FormatVersion = @FormatVersion;
                """,
                new
                {
                    TrayRootPath = normalizedTrayRoot,
                    FormatVersion
                },
                transaction: transaction);

            connection.Execute(
                """
                INSERT INTO TrayPreviewRootSnapshot (
                    TrayRootPath,
                    DirectoryWriteUtcTicks,
                    RootFingerprint,
                    CachedAtUtcTicks,
                    ItemCount,
                    FormatVersion
                )
                VALUES (
                    @TrayRootPath,
                    @DirectoryWriteUtcTicks,
                    @RootFingerprint,
                    @CachedAtUtcTicks,
                    @ItemCount,
                    @FormatVersion
                );
                """,
                new SnapshotHeaderRow
                {
                    TrayRootPath = normalizedTrayRoot,
                    DirectoryWriteUtcTicks = snapshot.DirectoryWriteUtcTicks,
                    RootFingerprint = snapshot.RootFingerprint,
                    CachedAtUtcTicks = snapshot.CachedAtUtc.ToUniversalTime().Ticks,
                    ItemCount = snapshot.Items.Count,
                    FormatVersion = FormatVersion
                },
                transaction: transaction);

            connection.Execute(
                """
                INSERT INTO TrayPreviewRootSnapshotItem (
                    TrayRootPath,
                    DirectoryWriteUtcTicks,
                    FormatVersion,
                    Ordinal,
                    GroupJson,
                    ChildGroupsJson,
                    PresetType,
                    ItemName,
                    FileListPreview,
                    NormalizedFallbackSearchText,
                    FileCount,
                    TotalBytes,
                    LatestWriteUtcTicks
                )
                VALUES (
                    @TrayRootPath,
                    @DirectoryWriteUtcTicks,
                    @FormatVersion,
                    @Ordinal,
                    @GroupJson,
                    @ChildGroupsJson,
                    @PresetType,
                    @ItemName,
                    @FileListPreview,
                    @NormalizedFallbackSearchText,
                    @FileCount,
                    @TotalBytes,
                    @LatestWriteUtcTicks
                );
                """,
                snapshot.Items
                    .Select((item, ordinal) => ToItemRow(normalizedTrayRoot, snapshot.DirectoryWriteUtcTicks, ordinal, item))
                    .ToArray(),
                transaction: transaction);

            PruneStaleRootsLocked(connection, transaction);
            transaction.Commit();

            _logger.LogDebug(
                "traypreview.rootsnapshot.persist.write trayRoot={TrayRoot} fingerprint={Fingerprint} rowCount={RowCount}",
                normalizedTrayRoot,
                snapshot.RootFingerprint,
                snapshot.Items.Count);
        }
    }

    public void Clear(string? trayRootPath = null)
    {
        lock (_gate)
        {
            EnsureSchemaLocked();

            using var connection = _database.OpenConnection();
            if (string.IsNullOrWhiteSpace(trayRootPath))
            {
                connection.Execute("DELETE FROM TrayPreviewRootSnapshotItem;");
                connection.Execute("DELETE FROM TrayPreviewRootSnapshot;");
                return;
            }

            var normalizedTrayRoot = NormalizeTrayRoot(trayRootPath);
            connection.Execute(
                """
                DELETE FROM TrayPreviewRootSnapshotItem
                WHERE TrayRootPath = @TrayRootPath
                  AND FormatVersion = @FormatVersion;
                DELETE FROM TrayPreviewRootSnapshot
                WHERE TrayRootPath = @TrayRootPath
                  AND FormatVersion = @FormatVersion;
                """,
                new
                {
                    TrayRootPath = normalizedTrayRoot,
                    FormatVersion
                });
        }
    }

    private void PruneStaleRootsLocked(SqliteConnection connection, SqliteTransaction transaction)
    {
        var staleRoots = connection.Query<StaleRootRow>(
                """
                SELECT
                    TrayRootPath,
                    MAX(CachedAtUtcTicks) AS CachedAtUtcTicks
                FROM TrayPreviewRootSnapshot
                WHERE FormatVersion = @FormatVersion
                GROUP BY TrayRootPath
                ORDER BY CachedAtUtcTicks ASC;
                """,
                new { FormatVersion },
                transaction: transaction)
            .ToArray();
        if (staleRoots.Length <= _maxPersistedRoots)
        {
            return;
        }

        foreach (var root in staleRoots.Take(staleRoots.Length - _maxPersistedRoots))
        {
            connection.Execute(
                """
                DELETE FROM TrayPreviewRootSnapshotItem
                WHERE TrayRootPath = @TrayRootPath
                  AND FormatVersion = @FormatVersion;
                DELETE FROM TrayPreviewRootSnapshot
                WHERE TrayRootPath = @TrayRootPath
                  AND FormatVersion = @FormatVersion;
                """,
                new
                {
                    root.TrayRootPath,
                    FormatVersion
                },
                transaction: transaction);
        }
    }

    private void EnsureSchemaLocked()
    {
        if (_schemaEnsured)
        {
            return;
        }

        using var connection = _database.OpenConnection();
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS TrayPreviewRootSnapshot (
                TrayRootPath TEXT NOT NULL,
                DirectoryWriteUtcTicks INTEGER NOT NULL,
                RootFingerprint TEXT NOT NULL,
                CachedAtUtcTicks INTEGER NOT NULL,
                ItemCount INTEGER NOT NULL,
                FormatVersion TEXT NOT NULL,
                PRIMARY KEY (TrayRootPath, DirectoryWriteUtcTicks, FormatVersion)
            );

            CREATE TABLE IF NOT EXISTS TrayPreviewRootSnapshotItem (
                TrayRootPath TEXT NOT NULL,
                DirectoryWriteUtcTicks INTEGER NOT NULL,
                FormatVersion TEXT NOT NULL,
                Ordinal INTEGER NOT NULL,
                GroupJson TEXT NOT NULL,
                ChildGroupsJson TEXT NOT NULL,
                PresetType TEXT NOT NULL,
                ItemName TEXT NOT NULL,
                FileListPreview TEXT NOT NULL,
                NormalizedFallbackSearchText TEXT NOT NULL,
                FileCount INTEGER NOT NULL,
                TotalBytes INTEGER NOT NULL,
                LatestWriteUtcTicks INTEGER NOT NULL,
                PRIMARY KEY (TrayRootPath, DirectoryWriteUtcTicks, FormatVersion, Ordinal)
            );
            """);
        _schemaEnsured = true;
    }

    private string NormalizeTrayRoot(string trayRootPath)
    {
        var resolved = _pathIdentityResolver.ResolveDirectory(trayRootPath ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(resolved.CanonicalPath))
        {
            return resolved.CanonicalPath;
        }

        if (!string.IsNullOrWhiteSpace(resolved.FullPath))
        {
            return resolved.FullPath;
        }

        return trayRootPath?.Trim().Trim('"') ?? string.Empty;
    }

    private static SnapshotItemRow ToItemRow(
        string trayRootPath,
        long directoryWriteUtcTicks,
        int ordinal,
        TrayPreviewRootItemRecord item)
    {
        return new SnapshotItemRow
        {
            TrayRootPath = trayRootPath,
            DirectoryWriteUtcTicks = directoryWriteUtcTicks,
            FormatVersion = FormatVersion,
            Ordinal = ordinal,
            GroupJson = JsonSerializer.Serialize(item.Group, JsonOptions),
            ChildGroupsJson = JsonSerializer.Serialize(item.ChildGroups, JsonOptions),
            PresetType = item.PresetType,
            ItemName = item.ItemName,
            FileListPreview = item.FileListPreview,
            NormalizedFallbackSearchText = item.NormalizedFallbackSearchText,
            FileCount = item.FileCount,
            TotalBytes = item.TotalBytes,
            LatestWriteUtcTicks = item.LatestWriteUtcTicks
        };
    }

    private static TrayPreviewRootItemRecord ToItemRecord(SnapshotItemRow row)
    {
        return new TrayPreviewRootItemRecord
        {
            Group = JsonSerializer.Deserialize<TrayPreviewGroupRecord>(row.GroupJson, JsonOptions)
                    ?? throw new InvalidOperationException("Tray preview root snapshot group payload is invalid."),
            ChildGroups = JsonSerializer.Deserialize<List<TrayPreviewGroupRecord>>(row.ChildGroupsJson, JsonOptions)
                          ?? new List<TrayPreviewGroupRecord>(),
            PresetType = row.PresetType,
            ItemName = row.ItemName,
            FileListPreview = row.FileListPreview,
            NormalizedFallbackSearchText = row.NormalizedFallbackSearchText,
            FileCount = row.FileCount,
            TotalBytes = row.TotalBytes,
            LatestWriteUtcTicks = row.LatestWriteUtcTicks
        };
    }

    private sealed class SnapshotHeaderRow
    {
        public string TrayRootPath { get; set; } = string.Empty;
        public long DirectoryWriteUtcTicks { get; set; }
        public string RootFingerprint { get; set; } = string.Empty;
        public long CachedAtUtcTicks { get; set; }
        public int ItemCount { get; set; }
        public string FormatVersion { get; set; } = string.Empty;
    }

    private sealed class SnapshotItemRow
    {
        public string TrayRootPath { get; set; } = string.Empty;
        public long DirectoryWriteUtcTicks { get; set; }
        public string FormatVersion { get; set; } = string.Empty;
        public int Ordinal { get; set; }
        public string GroupJson { get; set; } = string.Empty;
        public string ChildGroupsJson { get; set; } = string.Empty;
        public string PresetType { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string FileListPreview { get; set; } = string.Empty;
        public string NormalizedFallbackSearchText { get; set; } = string.Empty;
        public int FileCount { get; set; }
        public long TotalBytes { get; set; }
        public long LatestWriteUtcTicks { get; set; }
    }

    private sealed class StaleRootRow
    {
        public string TrayRootPath { get; set; } = string.Empty;
        public long CachedAtUtcTicks { get; set; }
    }
}
