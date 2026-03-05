using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using SimsModDesktop.Infrastructure.Persistence;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Infrastructure.Saves;

public sealed class SavePreviewCacheStore : ISavePreviewCacheStore
{
    private const string CacheSchemaVersion = "save-preview-v2";
    private static readonly IPathIdentityResolver PathIdentityResolver = new SystemPathIdentityResolver();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _cacheRootPath;
    private readonly AppCacheDatabase _database;

    public SavePreviewCacheStore()
        : this(GetDefaultCacheBasePath())
    {
    }

    public SavePreviewCacheStore(string cacheBasePath)
    {
        _cacheRootPath = Path.Combine(cacheBasePath, "SavePreview");
        _database = new AppCacheDatabase(cacheBasePath);
    }

    private static string GetDefaultCacheBasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimsModDesktop",
            "Cache");
    }

    public string GetCacheRootPath(string saveFilePath)
    {
        var normalized = NormalizePath(saveFilePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return Path.Combine(_cacheRootPath, ComputeShortHash(normalized));
    }

    public bool IsCurrent(string saveFilePath, SavePreviewCacheManifest manifest)
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
               string.Equals(manifest.CacheSchemaVersion, CacheSchemaVersion, StringComparison.Ordinal);
    }

    public bool TryLoad(string saveFilePath, out SavePreviewCacheManifest manifest)
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
            var record = connection.QuerySingleOrDefault<SavePreviewCacheRecord>(
                """
                SELECT
                    SourceSavePath,
                    SourceLength,
                    SourceLastWriteUtcTicks,
                    CacheSchemaVersion,
                    BuildStartedUtcTicks,
                    BuildCompletedUtcTicks,
                    TotalHouseholdCount,
                    ExportableHouseholdCount,
                    ReadyHouseholdCount,
                    FailedHouseholdCount,
                    BlockedHouseholdCount,
                    EntriesJson
                FROM SavePreviewCache
                WHERE SourceSavePath = @SourceSavePath;
                """,
                new { SourceSavePath = normalizedSavePath });

            if (record is null)
            {
                return false;
            }

            manifest = new SavePreviewCacheManifest
            {
                SourceSavePath = record.SourceSavePath,
                SourceLength = record.SourceLength,
                SourceLastWriteTimeUtc = new DateTime(record.SourceLastWriteUtcTicks, DateTimeKind.Utc),
                CacheSchemaVersion = record.CacheSchemaVersion,
                BuildStartedUtc = new DateTime(record.BuildStartedUtcTicks, DateTimeKind.Utc),
                BuildCompletedUtc = new DateTime(record.BuildCompletedUtcTicks, DateTimeKind.Utc),
                TotalHouseholdCount = record.TotalHouseholdCount,
                ExportableHouseholdCount = record.ExportableHouseholdCount,
                ReadyHouseholdCount = record.ReadyHouseholdCount,
                FailedHouseholdCount = record.FailedHouseholdCount,
                BlockedHouseholdCount = record.BlockedHouseholdCount,
                Entries = JsonSerializer.Deserialize<List<SavePreviewCacheHouseholdEntry>>(record.EntriesJson, JsonOptions)
                    ?? new List<SavePreviewCacheHouseholdEntry>()
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Save(string saveFilePath, SavePreviewCacheManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentNullException.ThrowIfNull(manifest);

        var cacheRoot = GetCacheRootPath(saveFilePath);
        Directory.CreateDirectory(cacheRoot);
        var normalizedSavePath = NormalizePath(manifest.SourceSavePath);
        if (string.IsNullOrWhiteSpace(normalizedSavePath))
        {
            normalizedSavePath = NormalizePath(saveFilePath);
        }

        using var connection = OpenConnection();
        connection.Execute(
            """
            INSERT INTO SavePreviewCache (
                SourceSavePath,
                SourceLength,
                SourceLastWriteUtcTicks,
                CacheSchemaVersion,
                BuildStartedUtcTicks,
                BuildCompletedUtcTicks,
                TotalHouseholdCount,
                ExportableHouseholdCount,
                ReadyHouseholdCount,
                FailedHouseholdCount,
                BlockedHouseholdCount,
                EntriesJson
            )
            VALUES (
                @SourceSavePath,
                @SourceLength,
                @SourceLastWriteUtcTicks,
                @CacheSchemaVersion,
                @BuildStartedUtcTicks,
                @BuildCompletedUtcTicks,
                @TotalHouseholdCount,
                @ExportableHouseholdCount,
                @ReadyHouseholdCount,
                @FailedHouseholdCount,
                @BlockedHouseholdCount,
                @EntriesJson
            )
            ON CONFLICT(SourceSavePath) DO UPDATE SET
                SourceLength = excluded.SourceLength,
                SourceLastWriteUtcTicks = excluded.SourceLastWriteUtcTicks,
                CacheSchemaVersion = excluded.CacheSchemaVersion,
                BuildStartedUtcTicks = excluded.BuildStartedUtcTicks,
                BuildCompletedUtcTicks = excluded.BuildCompletedUtcTicks,
                TotalHouseholdCount = excluded.TotalHouseholdCount,
                ExportableHouseholdCount = excluded.ExportableHouseholdCount,
                ReadyHouseholdCount = excluded.ReadyHouseholdCount,
                FailedHouseholdCount = excluded.FailedHouseholdCount,
                BlockedHouseholdCount = excluded.BlockedHouseholdCount,
                EntriesJson = excluded.EntriesJson;
            """,
            new SavePreviewCacheRecord
            {
                SourceSavePath = normalizedSavePath,
                SourceLength = manifest.SourceLength,
                SourceLastWriteUtcTicks = manifest.SourceLastWriteTimeUtc.ToUniversalTime().Ticks,
                CacheSchemaVersion = CacheSchemaVersion,
                BuildStartedUtcTicks = manifest.BuildStartedUtc.ToUniversalTime().Ticks,
                BuildCompletedUtcTicks = manifest.BuildCompletedUtc.ToUniversalTime().Ticks,
                TotalHouseholdCount = manifest.TotalHouseholdCount,
                ExportableHouseholdCount = manifest.ExportableHouseholdCount,
                ReadyHouseholdCount = manifest.ReadyHouseholdCount,
                FailedHouseholdCount = manifest.FailedHouseholdCount,
                BlockedHouseholdCount = manifest.BlockedHouseholdCount,
                EntriesJson = JsonSerializer.Serialize(manifest.Entries, JsonOptions)
            });
    }

    public void Clear(string saveFilePath)
    {
        var normalizedSavePath = NormalizePath(saveFilePath);
        if (!string.IsNullOrWhiteSpace(normalizedSavePath))
        {
            try
            {
                using var connection = OpenConnection();
                connection.Execute(
                    "DELETE FROM SavePreviewCache WHERE SourceSavePath = @SourceSavePath;",
                    new { SourceSavePath = normalizedSavePath });
            }
            catch
            {
            }
        }

        var cacheRoot = GetCacheRootPath(saveFilePath);
        if (string.IsNullOrWhiteSpace(cacheRoot) || !Directory.Exists(cacheRoot))
        {
            return;
        }

        try
        {
            Directory.Delete(cacheRoot, recursive: true);
        }
        catch
        {
        }
    }

    private static string NormalizePath(string? saveFilePath)
    {
        if (string.IsNullOrWhiteSpace(saveFilePath))
        {
            return string.Empty;
        }

        var resolved = PathIdentityResolver.ResolveFile(saveFilePath);
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

    private static string ComputeShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16];
    }

    private System.Data.IDbConnection OpenConnection()
    {
        var connection = _database.OpenConnection();
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS SavePreviewCache (
                SourceSavePath TEXT PRIMARY KEY,
                SourceLength INTEGER NOT NULL,
                SourceLastWriteUtcTicks INTEGER NOT NULL,
                CacheSchemaVersion TEXT NOT NULL,
                BuildStartedUtcTicks INTEGER NOT NULL,
                BuildCompletedUtcTicks INTEGER NOT NULL,
                TotalHouseholdCount INTEGER NOT NULL,
                ExportableHouseholdCount INTEGER NOT NULL,
                ReadyHouseholdCount INTEGER NOT NULL,
                FailedHouseholdCount INTEGER NOT NULL,
                BlockedHouseholdCount INTEGER NOT NULL,
                EntriesJson TEXT NOT NULL
            );
            """);
        return connection;
    }

    private sealed class SavePreviewCacheRecord
    {
        public string SourceSavePath { get; set; } = string.Empty;
        public long SourceLength { get; set; }
        public long SourceLastWriteUtcTicks { get; set; }
        public string CacheSchemaVersion { get; set; } = string.Empty;
        public long BuildStartedUtcTicks { get; set; }
        public long BuildCompletedUtcTicks { get; set; }
        public int TotalHouseholdCount { get; set; }
        public int ExportableHouseholdCount { get; set; }
        public int ReadyHouseholdCount { get; set; }
        public int FailedHouseholdCount { get; set; }
        public int BlockedHouseholdCount { get; set; }
        public string EntriesJson { get; set; } = "[]";
    }
}
