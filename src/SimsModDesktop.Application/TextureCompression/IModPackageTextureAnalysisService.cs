namespace SimsModDesktop.Application.TextureCompression;

public interface IModPackageTextureAnalysisService
{
    Task<ModPackageTextureSummary?> TryGetCachedAsync(
        string packagePath,
        CancellationToken cancellationToken = default);

    Task<ModPackageTextureSummary> AnalyzeAsync(
        string packagePath,
        CancellationToken cancellationToken = default);

    Task<ModPackageTextureAnalysisResult?> TryGetCachedResultAsync(
        string packagePath,
        CancellationToken cancellationToken = default);

    Task<ModPackageTextureAnalysisResult> AnalyzeResultAsync(
        string packagePath,
        CancellationToken cancellationToken = default);
}
