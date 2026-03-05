using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Application.Mods;

public sealed class ModPackageScanService : IModPackageScanService
{
    private static readonly IPathIdentityResolver PathIdentityResolver = new SystemPathIdentityResolver();

    public Task<IReadOnlyList<ModPackageScanResult>> ScanAsync(
        string modsRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(modsRoot);

        var resolvedRoot = PathIdentityResolver.ResolveDirectory(modsRoot);
        var root = !string.IsNullOrWhiteSpace(resolvedRoot.CanonicalPath)
            ? resolvedRoot.CanonicalPath
            : resolvedRoot.FullPath;
        if (!Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<ModPackageScanResult>>(Array.Empty<ModPackageScanResult>());
        }

        var packages = new List<ModPackageScanResult>();
        foreach (var path in EnumeratePackageFiles(root, cancellationToken))
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(root, fileInfo.FullName);
            packages.Add(new ModPackageScanResult
            {
                PackagePath = fileInfo.FullName,
                FileLength = fileInfo.Length,
                LastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
                PackageType = ResolvePackageType(fileInfo.Name),
                ScopeHint = ResolveScope(relativePath)
            });
        }

        return Task.FromResult<IReadOnlyList<ModPackageScanResult>>(packages);
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
                if (IsReparseDirectory(child))
                {
                    continue;
                }

                pending.Push(child);
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static bool IsReparseDirectory(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch
        {
            return false;
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
}
