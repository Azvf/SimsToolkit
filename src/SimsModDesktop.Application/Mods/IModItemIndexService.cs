namespace SimsModDesktop.Application.Mods;

public interface IModItemIndexService
{
    Task<ModItemIndexBuildResult> RebuildPackageAsync(
        string packagePath,
        CancellationToken cancellationToken = default);

    Task InvalidatePackageAsync(
        string packagePath,
        CancellationToken cancellationToken = default);
}
