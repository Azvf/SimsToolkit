namespace SimsModDesktop.Application.TextureCompression;

public interface IModPackageTextureAnalysisStore
{
    Task<ModPackageTextureAnalysisResult?> TryGetAsync(
        string packagePath,
        long fileLength,
        long lastWriteUtcTicks,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        ModPackageTextureAnalysisResult analysis,
        CancellationToken cancellationToken = default);
}
