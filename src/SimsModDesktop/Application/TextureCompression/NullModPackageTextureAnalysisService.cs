namespace SimsModDesktop.Application.TextureCompression;

public sealed class NullModPackageTextureAnalysisService : IModPackageTextureAnalysisService
{
    public static NullModPackageTextureAnalysisService Instance { get; } = new();

    private NullModPackageTextureAnalysisService()
    {
    }

    public Task<ModPackageTextureSummary?> TryGetCachedAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ModPackageTextureSummary?>(null);
    }

    public Task<ModPackageTextureSummary> AnalyzeAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Mod package texture analysis service is not configured.");
    }

    public Task<ModPackageTextureAnalysisResult?> TryGetCachedResultAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ModPackageTextureAnalysisResult?>(null);
    }

    public Task<ModPackageTextureAnalysisResult> AnalyzeResultAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("Mod package texture analysis service is not configured.");
    }
}
