using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Application.Mods;

internal sealed class ModPackageParseContext
{
    public required string PackagePath { get; init; }
    public required string FileName { get; init; }
    public required long FileLength { get; init; }
    public required long LastWriteUtcTicks { get; init; }
    public required DbpfPackageIndex PackageIndex { get; init; }

    public static ModPackageParseContext Create(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        var fullPath = Path.GetFullPath(packagePath.Trim());
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Package file was not found.", fullPath);
        }

        return new ModPackageParseContext
        {
            PackagePath = fullPath,
            FileName = fileInfo.Name,
            FileLength = fileInfo.Length,
            LastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
            PackageIndex = DbpfPackageIndexReader.ReadPackageIndex(fullPath)
        };
    }
}

internal interface IContextAwareFastModItemIndexService
{
    Task<ModItemFastIndexBuildResult> BuildFastPackageAsync(
        ModPackageParseContext parseContext,
        CancellationToken cancellationToken = default);
}

internal interface IContextAwareDeepModItemEnrichmentService
{
    Task<ModItemEnrichmentBatch> EnrichPackageAsync(
        ModPackageParseContext parseContext,
        CancellationToken cancellationToken = default);
}
