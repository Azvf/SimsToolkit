using SimsModDesktop.Application.Models;

namespace SimsModDesktop.Application.Preview;

public interface IPreviewMetadataFacade
{
    Task<IReadOnlyDictionary<string, TrayMetadataResult>> GetMetadataAsync(
        IReadOnlyCollection<string> trayItemPaths,
        CancellationToken cancellationToken = default);
}
