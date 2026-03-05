namespace SimsModDesktop.Application.Services;

public enum AppCacheMaintenanceMode
{
    Light = 0,
    Standard = 1,
    Deep = 2
}

public interface IAppCacheMaintenanceService
{
    Task<AppCacheMaintenanceResult> ClearAsync(CancellationToken cancellationToken = default);

    Task<AppCacheMaintenanceResult> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        return ClearAsync(cancellationToken);
    }

    Task<AppCacheMaintenanceResult> MaintainAsync(
        AppCacheMaintenanceMode mode = AppCacheMaintenanceMode.Light,
        bool includePackageIndexCache = true,
        CancellationToken cancellationToken = default);
}

public sealed record AppCacheMaintenanceResult
{
    public bool Success { get; init; }
    public int RemovedDirectoryCount { get; init; }
    public int RemovedFileCount { get; init; }
    public AppCacheMaintenanceMode? MaintenanceMode { get; init; }
    public int MaintainedDatabaseCount { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public IReadOnlyList<AppCacheDatabaseMaintenanceDetail> DatabaseDetails { get; init; } =
        Array.Empty<AppCacheDatabaseMaintenanceDetail>();
    public string Message { get; init; } = string.Empty;
}

public sealed record AppCacheDatabaseMaintenanceDetail
{
    public string DatabasePath { get; init; } = string.Empty;
    public bool Success { get; init; }
    public long DatabaseBytesBefore { get; init; }
    public long DatabaseBytesAfter { get; init; }
    public long WalBytesBefore { get; init; }
    public long WalBytesAfter { get; init; }
    public string Message { get; init; } = string.Empty;
}
