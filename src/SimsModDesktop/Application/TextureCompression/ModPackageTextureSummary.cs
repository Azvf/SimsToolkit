namespace SimsModDesktop.Application.TextureCompression;

public sealed class ModPackageTextureSummary
{
    public required string PackagePath { get; init; }
    public required long FileLength { get; init; }
    public required long LastWriteUtcTicks { get; init; }
    public required int TextureResourceCount { get; init; }
    public required int DdsCount { get; init; }
    public required int PngCount { get; init; }
    public required int UnsupportedTextureCount { get; init; }
    public required int EditableTextureCount { get; init; }
    public required long TotalTextureBytes { get; init; }
    public required DateTime LastAnalyzedLocal { get; init; }
}
