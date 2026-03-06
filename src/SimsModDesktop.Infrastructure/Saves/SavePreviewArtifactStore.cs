using System.Text.Json;
using Dapper;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.Infrastructure.Persistence;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Infrastructure.Saves;

public sealed class SavePreviewArtifactStore : ISavePreviewArtifactStore
{
    private const string ArtifactSchemaVersion = "save-preview-artifact-v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AppCacheDatabase _database;
    private readonly IPathIdentityResolver _pathIdentityResolver;

    public SavePreviewArtifactStore()
        : this(GetDefaultCacheBasePath(), null)
    {
    }

    public SavePreviewArtifactStore(string cacheBasePath, IPathIdentityResolver? pathIdentityResolver = null)
    {
        _database = new AppCacheDatabase(cacheBasePath);
        _pathIdentityResolver = pathIdentityResolver ?? new SystemPathIdentityResolver();
    }

    public bool TryLoad(string saveFilePath, string householdKey, out SavePreviewArtifactRecord record)
    {
        record = null!;
        var normalizedSavePath = NormalizePath(saveFilePath);
        if (string.IsNullOrWhiteSpace(normalizedSavePath) || string.IsNullOrWhiteSpace(householdKey))
        {
            return false;
        }

        try
        {
            using var connection = OpenConnection();
            var stored = connection.QuerySingleOrDefault<ArtifactRecord>(
                """
                SELECT
                    SourceSavePath,
                    HouseholdKey,
                    SourceLength,
                    SourceLastWriteUtcTicks,
                    ArtifactSchemaVersion,
                    ArtifactRootPath,
                    GeneratedFileNamesJson,
                    LastPreparedUtcTicks
                FROM SavePreviewArtifact
                WHERE SourceSavePath = @SourceSavePath
                  AND HouseholdKey = @HouseholdKey;
                """,
                new
                {
                    SourceSavePath = normalizedSavePath,
                    HouseholdKey = householdKey
                });

            if (stored is null)
            {
                return false;
            }

            record = new SavePreviewArtifactRecord
            {
                SourceSavePath = stored.SourceSavePath,
                HouseholdKey = stored.HouseholdKey,
                SourceLength = stored.SourceLength,
                SourceLastWriteTimeUtc = new DateTime(stored.SourceLastWriteUtcTicks, DateTimeKind.Utc),
                ArtifactSchemaVersion = stored.ArtifactSchemaVersion,
                ArtifactRootPath = stored.ArtifactRootPath,
                GeneratedFileNames = JsonSerializer.Deserialize<List<string>>(stored.GeneratedFileNamesJson, JsonOptions)
                    ?? new List<string>(),
                LastPreparedUtc = new DateTime(stored.LastPreparedUtcTicks, DateTimeKind.Utc)
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Save(string saveFilePath, string householdKey, SavePreviewArtifactRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(householdKey);
        ArgumentNullException.ThrowIfNull(record);

        var normalizedSavePath = NormalizePath(record.SourceSavePath);
        if (string.IsNullOrWhiteSpace(normalizedSavePath))
        {
            normalizedSavePath = NormalizePath(saveFilePath);
        }

        using var connection = OpenConnection();
        connection.Execute(
            """
            INSERT INTO SavePreviewArtifact (
                SourceSavePath,
                HouseholdKey,
                SourceLength,
                SourceLastWriteUtcTicks,
                ArtifactSchemaVersion,
                ArtifactRootPath,
                GeneratedFileNamesJson,
                LastPreparedUtcTicks
            )
            VALUES (
                @SourceSavePath,
                @HouseholdKey,
                @SourceLength,
                @SourceLastWriteUtcTicks,
                @ArtifactSchemaVersion,
                @ArtifactRootPath,
                @GeneratedFileNamesJson,
                @LastPreparedUtcTicks
            )
            ON CONFLICT(SourceSavePath, HouseholdKey) DO UPDATE SET
                SourceLength = excluded.SourceLength,
                SourceLastWriteUtcTicks = excluded.SourceLastWriteUtcTicks,
                ArtifactSchemaVersion = excluded.ArtifactSchemaVersion,
                ArtifactRootPath = excluded.ArtifactRootPath,
                GeneratedFileNamesJson = excluded.GeneratedFileNamesJson,
                LastPreparedUtcTicks = excluded.LastPreparedUtcTicks;
            """,
            new ArtifactRecord
            {
                SourceSavePath = normalizedSavePath,
                HouseholdKey = householdKey,
                SourceLength = record.SourceLength,
                SourceLastWriteUtcTicks = record.SourceLastWriteTimeUtc.ToUniversalTime().Ticks,
                ArtifactSchemaVersion = ArtifactSchemaVersion,
                ArtifactRootPath = record.ArtifactRootPath,
                GeneratedFileNamesJson = JsonSerializer.Serialize(record.GeneratedFileNames, JsonOptions),
                LastPreparedUtcTicks = record.LastPreparedUtc.ToUniversalTime().Ticks
            });
    }

    public void Clear(string saveFilePath)
    {
        var normalizedSavePath = NormalizePath(saveFilePath);
        if (string.IsNullOrWhiteSpace(normalizedSavePath))
        {
            return;
        }

        try
        {
            using var connection = OpenConnection();
            connection.Execute(
                "DELETE FROM SavePreviewArtifact WHERE SourceSavePath = @SourceSavePath;",
                new { SourceSavePath = normalizedSavePath });
        }
        catch
        {
        }
    }

    public bool IsCurrent(string saveFilePath, string householdKey, SavePreviewArtifactRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(householdKey);
        ArgumentNullException.ThrowIfNull(record);

        var file = new FileInfo(NormalizePath(saveFilePath));
        if (!file.Exists)
        {
            return false;
        }

        if (!string.Equals(record.SourceSavePath, file.FullName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(record.HouseholdKey, householdKey, StringComparison.OrdinalIgnoreCase) ||
            record.SourceLength != file.Length ||
            record.SourceLastWriteTimeUtc != file.LastWriteTimeUtc ||
            !string.Equals(record.ArtifactSchemaVersion, ArtifactSchemaVersion, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(record.ArtifactRootPath) || !Directory.Exists(record.ArtifactRootPath))
        {
            return false;
        }

        return record.GeneratedFileNames.Count > 0 &&
               record.GeneratedFileNames.All(name =>
                   !string.IsNullOrWhiteSpace(name) &&
                   File.Exists(Path.Combine(record.ArtifactRootPath, name)));
    }

    private static string GetDefaultCacheBasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimsModDesktop",
            "Cache");
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
            CREATE TABLE IF NOT EXISTS SavePreviewArtifact (
                SourceSavePath TEXT NOT NULL,
                HouseholdKey TEXT NOT NULL,
                SourceLength INTEGER NOT NULL,
                SourceLastWriteUtcTicks INTEGER NOT NULL,
                ArtifactSchemaVersion TEXT NOT NULL,
                ArtifactRootPath TEXT NOT NULL,
                GeneratedFileNamesJson TEXT NOT NULL,
                LastPreparedUtcTicks INTEGER NOT NULL,
                PRIMARY KEY (SourceSavePath, HouseholdKey)
            );
            """);
        return connection;
    }

    private sealed class ArtifactRecord
    {
        public string SourceSavePath { get; set; } = string.Empty;
        public string HouseholdKey { get; set; } = string.Empty;
        public long SourceLength { get; set; }
        public long SourceLastWriteUtcTicks { get; set; }
        public string ArtifactSchemaVersion { get; set; } = string.Empty;
        public string ArtifactRootPath { get; set; } = string.Empty;
        public string GeneratedFileNamesJson { get; set; } = "[]";
        public long LastPreparedUtcTicks { get; set; }
    }
}
