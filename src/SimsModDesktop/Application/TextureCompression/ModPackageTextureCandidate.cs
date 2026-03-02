namespace SimsModDesktop.Application.TextureCompression;

public sealed class ModPackageTextureCandidate
{
    public required string ResourceKeyText { get; init; }
    public required string ContainerKind { get; init; }
    public required string Format { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int MipMapCount { get; init; }
    public required bool Editable { get; init; }
    public required string SuggestedAction { get; init; }
    public required string Notes { get; init; }
    public required int SizeBytes { get; init; }
    public string LinkRole { get; init; } = "Fallback";
}
