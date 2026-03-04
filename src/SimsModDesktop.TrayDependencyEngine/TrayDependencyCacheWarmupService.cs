namespace SimsModDesktop.TrayDependencyEngine;

public sealed class TrayDependencyCacheWarmupService : ITrayDependencyCacheWarmupService
{
    private readonly IPackageIndexCache _packageIndexCache;
    private readonly object _sync = new();
    private readonly HashSet<string> _attemptedRoots = new(StringComparer.OrdinalIgnoreCase);

    public TrayDependencyCacheWarmupService(IPackageIndexCache packageIndexCache)
    {
        _packageIndexCache = packageIndexCache;
    }

    public async Task<TrayDependencyCacheWarmupResult> WarmupIfMissingAsync(
        string modsRootPath,
        IProgress<TrayDependencyExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modsRootPath))
        {
            return new TrayDependencyCacheWarmupResult
            {
                Skipped = true,
                Message = "Startup warmup skipped: Mods path is empty."
            };
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(modsRootPath.Trim());
        }
        catch
        {
            return new TrayDependencyCacheWarmupResult
            {
                Skipped = true,
                Message = "Startup warmup skipped: Mods path is invalid."
            };
        }

        if (!Directory.Exists(normalizedRoot))
        {
            return new TrayDependencyCacheWarmupResult
            {
                Skipped = true,
                Message = "Startup warmup skipped: Mods path does not exist."
            };
        }

        lock (_sync)
        {
            if (!_attemptedRoots.Add(normalizedRoot))
            {
                return new TrayDependencyCacheWarmupResult
                {
                    Skipped = true,
                    Message = "Startup warmup skipped: this Mods path is already queued."
                };
            }
        }

        progress?.Report(new TrayDependencyExportProgress
        {
            Stage = TrayDependencyExportStage.Preparing,
            Percent = 0,
            Detail = "Checking package index cache..."
        });

        if (_packageIndexCache is PackageIndexCache concreteCache &&
            await concreteCache.HasPersistedSnapshotAsync(normalizedRoot, cancellationToken).ConfigureAwait(false))
        {
            progress?.Report(new TrayDependencyExportProgress
            {
                Stage = TrayDependencyExportStage.Completed,
                Percent = 100,
                Detail = "Using existing package index cache."
            });
            return new TrayDependencyCacheWarmupResult
            {
                Skipped = true,
                Message = "Startup warmup skipped: existing package index cache was found."
            };
        }

        // Run snapshot building on a worker thread to avoid blocking UI startup.
        var snapshot = await Task.Run(
                () => _packageIndexCache.GetSnapshotAsync(normalizedRoot, progress, cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new TrayDependencyExportProgress
        {
            Stage = TrayDependencyExportStage.Completed,
            Percent = 100,
            Detail = $"Startup package index cache is ready ({snapshot.Packages.Count}/{snapshot.Packages.Count})."
        });
        return new TrayDependencyCacheWarmupResult
        {
            WarmedUp = true,
            PackageCount = snapshot.Packages.Count,
            Message = $"Startup warmup completed for {snapshot.Packages.Count} package(s)."
        };
    }
}
