namespace SimsModDesktop.Application.TextureCompression;

public sealed class ModPackageTextureAnalysisResult
{
    public required ModPackageTextureSummary Summary { get; init; }
    public IReadOnlyList<ModPackageTextureCandidate> Candidates { get; init; } = Array.Empty<ModPackageTextureCandidate>();
}
