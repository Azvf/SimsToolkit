using SimsModDesktop.Infrastructure.TextureProcessing;

namespace SimsModDesktop.Application.TextureCompression;

public sealed class TextureCompressionRequest
{
    public required TextureSourceDescriptor Source { get; init; }
    public required ReadOnlyMemory<byte> SourceBytes { get; init; }
    public int? TargetWidth { get; init; }
    public int? TargetHeight { get; init; }
    public bool GenerateMipMaps { get; init; } = true;
    public TextureTargetFormat? PreferredFormat { get; init; }
    public TextureColorSpaceHint ColorSpaceHint { get; init; } = TextureColorSpaceHint.Srgb;
}
