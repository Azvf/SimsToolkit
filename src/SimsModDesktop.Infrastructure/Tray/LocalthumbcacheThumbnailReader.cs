using System.Globalization;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Infrastructure.Tray;

public sealed class LocalthumbcacheThumbnailReader
{
    private readonly object _cacheGate = new();
    private readonly IDbpfResourceReader _resourceReader;

    private string _cachedPackagePath = string.Empty;
    private DateTime _cachedLastWriteUtc = DateTime.MinValue;
    private byte[]? _cachedPackageBytes;
    private CachedPackageEntryIndex? _cachedPackageEntryIndex;

    public LocalthumbcacheThumbnailReader(IDbpfResourceReader? resourceReader = null)
    {
        _resourceReader = resourceReader ?? new DbpfResourceReader();
    }

    public ExtractedTrayImage? TryExtractBestImage(
        string trayRootPath,
        string trayInstanceId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseInstance(trayInstanceId, out var instanceValue, out var patterns))
        {
            return null;
        }

        var packagePath = ResolveLocalthumbcachePath(trayRootPath);
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            return null;
        }

        var structuredImage = TryExtractBestImageFromEntries(packagePath, instanceValue, cancellationToken);
        if (structuredImage is not null)
        {
            return structuredImage;
        }

        byte[] packageBytes;
        try
        {
            packageBytes = GetPackageBytes(packagePath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var processedOffsets = new HashSet<int>();
        var candidates = new List<ExtractedTrayImage>();

        foreach (var pattern in patterns)
        {
            foreach (var instanceOffset in FindPatternOffsets(packageBytes, pattern))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryAddIndexCandidate(packageBytes, instanceOffset, 8, 12, processedOffsets, candidates);
                TryAddIndexCandidate(packageBytes, instanceOffset, 12, 16, processedOffsets, candidates);
            }
        }

        return candidates
            .OrderByDescending(candidate => candidate.PixelArea)
            .FirstOrDefault();
    }

    private ExtractedTrayImage? TryExtractBestImageFromEntries(
        string packagePath,
        ulong trayInstanceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var cachedIndex = GetOrBuildPackageEntryIndex(packagePath);
            if (cachedIndex.PackageIndex.Entries.Length == 0 ||
                !cachedIndex.EntryIndexesByInstance.TryGetValue(trayInstanceId, out var entryIndexes) ||
                entryIndexes.Length == 0)
            {
                return null;
            }

            ExtractedTrayImage? best = null;
            using var session = _resourceReader.OpenSession(packagePath);
            for (var i = 0; i < entryIndexes.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = cachedIndex.PackageIndex.Entries[entryIndexes[i]];
                if (entry.IsDeleted || !session.TryReadBytes(entry, out var bytes, out _))
                {
                    continue;
                }

                var image = TrayImagePayloadScanner.TryExtractBestImage(bytes);
                if (image is null)
                {
                    continue;
                }

                if (best is null || image.PixelArea > best.PixelArea)
                {
                    best = image;
                }
            }

            return best;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private CachedPackageEntryIndex GetOrBuildPackageEntryIndex(string packagePath)
    {
        var normalizedPath = Path.GetFullPath(packagePath);
        var lastWriteUtc = File.GetLastWriteTimeUtc(normalizedPath);

        lock (_cacheGate)
        {
            if (_cachedPackageEntryIndex is not null &&
                string.Equals(_cachedPackageEntryIndex.PackagePath, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                _cachedPackageEntryIndex.LastWriteUtc == lastWriteUtc)
            {
                return _cachedPackageEntryIndex;
            }
        }

        var packageIndex = DbpfPackageIndexReader.ReadPackageIndex(normalizedPath);
        var entryIndexesByInstance = packageIndex.Entries
            .Select((entry, index) => new { entry, index })
            .Where(pair => !pair.entry.IsDeleted)
            .GroupBy(pair => pair.entry.Instance)
            .ToDictionary(
                group => group.Key,
                group => group.Select(pair => pair.index).ToArray());
        var built = new CachedPackageEntryIndex
        {
            PackagePath = normalizedPath,
            LastWriteUtc = lastWriteUtc,
            PackageIndex = packageIndex,
            EntryIndexesByInstance = entryIndexesByInstance
        };

        lock (_cacheGate)
        {
            if (_cachedPackageEntryIndex is not null &&
                string.Equals(_cachedPackageEntryIndex.PackagePath, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                _cachedPackageEntryIndex.LastWriteUtc == lastWriteUtc)
            {
                return _cachedPackageEntryIndex;
            }

            _cachedPackageEntryIndex = built;
            return built;
        }
    }

    internal IReadOnlyList<ExtractedTrayImage> TryEnumerateEntriesByType(
        string packagePath,
        uint type,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            return Array.Empty<ExtractedTrayImage>();
        }

        try
        {
            var package = DbpfPackageIndexReader.ReadPackageIndex(packagePath);
            if (!package.TypeBuckets.TryGetValue(type, out var bucket))
            {
                return Array.Empty<ExtractedTrayImage>();
            }

            var images = new List<ExtractedTrayImage>();
            using var session = _resourceReader.OpenSession(packagePath);
            foreach (var entryIndexes in bucket.InstanceToEntryIndexes.Values)
            {
                for (var i = 0; i < entryIndexes.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var entry = package.Entries[entryIndexes[i]];
                    if (!session.TryReadBytes(entry, out var bytes, out _))
                    {
                        continue;
                    }

                    var image = TrayImagePayloadScanner.TryExtractBestImage(bytes);
                    if (image is not null)
                    {
                        images.Add(image);
                    }
                }
            }

            return images;
        }
        catch (IOException)
        {
            return Array.Empty<ExtractedTrayImage>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<ExtractedTrayImage>();
        }
        catch (InvalidDataException)
        {
            return Array.Empty<ExtractedTrayImage>();
        }
    }

    private static void TryAddIndexCandidate(
        byte[] packageBytes,
        int instanceOffset,
        int offsetDelta,
        int sizeDelta,
        HashSet<int> processedOffsets,
        List<ExtractedTrayImage> candidates)
    {
        var offsetPosition = instanceOffset + offsetDelta;
        var sizePosition = instanceOffset + sizeDelta;
        if (offsetPosition < 0 ||
            sizePosition < 0 ||
            sizePosition + 4 > packageBytes.Length)
        {
            return;
        }

        var resourceOffset = BitConverter.ToInt32(packageBytes, offsetPosition);
        var resourceSize = BitConverter.ToInt32(packageBytes, sizePosition);
        if (resourceOffset < 0 ||
            resourceSize < 16 ||
            resourceOffset >= packageBytes.Length ||
            resourceOffset + resourceSize > packageBytes.Length ||
            !processedOffsets.Add(resourceOffset))
        {
            return;
        }

        var image = TrayImagePayloadScanner.TryExtractBestImage(
            new ReadOnlySpan<byte>(packageBytes, resourceOffset, resourceSize));
        if (image is not null)
        {
            candidates.Add(image);
        }
    }

    private byte[] GetPackageBytes(string packagePath)
    {
        var normalizedPath = Path.GetFullPath(packagePath);
        var lastWriteUtc = File.GetLastWriteTimeUtc(normalizedPath);

        lock (_cacheGate)
        {
            if (_cachedPackageBytes is not null &&
                normalizedPath.Equals(_cachedPackagePath, StringComparison.OrdinalIgnoreCase) &&
                lastWriteUtc == _cachedLastWriteUtc)
            {
                return _cachedPackageBytes;
            }

            _cachedPackageBytes = File.ReadAllBytes(normalizedPath);
            _cachedPackagePath = normalizedPath;
            _cachedLastWriteUtc = lastWriteUtc;
            return _cachedPackageBytes;
        }
    }

    private static IEnumerable<int> FindPatternOffsets(byte[] buffer, byte[] pattern)
    {
        if (buffer.Length == 0 || pattern.Length == 0 || buffer.Length < pattern.Length)
        {
            yield break;
        }

        for (var i = 0; i <= buffer.Length - pattern.Length; i++)
        {
            if (new ReadOnlySpan<byte>(buffer, i, pattern.Length).SequenceEqual(pattern))
            {
                yield return i;
            }
        }
    }

    private static bool TryParseInstancePatterns(string trayInstanceId, out List<byte[]> patterns)
    {
        return TryParseInstance(trayInstanceId, out _, out patterns);
    }

    private static bool TryParseInstance(string trayInstanceId, out ulong value, out List<byte[]> patterns)
    {
        patterns = new List<byte[]>();
        value = 0;

        if (string.IsNullOrWhiteSpace(trayInstanceId))
        {
            return false;
        }

        var raw = trayInstanceId.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[2..];
        }

        if (!ulong.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        var littleEndian = BitConverter.GetBytes(value);
        patterns.Add(littleEndian);

        var bigEndian = littleEndian.Reverse().ToArray();
        if (!bigEndian.SequenceEqual(littleEndian))
        {
            patterns.Add(bigEndian);
        }

        return true;
    }

    private static string ResolveLocalthumbcachePath(string trayRootPath)
    {
        if (!string.IsNullOrWhiteSpace(trayRootPath))
        {
            var trayDirectory = new DirectoryInfo(Path.GetFullPath(trayRootPath));
            var simsRoot = string.Equals(trayDirectory.Name, "Tray", StringComparison.OrdinalIgnoreCase) &&
                           trayDirectory.Parent is not null
                ? trayDirectory.Parent.FullName
                : trayDirectory.FullName;
            var siblingPath = Path.Combine(simsRoot, "localthumbcache.package");
            if (File.Exists(siblingPath))
            {
                return siblingPath;
            }
        }

        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documentsPath, "Electronic Arts", "The Sims 4", "localthumbcache.package");
    }

    private sealed class CachedPackageEntryIndex
    {
        public string PackagePath { get; init; } = string.Empty;
        public DateTime LastWriteUtc { get; init; }
        public DbpfPackageIndex PackageIndex { get; init; } = null!;
        public IReadOnlyDictionary<ulong, int[]> EntryIndexesByInstance { get; init; } =
            new Dictionary<ulong, int[]>();
    }
}
