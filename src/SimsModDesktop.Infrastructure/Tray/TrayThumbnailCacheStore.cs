using System.Security.Cryptography;
using System.Text;
using Dapper;
using SimsModDesktop.Infrastructure.Persistence;
using SimsModDesktop.Models;

namespace SimsModDesktop.Infrastructure.Tray;

public sealed class TrayThumbnailCacheStore
{
    private const int DefaultTargetWidth = 768;
    private const int DefaultTargetHeight = 576;
    private const string CacheTransformVersion = "canvas-fit-v5";

    private readonly object _gate = new();
    private readonly string _cacheRootPath;
    private readonly AppCacheDatabase _database;
    private readonly int _targetWidth;
    private readonly int _targetHeight;
    private readonly string _cacheProfile;

    private bool _manifestLoaded;
    private Dictionary<string, TrayThumbnailManifestEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public TrayThumbnailCacheStore()
        : this(
            GetDefaultCacheBasePath(),
            DefaultTargetWidth,
            DefaultTargetHeight)
    {
    }

    public TrayThumbnailCacheStore(
        string cacheRootPath,
        int targetWidth = DefaultTargetWidth,
        int targetHeight = DefaultTargetHeight)
    {
        _cacheRootPath = Path.Combine(cacheRootPath, "TrayPreviewThumbnails");
        _database = new AppCacheDatabase(cacheRootPath);
        _targetWidth = Math.Max(targetWidth, 1);
        _targetHeight = Math.Max(targetHeight, 1);
        _cacheProfile = BuildCacheProfile(_targetWidth, _targetHeight);
    }

    public bool TryGetValidEntry(SimsTrayPreviewItem item, out TrayThumbnailCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(item);

        lock (_gate)
        {
            EnsureManifestLoadedLocked();

            if (!_entries.TryGetValue(BuildEntryKey(item.TrayRootPath, item.TrayItemKey), out var stored))
            {
                entry = null!;
                return false;
            }

            if (!string.Equals(stored.ContentFingerprint, item.ContentFingerprint, StringComparison.Ordinal) ||
                !string.Equals(stored.CacheProfile, _cacheProfile, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(stored.CacheFilePath) ||
                !File.Exists(stored.CacheFilePath))
            {
                entry = null!;
                return false;
            }

            stored.LastSeenUtc = DateTime.UtcNow;
            entry = ToCacheEntry(stored);
            return true;
        }
    }

    public Task<TrayThumbnailCacheEntry?> StoreAsync(
        SimsTrayPreviewItem item,
        ReadOnlyMemory<byte> sourceImageData,
        TrayThumbnailSourceKind sourceKind,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        cancellationToken.ThrowIfCancellationRequested();

        if (!TrayImageCodec.TryTranscodeToCanvasPng(
                sourceImageData.Span,
                _targetWidth,
                _targetHeight,
                out var pngBytes,
                out var sourceWidth,
                out var sourceHeight))
        {
            return Task.FromResult<TrayThumbnailCacheEntry?>(null);
        }

        lock (_gate)
        {
            EnsureManifestLoadedLocked();

            var normalizedTrayRoot = NormalizeTrayRoot(item.TrayRootPath);
            var trayRootHash = ComputeShortHash(normalizedTrayRoot);
            var thumbDirectory = Path.Combine(_cacheRootPath, "thumbs", trayRootHash);
            Directory.CreateDirectory(thumbDirectory);

            var fileName = $"{SanitizeFileName(item.TrayItemKey)}_{TrimFingerprint(item.ContentFingerprint)}_{_cacheProfile}.png";
            var cacheFilePath = Path.Combine(thumbDirectory, fileName);
            File.WriteAllBytes(cacheFilePath, pngBytes);

            var entryKey = BuildEntryKey(item.TrayRootPath, item.TrayItemKey);
            if (_entries.TryGetValue(entryKey, out var existing) &&
                !string.IsNullOrWhiteSpace(existing.CacheFilePath) &&
                !existing.CacheFilePath.Equals(cacheFilePath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(existing.CacheFilePath))
            {
                TryDeleteFile(existing.CacheFilePath);
            }

            var stored = new TrayThumbnailManifestEntry
            {
                TrayRootPath = normalizedTrayRoot,
                TrayRootHash = trayRootHash,
                TrayItemKey = item.TrayItemKey,
                TrayInstanceId = item.TrayInstanceId,
                ContentFingerprint = item.ContentFingerprint,
                CacheFilePath = cacheFilePath,
                SourceKind = sourceKind,
                Width = sourceWidth,
                Height = sourceHeight,
                CacheProfile = _cacheProfile,
                LastSeenUtc = DateTime.UtcNow
            };

            _entries[entryKey] = stored;
            UpsertEntryLocked(stored);
            return Task.FromResult<TrayThumbnailCacheEntry?>(ToCacheEntry(stored));
        }
    }

    public Task CleanupStaleEntriesAsync(
        string trayRootPath,
        IReadOnlyCollection<string> liveItemKeys,
        CancellationToken cancellationToken = default)
    {
        var normalizedTrayRoot = NormalizeTrayRoot(trayRootPath);
        var liveKeys = new HashSet<string>(liveItemKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        lock (_gate)
        {
            EnsureManifestLoadedLocked();

            var staleEntries = _entries.Values
                .Where(entry => entry.TrayRootPath.Equals(normalizedTrayRoot, StringComparison.OrdinalIgnoreCase) &&
                                !liveKeys.Contains(entry.TrayItemKey))
                .ToList();

            foreach (var stale in staleEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _entries.Remove(BuildEntryKey(stale.TrayRootPath, stale.TrayItemKey));
                if (!string.IsNullOrWhiteSpace(stale.CacheFilePath))
                {
                    TryDeleteFile(stale.CacheFilePath);
                }
            }

            CleanupOrphanFilesLocked(normalizedTrayRoot);
            DeleteEntriesLocked(staleEntries);
        }

        return Task.CompletedTask;
    }

    public void ResetMemoryCache(string? trayRootPath = null)
    {
        lock (_gate)
        {
            if (!_manifestLoaded)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(trayRootPath))
            {
                _entries.Clear();
                _manifestLoaded = false;
                return;
            }

            var normalizedTrayRoot = NormalizeTrayRoot(trayRootPath);
            var staleKeys = _entries
                .Where(pair => pair.Value.TrayRootPath.Equals(normalizedTrayRoot, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToArray();

            foreach (var key in staleKeys)
            {
                _entries.Remove(key);
            }
        }
    }

    private void CleanupOrphanFilesLocked(string normalizedTrayRoot)
    {
        var trayRootHash = ComputeShortHash(normalizedTrayRoot);
        var thumbDirectory = Path.Combine(_cacheRootPath, "thumbs", trayRootHash);
        if (!Directory.Exists(thumbDirectory))
        {
            return;
        }

        var livePaths = _entries.Values
            .Where(entry => entry.TrayRootHash.Equals(trayRootHash, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.CacheFilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in Directory.EnumerateFiles(thumbDirectory, "*.png", SearchOption.TopDirectoryOnly))
        {
            if (!livePaths.Contains(filePath))
            {
                TryDeleteFile(filePath);
            }
        }

        if (!Directory.EnumerateFileSystemEntries(thumbDirectory).Any())
        {
            try
            {
                Directory.Delete(thumbDirectory, recursive: false);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void EnsureManifestLoadedLocked()
    {
        if (_manifestLoaded)
        {
            return;
        }

        _manifestLoaded = true;
        _entries = new Dictionary<string, TrayThumbnailManifestEntry>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var connection = OpenConnection();
            var rows = connection.Query<TrayThumbnailRow>(
                """
                SELECT
                    TrayRootPath,
                    TrayRootHash,
                    TrayItemKey,
                    TrayInstanceId,
                    ContentFingerprint,
                    CacheFilePath,
                    SourceKind,
                    Width,
                    Height,
                    CacheProfile,
                    LastSeenUtcTicks
                FROM TrayThumbnailCache;
                """);

            foreach (var row in rows)
            {
                var entry = new TrayThumbnailManifestEntry
                {
                    TrayRootPath = row.TrayRootPath,
                    TrayRootHash = row.TrayRootHash,
                    TrayItemKey = row.TrayItemKey,
                    TrayInstanceId = row.TrayInstanceId,
                    ContentFingerprint = row.ContentFingerprint,
                    CacheFilePath = row.CacheFilePath,
                    SourceKind = row.SourceKind,
                    Width = row.Width,
                    Height = row.Height,
                    CacheProfile = row.CacheProfile,
                    LastSeenUtc = new DateTime(row.LastSeenUtcTicks, DateTimeKind.Utc)
                };
                _entries[BuildEntryKey(entry.TrayRootPath, entry.TrayItemKey)] = entry;
            }
        }
        catch
        {
        }
    }

    private void PersistManifestLocked()
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        connection.Execute("DELETE FROM TrayThumbnailCache;", transaction: transaction);
        connection.Execute(
            """
            INSERT INTO TrayThumbnailCache (
                TrayRootPath,
                TrayRootHash,
                TrayItemKey,
                TrayInstanceId,
                ContentFingerprint,
                CacheFilePath,
                SourceKind,
                Width,
                Height,
                CacheProfile,
                LastSeenUtcTicks
            )
            VALUES (
                @TrayRootPath,
                @TrayRootHash,
                @TrayItemKey,
                @TrayInstanceId,
                @ContentFingerprint,
                @CacheFilePath,
                @SourceKind,
                @Width,
                @Height,
                @CacheProfile,
                @LastSeenUtcTicks
            );
            """,
            _entries.Values
                .OrderBy(entry => entry.TrayRootPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.TrayItemKey, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new TrayThumbnailRow
                {
                    TrayRootPath = entry.TrayRootPath,
                    TrayRootHash = entry.TrayRootHash,
                    TrayItemKey = entry.TrayItemKey,
                    TrayInstanceId = entry.TrayInstanceId,
                    ContentFingerprint = entry.ContentFingerprint,
                    CacheFilePath = entry.CacheFilePath,
                    SourceKind = entry.SourceKind,
                    Width = entry.Width,
                    Height = entry.Height,
                    CacheProfile = entry.CacheProfile,
                    LastSeenUtcTicks = entry.LastSeenUtc.ToUniversalTime().Ticks
                }),
            transaction: transaction);

        transaction.Commit();
    }

    private void UpsertEntryLocked(TrayThumbnailManifestEntry entry)
    {
        using var connection = OpenConnection();
        connection.Execute(
            """
            INSERT INTO TrayThumbnailCache (
                TrayRootPath,
                TrayRootHash,
                TrayItemKey,
                TrayInstanceId,
                ContentFingerprint,
                CacheFilePath,
                SourceKind,
                Width,
                Height,
                CacheProfile,
                LastSeenUtcTicks
            )
            VALUES (
                @TrayRootPath,
                @TrayRootHash,
                @TrayItemKey,
                @TrayInstanceId,
                @ContentFingerprint,
                @CacheFilePath,
                @SourceKind,
                @Width,
                @Height,
                @CacheProfile,
                @LastSeenUtcTicks
            )
            ON CONFLICT(TrayRootPath, TrayItemKey) DO UPDATE SET
                TrayRootHash = excluded.TrayRootHash,
                TrayInstanceId = excluded.TrayInstanceId,
                ContentFingerprint = excluded.ContentFingerprint,
                CacheFilePath = excluded.CacheFilePath,
                SourceKind = excluded.SourceKind,
                Width = excluded.Width,
                Height = excluded.Height,
                CacheProfile = excluded.CacheProfile,
                LastSeenUtcTicks = excluded.LastSeenUtcTicks;
            """,
            new TrayThumbnailRow
            {
                TrayRootPath = entry.TrayRootPath,
                TrayRootHash = entry.TrayRootHash,
                TrayItemKey = entry.TrayItemKey,
                TrayInstanceId = entry.TrayInstanceId,
                ContentFingerprint = entry.ContentFingerprint,
                CacheFilePath = entry.CacheFilePath,
                SourceKind = entry.SourceKind,
                Width = entry.Width,
                Height = entry.Height,
                CacheProfile = entry.CacheProfile,
                LastSeenUtcTicks = entry.LastSeenUtc.ToUniversalTime().Ticks
            });
    }

    private void DeleteEntriesLocked(IReadOnlyList<TrayThumbnailManifestEntry> entries)
    {
        if (entries.Count == 0)
        {
            return;
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        connection.Execute(
            """
            DELETE FROM TrayThumbnailCache
            WHERE TrayRootPath = @TrayRootPath
              AND TrayItemKey = @TrayItemKey;
            """,
            entries.Select(entry => new
            {
                entry.TrayRootPath,
                entry.TrayItemKey
            }),
            transaction: transaction);
        transaction.Commit();
    }

    private static TrayThumbnailCacheEntry ToCacheEntry(TrayThumbnailManifestEntry entry)
    {
        return new TrayThumbnailCacheEntry
        {
            CacheFilePath = entry.CacheFilePath,
            SourceKind = entry.SourceKind,
            Width = entry.Width,
            Height = entry.Height
        };
    }

    private static string BuildEntryKey(string trayRootPath, string trayItemKey)
    {
        return $"{NormalizeTrayRoot(trayRootPath)}|{trayItemKey.Trim()}";
    }

    private static string NormalizeTrayRoot(string trayRootPath)
    {
        return Path.GetFullPath(trayRootPath.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "item";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Trim()
            .Select(ch => invalid.Contains(ch) || ch == '!' || ch == ':' ? '_' : ch)
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "item" : sanitized;
    }

    private static string TrimFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return "nofp";
        }

        return fingerprint.Length <= 12
            ? fingerprint
            : fingerprint[..12];
    }

    private static string ComputeShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..12];
    }

    private static string BuildCacheProfile(int targetWidth, int targetHeight)
    {
        return $"{CacheTransformVersion}-{targetWidth}x{targetHeight}";
    }

    private static string GetDefaultCacheBasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimsModDesktop",
            "Cache");
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public sealed class TrayThumbnailCacheEntry
    {
        public string CacheFilePath { get; init; } = string.Empty;
        public TrayThumbnailSourceKind SourceKind { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
    }

    private System.Data.IDbConnection OpenConnection()
    {
        var connection = _database.OpenConnection();
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS TrayThumbnailCache (
                TrayRootPath TEXT NOT NULL,
                TrayRootHash TEXT NOT NULL,
                TrayItemKey TEXT NOT NULL,
                TrayInstanceId TEXT NOT NULL,
                ContentFingerprint TEXT NOT NULL,
                CacheFilePath TEXT NOT NULL,
                SourceKind INTEGER NOT NULL,
                Width INTEGER NOT NULL,
                Height INTEGER NOT NULL,
                CacheProfile TEXT NOT NULL,
                LastSeenUtcTicks INTEGER NOT NULL,
                PRIMARY KEY (TrayRootPath, TrayItemKey)
            );
            """);
        return connection;
    }

    private sealed class TrayThumbnailManifestEntry
    {
        public string TrayRootPath { get; init; } = string.Empty;
        public string TrayRootHash { get; init; } = string.Empty;
        public string TrayItemKey { get; init; } = string.Empty;
        public string TrayInstanceId { get; init; } = string.Empty;
        public string ContentFingerprint { get; init; } = string.Empty;
        public string CacheFilePath { get; init; } = string.Empty;
        public TrayThumbnailSourceKind SourceKind { get; init; }
        public int Width { get; init; }
        public int Height { get; init; }
        public string CacheProfile { get; init; } = string.Empty;
        public DateTime LastSeenUtc { get; set; }
    }

    private sealed class TrayThumbnailRow
    {
        public string TrayRootPath { get; set; } = string.Empty;
        public string TrayRootHash { get; set; } = string.Empty;
        public string TrayItemKey { get; set; } = string.Empty;
        public string TrayInstanceId { get; set; } = string.Empty;
        public string ContentFingerprint { get; set; } = string.Empty;
        public string CacheFilePath { get; set; } = string.Empty;
        public TrayThumbnailSourceKind SourceKind { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string CacheProfile { get; set; } = string.Empty;
        public long LastSeenUtcTicks { get; set; }
    }
}
