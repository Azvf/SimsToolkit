using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class TrayThumbnailCacheStore
{
    private const int DefaultTargetWidth = 768;
    private const int DefaultTargetHeight = 576;
    private const string CacheTransformVersion = "canvas-fit-v4";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private readonly string _cacheRootPath;
    private readonly string _manifestPath;
    private readonly int _targetWidth;
    private readonly int _targetHeight;
    private readonly string _cacheProfile;

    private bool _manifestLoaded;
    private Dictionary<string, TrayThumbnailManifestEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public TrayThumbnailCacheStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimsModDesktop",
                "Cache",
                "TrayPreviewThumbnails"),
            DefaultTargetWidth,
            DefaultTargetHeight)
    {
    }

    internal TrayThumbnailCacheStore(
        string cacheRootPath,
        int targetWidth = DefaultTargetWidth,
        int targetHeight = DefaultTargetHeight)
    {
        _cacheRootPath = cacheRootPath;
        _manifestPath = Path.Combine(_cacheRootPath, "manifest.json");
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
            PersistManifestLocked();
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
            PersistManifestLocked();
        }

        return Task.CompletedTask;
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

        if (!File.Exists(_manifestPath))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(_manifestPath);
            var payload = JsonSerializer.Deserialize<TrayThumbnailManifestPayload>(stream, JsonOptions);
            if (payload?.Entries is null)
            {
                return;
            }

            foreach (var entry in payload.Entries)
            {
                _entries[BuildEntryKey(entry.TrayRootPath, entry.TrayItemKey)] = entry;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }
    }

    private void PersistManifestLocked()
    {
        Directory.CreateDirectory(_cacheRootPath);

        var payload = new TrayThumbnailManifestPayload
        {
            Entries = _entries.Values
                .OrderBy(entry => entry.TrayRootPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.TrayItemKey, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        using var stream = new FileStream(_manifestPath, FileMode.Create, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, payload, JsonOptions);
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

    private sealed class TrayThumbnailManifestPayload
    {
        public List<TrayThumbnailManifestEntry> Entries { get; init; } = new();
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
}
