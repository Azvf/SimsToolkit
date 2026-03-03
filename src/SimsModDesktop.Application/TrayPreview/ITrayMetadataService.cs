using SimsModDesktop.Models;

namespace SimsModDesktop.Application.TrayPreview;

public interface ITrayMetadataService
{
    Task<IReadOnlyDictionary<string, TrayMetadataResult>> GetMetadataAsync(
        IReadOnlyCollection<string> trayItemPaths,
        CancellationToken cancellationToken = default);
}
