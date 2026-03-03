using SimsModDesktop.Application.TextureCompression;

namespace SimsModDesktop.Application.TextureProcessing;

public sealed class TextureTranscodeRequest
{
    public required TextureSourceDescriptor Source { get; init; }
    public required ReadOnlyMemory<byte> SourceBytes { get; init; }
    public required TextureTargetFormat TargetFormat { get; init; }
    public required int TargetWidth { get; init; }
    public required int TargetHeight { get; init; }
    public bool GenerateMipMaps { get; init; } = true;
    public TextureColorSpaceHint ColorSpaceHint { get; init; } = TextureColorSpaceHint.Srgb;
}
