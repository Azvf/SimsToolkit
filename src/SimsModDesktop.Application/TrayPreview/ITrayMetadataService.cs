using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public interface ITrayMetadataService
{
    Task<IReadOnlyDictionary<string, TrayMetadataResult>> GetMetadataAsync(
        IReadOnlyCollection<string> trayItemPaths,
        CancellationToken cancellationToken = default);
}
