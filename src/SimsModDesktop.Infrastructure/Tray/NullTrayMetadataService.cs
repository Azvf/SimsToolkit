
namespace SimsModDesktop.Infrastructure.Tray;

public sealed class NullTrayMetadataService : ITrayMetadataService
{
    public static NullTrayMetadataService Instance { get; } = new();

    private NullTrayMetadataService()
    {
    }

    public Task<IReadOnlyDictionary<string, TrayMetadataResult>> GetMetadataAsync(
        IReadOnlyCollection<string> trayItemPaths,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<string, TrayMetadataResult>>(
            new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase));
    }

    public Task<IReadOnlyDictionary<string, TrayMetadataResult>> GetMetadataAsync(
        TrayMetadataBatchRequest request,
        CancellationToken cancellationToken = default)
    {
        return GetMetadataAsync(request.TrayItemPaths, cancellationToken);
    }
}
