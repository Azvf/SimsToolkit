namespace SimsModDesktop.Infrastructure.Services;

public sealed class AppCacheMaintenanceService : IAppCacheMaintenanceService
{
    private readonly string _cacheRootPath;

    public AppCacheMaintenanceService()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimsModDesktop",
                "Cache"))
    {
    }

    public AppCacheMaintenanceService(string cacheRootPath)
    {
        _cacheRootPath = cacheRootPath;
    }

    public Task<AppCacheMaintenanceResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        return ClearInternalAsync(includePackageIndexCache: false, cancellationToken);
    }

    public Task<AppCacheMaintenanceResult> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        return ClearInternalAsync(includePackageIndexCache: true, cancellationToken);
    }

    private Task<AppCacheMaintenanceResult> ClearInternalAsync(bool includePackageIndexCache, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var removedDirectoryCount = 0;
            var removedFileCount = 0;
            var directoryTargets = new[]
            {
                Path.Combine(_cacheRootPath, "SavePreview"),
                Path.Combine(_cacheRootPath, "TrayPreviewThumbnails", "thumbs")
            };
            var fileTargets = new[]
            {
                Path.Combine(_cacheRootPath, "app-cache.db"),
                Path.Combine(_cacheRootPath, "app-cache.db-wal"),
                Path.Combine(_cacheRootPath, "app-cache.db-shm")
            }.ToList();

            if (includePackageIndexCache)
            {
                fileTargets.Add(Path.Combine(_cacheRootPath, "TrayDependencyPackageIndex", "cache.db"));
                fileTargets.Add(Path.Combine(_cacheRootPath, "TrayDependencyPackageIndex", "cache.db-wal"));
                fileTargets.Add(Path.Combine(_cacheRootPath, "TrayDependencyPackageIndex", "cache.db-shm"));
            }

            try
            {
                foreach (var path in fileTargets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    File.Delete(path);
                    removedFileCount++;
                }

                foreach (var path in directoryTargets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Directory.Exists(path))
                    {
                        continue;
                    }

                    Directory.Delete(path, recursive: true);
                    removedDirectoryCount++;
                }

                if (Directory.Exists(_cacheRootPath) &&
                    !Directory.EnumerateFileSystemEntries(_cacheRootPath).Any())
                {
                    Directory.Delete(_cacheRootPath, recursive: false);
                }

                var removedTargetCount = removedDirectoryCount + removedFileCount;
                var message = removedTargetCount > 0
                    ? $"Cleared {removedDirectoryCount} cache folder(s) and {removedFileCount} cache file(s). Restart the app to drop in-memory caches."
                    : "No disk cache files were present. Restart the app to drop in-memory caches.";

                return new AppCacheMaintenanceResult
                {
                    Success = true,
                    RemovedDirectoryCount = removedDirectoryCount,
                    Message = message
                };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new AppCacheMaintenanceResult
                {
                    Success = false,
                    RemovedDirectoryCount = removedDirectoryCount,
                    Message = $"Failed to clear cache: {ex.Message}"
                };
            }
        }, cancellationToken);
    }
}
