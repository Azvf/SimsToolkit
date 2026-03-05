using SimsModDesktop.PackageCore;

namespace SimsModDesktop.SaveData.Services;

public interface ISaveAppearanceLinkService
{
    Task<Ts4SimAppearanceSnapshot> BuildSnapshotAsync(
        string savePath,
        string gameRoot,
        string modsRoot,
        CancellationToken cancellationToken = default);
}
