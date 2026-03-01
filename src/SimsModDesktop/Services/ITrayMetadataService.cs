using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public interface ITrayMetadataService
{
    Task<IReadOnlyDictionary<string, TrayMetadataResult>> GetMetadataAsync(
        IReadOnlyCollection<string> trayItemPaths,
        CancellationToken cancellationToken = default);
}

internal sealed class NullTrayMetadataService : ITrayMetadataService
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
}
