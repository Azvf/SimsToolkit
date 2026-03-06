using System.Text.Json;
using Dapper;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.Infrastructure.Persistence;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Infrastructure.Saves;

public sealed class SavePreviewDescriptorStore : ISavePreviewDescriptorStore
{
    private const string DescriptorSchemaVersion = "save-preview-descriptor-v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly AppCacheDatabase _database;
    private readonly IPathIdentityResolver _pathIdentityResolver;

    public SavePreviewDescriptorStore()
        : this(GetDefaultCacheBasePath(), null)
    {
    }

    public SavePreviewDescriptorStore(string cacheBasePath, IPathIdentityResolver? pathIdentityResolver = null)
    {
        _database = new AppCacheDatabase(cacheBasePath);
        _pathIdentityResolver = pathIdentityResolver ?? new SystemPathIdentityResolver();
    }

    public bool IsDescriptorCurrent(string saveFilePath, SavePreviewDescriptorManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentNullException.ThrowIfNull(manifest);

        var file = new FileInfo(NormalizePath(saveFilePath));
        if (!file.Exists)
        {
            return false;
        }

        return string.Equals(manifest.SourceSavePath, file.FullName, StringComparison.OrdinalIgnoreCase) &&
               manifest.SourceLength == file.Length &&
               manifest.SourceLastWriteTimeUtc == file.LastWriteTimeUtc &&
               string.Equals(manifest.DescriptorSchemaVersion, DescriptorSchemaVersion, StringComparison.Ordinal);
    }

    public bool TryLoadDescriptor(string saveFilePath, out SavePreviewDescriptorManifest manifest)
    {
        manifest = null!;
        var normalizedSavePath = NormalizePath(saveFilePath);
        if (string.IsNullOrWhiteSpace(normalizedSavePath))
        {
            return false;
        }

        try
        {
            using var connection = OpenConnection();
            var header = connection.QuerySingleOrDefault<DescriptorHeaderRecord>(
                """
                SELECT
                    SourceSavePath,
                    SourceLength,
                    SourceLastWriteUtcTicks,
                    DescriptorSchemaVersion,
                    BuildStartedUtcTicks,
                    BuildCompletedUtcTicks,
                    TotalHouseholdCount,
                    ExportableHouseholdCount,
                    ReadyHouseholdCount,
                    BlockedHouseholdCount
                FROM SavePreviewDescriptor
                WHERE SourceSavePath = @SourceSavePath;
                """,
                new { SourceSavePath = normalizedSavePath });

            if (header is null)
            {
                return false;
            }

            var entries = connection.Query<DescriptorItemRecord>(
                    """
                    SELECT
                        SourceSavePath,
                        HouseholdId,
                        TrayItemKey,
                        StableInstanceIdHex,
                        HouseholdName,
                        HomeZoneName,
                        HouseholdSize,
                        CanExport,
                        BuildState,
                        LastError,
                        SearchText,
                        DisplayTitle,
                        DisplaySubtitle,
                        DisplayDescription,
                        DisplayPrimaryMeta,
                        DisplaySecondaryMeta,
                        DisplayTertiaryMeta
                    FROM SavePreviewDescriptorItem
                    WHERE SourceSavePath = @SourceSavePath
                    ORDER BY HouseholdId;
                    """,
                    new { SourceSavePath = normalizedSavePath })
                .Select(MapEntry)
                .ToList();

            manifest = new SavePreviewDescriptorManifest
            {
                SourceSavePath = header.SourceSavePath,
                SourceLength = header.SourceLength,
                SourceLastWriteTimeUtc = new DateTime(header.SourceLastWriteUtcTicks, DateTimeKind.Utc),
                DescriptorSchemaVersion = header.DescriptorSchemaVersion,
                BuildStartedUtc = new DateTime(header.BuildStartedUtcTicks, DateTimeKind.Utc),
                BuildCompletedUtc = new DateTime(header.BuildCompletedUtcTicks, DateTimeKind.Utc),
                TotalHouseholdCount = header.TotalHouseholdCount,
                ExportableHouseholdCount = header.ExportableHouseholdCount,
                ReadyHouseholdCount = header.ReadyHouseholdCount,
                BlockedHouseholdCount = header.BlockedHouseholdCount,
                Entries = entries
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void SaveDescriptor(string saveFilePath, SavePreviewDescriptorManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentNullException.ThrowIfNull(manifest);

        var normalizedSavePath = NormalizePath(manifest.SourceSavePath);
        if (string.IsNullOrWhiteSpace(normalizedSavePath))
        {
            normalizedSavePath = NormalizePath(saveFilePath);
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        connection.Execute(
            """
            INSERT INTO SavePreviewDescriptor (
                SourceSavePath,
                SourceLength,
                SourceLastWriteUtcTicks,
                DescriptorSchemaVersion,
                BuildStartedUtcTicks,
                BuildCompletedUtcTicks,
                TotalHouseholdCount,
                ExportableHouseholdCount,
                ReadyHouseholdCount,
                BlockedHouseholdCount
            )
            VALUES (
                @SourceSavePath,
                @SourceLength,
                @SourceLastWriteUtcTicks,
                @DescriptorSchemaVersion,
                @BuildStartedUtcTicks,
                @BuildCompletedUtcTicks,
                @TotalHouseholdCount,
                @ExportableHouseholdCount,
                @ReadyHouseholdCount,
                @BlockedHouseholdCount
            )
            ON CONFLICT(SourceSavePath) DO UPDATE SET
                SourceLength = excluded.SourceLength,
                SourceLastWriteUtcTicks = excluded.SourceLastWriteUtcTicks,
                DescriptorSchemaVersion = excluded.DescriptorSchemaVersion,
                BuildStartedUtcTicks = excluded.BuildStartedUtcTicks,
                BuildCompletedUtcTicks = excluded.BuildCompletedUtcTicks,
                TotalHouseholdCount = excluded.TotalHouseholdCount,
                ExportableHouseholdCount = excluded.ExportableHouseholdCount,
                ReadyHouseholdCount = excluded.ReadyHouseholdCount,
                BlockedHouseholdCount = excluded.BlockedHouseholdCount;
            """,
            new DescriptorHeaderRecord
            {
                SourceSavePath = normalizedSavePath,
                SourceLength = manifest.SourceLength,
                SourceLastWriteUtcTicks = manifest.SourceLastWriteTimeUtc.ToUniversalTime().Ticks,
                DescriptorSchemaVersion = DescriptorSchemaVersion,
                BuildStartedUtcTicks = manifest.BuildStartedUtc.ToUniversalTime().Ticks,
                BuildCompletedUtcTicks = manifest.BuildCompletedUtc.ToUniversalTime().Ticks,
                TotalHouseholdCount = manifest.TotalHouseholdCount,
                ExportableHouseholdCount = manifest.ExportableHouseholdCount,
                ReadyHouseholdCount = manifest.ReadyHouseholdCount,
                BlockedHouseholdCount = manifest.BlockedHouseholdCount
            },
            transaction);

        connection.Execute(
            "DELETE FROM SavePreviewDescriptorItem WHERE SourceSavePath = @SourceSavePath;",
            new { SourceSavePath = normalizedSavePath },
            transaction);

        var itemRecords = manifest.Entries.Select(entry => new DescriptorItemRecord
        {
            SourceSavePath = normalizedSavePath,
            HouseholdId = entry.HouseholdId,
            TrayItemKey = entry.TrayItemKey,
            StableInstanceIdHex = entry.StableInstanceIdHex,
            HouseholdName = entry.HouseholdName,
            HomeZoneName = entry.HomeZoneName,
            HouseholdSize = entry.HouseholdSize,
            CanExport = entry.CanExport ? 1 : 0,
            BuildState = entry.BuildState,
            LastError = entry.LastError,
            SearchText = entry.SearchText,
            DisplayTitle = entry.DisplayTitle,
            DisplaySubtitle = entry.DisplaySubtitle,
            DisplayDescription = entry.DisplayDescription,
            DisplayPrimaryMeta = entry.DisplayPrimaryMeta,
            DisplaySecondaryMeta = entry.DisplaySecondaryMeta,
            DisplayTertiaryMeta = entry.DisplayTertiaryMeta
        }).ToArray();

        if (itemRecords.Length > 0)
        {
            connection.Execute(
                """
                INSERT INTO SavePreviewDescriptorItem (
                    SourceSavePath,
                    HouseholdId,
                    TrayItemKey,
                    StableInstanceIdHex,
                    HouseholdName,
                    HomeZoneName,
                    HouseholdSize,
                    CanExport,
                    BuildState,
                    LastError,
                    SearchText,
                    DisplayTitle,
                    DisplaySubtitle,
                    DisplayDescription,
                    DisplayPrimaryMeta,
                    DisplaySecondaryMeta,
                    DisplayTertiaryMeta
                )
                VALUES (
                    @SourceSavePath,
                    @HouseholdId,
                    @TrayItemKey,
                    @StableInstanceIdHex,
                    @HouseholdName,
                    @HomeZoneName,
                    @HouseholdSize,
                    @CanExport,
                    @BuildState,
                    @LastError,
                    @SearchText,
                    @DisplayTitle,
                    @DisplaySubtitle,
                    @DisplayDescription,
                    @DisplayPrimaryMeta,
                    @DisplaySecondaryMeta,
                    @DisplayTertiaryMeta
                );
                """,
                itemRecords,
                transaction);
        }

        transaction.Commit();
    }

    public void ClearDescriptor(string saveFilePath)
    {
        var normalizedSavePath = NormalizePath(saveFilePath);
        if (string.IsNullOrWhiteSpace(normalizedSavePath))
        {
            return;
        }

        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            connection.Execute(
                "DELETE FROM SavePreviewDescriptorItem WHERE SourceSavePath = @SourceSavePath;",
                new { SourceSavePath = normalizedSavePath },
                transaction);
            connection.Execute(
                "DELETE FROM SavePreviewDescriptor WHERE SourceSavePath = @SourceSavePath;",
                new { SourceSavePath = normalizedSavePath },
                transaction);
            transaction.Commit();
        }
        catch
        {
        }
    }

    private static string GetDefaultCacheBasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimsModDesktop",
            "Cache");
    }

    private static SavePreviewDescriptorEntry MapEntry(DescriptorItemRecord record)
    {
        return new SavePreviewDescriptorEntry
        {
            HouseholdId = record.HouseholdId,
            TrayItemKey = record.TrayItemKey,
            StableInstanceIdHex = record.StableInstanceIdHex,
            HouseholdName = record.HouseholdName,
            HomeZoneName = record.HomeZoneName,
            HouseholdSize = record.HouseholdSize,
            CanExport = record.CanExport != 0,
            BuildState = record.BuildState,
            LastError = record.LastError,
            SearchText = record.SearchText,
            DisplayTitle = record.DisplayTitle,
            DisplaySubtitle = record.DisplaySubtitle,
            DisplayDescription = record.DisplayDescription,
            DisplayPrimaryMeta = record.DisplayPrimaryMeta,
            DisplaySecondaryMeta = record.DisplaySecondaryMeta,
            DisplayTertiaryMeta = record.DisplayTertiaryMeta
        };
    }

    private string NormalizePath(string? saveFilePath)
    {
        if (string.IsNullOrWhiteSpace(saveFilePath))
        {
            return string.Empty;
        }

        var resolved = _pathIdentityResolver.ResolveFile(saveFilePath);
        if (!string.IsNullOrWhiteSpace(resolved.CanonicalPath))
        {
            return resolved.CanonicalPath;
        }

        if (!string.IsNullOrWhiteSpace(resolved.FullPath))
        {
            return resolved.FullPath;
        }

        return saveFilePath.Trim().Trim('"');
    }

    private System.Data.IDbConnection OpenConnection()
    {
        var connection = _database.OpenConnection();
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS SavePreviewDescriptor (
                SourceSavePath TEXT PRIMARY KEY,
                SourceLength INTEGER NOT NULL,
                SourceLastWriteUtcTicks INTEGER NOT NULL,
                DescriptorSchemaVersion TEXT NOT NULL,
                BuildStartedUtcTicks INTEGER NOT NULL,
                BuildCompletedUtcTicks INTEGER NOT NULL,
                TotalHouseholdCount INTEGER NOT NULL,
                ExportableHouseholdCount INTEGER NOT NULL,
                ReadyHouseholdCount INTEGER NOT NULL,
                BlockedHouseholdCount INTEGER NOT NULL
            );
            """);
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS SavePreviewDescriptorItem (
                SourceSavePath TEXT NOT NULL,
                HouseholdId INTEGER NOT NULL,
                TrayItemKey TEXT NOT NULL,
                StableInstanceIdHex TEXT NOT NULL,
                HouseholdName TEXT NOT NULL,
                HomeZoneName TEXT NOT NULL,
                HouseholdSize INTEGER NOT NULL,
                CanExport INTEGER NOT NULL,
                BuildState TEXT NOT NULL,
                LastError TEXT NOT NULL,
                SearchText TEXT NOT NULL,
                DisplayTitle TEXT NOT NULL,
                DisplaySubtitle TEXT NOT NULL,
                DisplayDescription TEXT NOT NULL,
                DisplayPrimaryMeta TEXT NOT NULL,
                DisplaySecondaryMeta TEXT NOT NULL,
                DisplayTertiaryMeta TEXT NOT NULL,
                PRIMARY KEY (SourceSavePath, HouseholdId)
            );
            """);
        return connection;
    }

    private sealed class DescriptorHeaderRecord
    {
        public string SourceSavePath { get; set; } = string.Empty;
        public long SourceLength { get; set; }
        public long SourceLastWriteUtcTicks { get; set; }
        public string DescriptorSchemaVersion { get; set; } = string.Empty;
        public long BuildStartedUtcTicks { get; set; }
        public long BuildCompletedUtcTicks { get; set; }
        public int TotalHouseholdCount { get; set; }
        public int ExportableHouseholdCount { get; set; }
        public int ReadyHouseholdCount { get; set; }
        public int BlockedHouseholdCount { get; set; }
    }

    private sealed class DescriptorItemRecord
    {
        public string SourceSavePath { get; set; } = string.Empty;
        public ulong HouseholdId { get; set; }
        public string TrayItemKey { get; set; } = string.Empty;
        public string StableInstanceIdHex { get; set; } = string.Empty;
        public string HouseholdName { get; set; } = string.Empty;
        public string HomeZoneName { get; set; } = string.Empty;
        public int HouseholdSize { get; set; }
        public int CanExport { get; set; }
        public string BuildState { get; set; } = string.Empty;
        public string LastError { get; set; } = string.Empty;
        public string SearchText { get; set; } = string.Empty;
        public string DisplayTitle { get; set; } = string.Empty;
        public string DisplaySubtitle { get; set; } = string.Empty;
        public string DisplayDescription { get; set; } = string.Empty;
        public string DisplayPrimaryMeta { get; set; } = string.Empty;
        public string DisplaySecondaryMeta { get; set; } = string.Empty;
        public string DisplayTertiaryMeta { get; set; } = string.Empty;
    }
}
