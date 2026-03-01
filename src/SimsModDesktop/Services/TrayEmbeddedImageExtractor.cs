using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class TrayEmbeddedImageExtractor
{
    private static readonly string[] PreferredExtensions =
    [
        ".bpi",
        ".rmi",
        ".hhi",
        ".sgi",
        ".trayitem"
    ];

    internal ExtractedTrayImage? TryExtractBestImage(
        SimsTrayPreviewItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        foreach (var candidatePath in EnumerateCandidatePaths(item))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var image = TryExtractBestImage(candidatePath, cancellationToken);
            if (image is not null)
            {
                return image;
            }
        }

        return null;
    }

    internal ExtractedTrayImage? TryExtractBestImage(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var bytes = File.ReadAllBytes(filePath);
            return TrayImagePayloadScanner.TryExtractBestImage(bytes);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateCandidatePaths(SimsTrayPreviewItem item)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in item.SourceFilePaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Where(File.Exists)
                     .OrderBy(GetPriority)
                     .ThenBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Add(path))
            {
                yield return path;
            }
        }

        var trayItemPath = ResolveTrayItemPath(item);
        if (!string.IsNullOrWhiteSpace(trayItemPath) && seen.Add(trayItemPath))
        {
            yield return trayItemPath;
        }
    }

    private static int GetPriority(string path)
    {
        var extension = Path.GetExtension(path);
        for (var i = 0; i < PreferredExtensions.Length; i++)
        {
            if (string.Equals(extension, PreferredExtensions[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return PreferredExtensions.Length;
    }

    private static string ResolveTrayItemPath(SimsTrayPreviewItem item)
    {
        var trayItemPath = item.SourceFilePaths
            .FirstOrDefault(path => string.Equals(
                Path.GetExtension(path),
                ".trayitem",
                StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(trayItemPath) && File.Exists(trayItemPath))
        {
            return trayItemPath;
        }

        if (string.IsNullOrWhiteSpace(item.TrayRootPath) || string.IsNullOrWhiteSpace(item.TrayItemKey))
        {
            return string.Empty;
        }

        var candidate = Path.Combine(item.TrayRootPath, item.TrayItemKey + ".trayitem");
        return File.Exists(candidate) ? candidate : string.Empty;
    }
}
