namespace SimsModDesktop.Services;

public interface IAppCacheMaintenanceService
{
    Task<AppCacheMaintenanceResult> ClearAsync(CancellationToken cancellationToken = default);
}

public sealed record AppCacheMaintenanceResult
{
    public bool Success { get; init; }
    public int RemovedDirectoryCount { get; init; }
    public string Message { get; init; } = string.Empty;
}

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

    internal AppCacheMaintenanceService(string cacheRootPath)
    {
        _cacheRootPath = cacheRootPath;
    }

    public Task<AppCacheMaintenanceResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var removedDirectoryCount = 0;
        var targets = new[]
        {
            Path.Combine(_cacheRootPath, "TrayPreviewThumbnails"),
            Path.Combine(_cacheRootPath, "TrayMetadataIndex")
        };

        try
        {
            foreach (var path in targets)
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

            var message = removedDirectoryCount > 0
                ? $"Cleared {removedDirectoryCount} cache folder(s). Restart the app to drop in-memory caches."
                : "No disk cache folders were present. Restart the app to drop in-memory caches.";

            return Task.FromResult(new AppCacheMaintenanceResult
            {
                Success = true,
                RemovedDirectoryCount = removedDirectoryCount,
                Message = message
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(new AppCacheMaintenanceResult
            {
                Success = false,
                RemovedDirectoryCount = removedDirectoryCount,
                Message = $"Failed to clear cache: {ex.Message}"
            });
        }
    }
}
