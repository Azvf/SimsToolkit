namespace SimsModDesktop.Application.Mods;

public interface IFastModItemIndexService
{
    Task<ModItemFastIndexBuildResult> BuildFastPackageAsync(
        string packagePath,
        CancellationToken cancellationToken = default);
}
