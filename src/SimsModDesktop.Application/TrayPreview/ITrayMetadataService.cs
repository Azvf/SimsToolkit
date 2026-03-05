
namespace SimsModDesktop.Application.TrayPreview;

public interface ITrayMetadataService
{
    Task<IReadOnlyDictionary<string, TrayMetadataResult>> GetMetadataAsync(
        IReadOnlyCollection<string> trayItemPaths,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, TrayMetadataResult>> GetMetadataAsync(
        TrayMetadataBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return GetMetadataAsync(request.TrayItemPaths, cancellationToken);
    }
}

public sealed record TrayMetadataBatchRequest
{
    public IReadOnlyCollection<string> TrayItemPaths { get; init; } = Array.Empty<string>();
    public int? WorkerCount { get; init; }
    public bool PreferCacheOnly { get; init; }
}
