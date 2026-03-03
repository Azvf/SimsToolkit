namespace SimsModDesktop.Application.Services;

public interface IAppCacheMaintenanceService
{
    Task<AppCacheMaintenanceResult> ClearAsync(CancellationToken cancellationToken = default);

    Task<AppCacheMaintenanceResult> ClearAllAsync(CancellationToken cancellationToken = default)
    {
        return ClearAsync(cancellationToken);
    }
}

public sealed record AppCacheMaintenanceResult
{
    public bool Success { get; init; }
    public int RemovedDirectoryCount { get; init; }
    public string Message { get; init; } = string.Empty;
}
