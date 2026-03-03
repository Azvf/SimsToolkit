using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Application.TextureProcessing;

public sealed class TextureSourceDescriptor
{
    public required DbpfResourceKey ResourceKey { get; init; }
    public required TextureContainerKind ContainerKind { get; init; }
    public required TexturePixelFormatKind SourcePixelFormat { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required bool HasAlpha { get; init; }
    public required int MipMapCount { get; init; }
}
