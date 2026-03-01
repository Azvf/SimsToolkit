using System.Globalization;

namespace SimsModDesktop.Services;

public sealed class LocalthumbcacheThumbnailReader
{
    private readonly object _cacheGate = new();

    private string _cachedPackagePath = string.Empty;
    private DateTime _cachedLastWriteUtc = DateTime.MinValue;
    private byte[]? _cachedPackageBytes;

    internal ExtractedTrayImage? TryExtractBestImage(
        string trayRootPath,
        string trayInstanceId,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseInstancePatterns(trayInstanceId, out var patterns))
        {
            return null;
        }

        var packagePath = ResolveLocalthumbcachePath(trayRootPath);
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            return null;
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
        patterns = new List<byte[]>();

        if (string.IsNullOrWhiteSpace(trayInstanceId))
        {
            return false;
        }

        var raw = trayInstanceId.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw[2..];
        }

        if (!ulong.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
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
}
