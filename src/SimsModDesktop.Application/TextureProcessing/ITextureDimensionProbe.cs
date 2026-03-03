namespace SimsModDesktop.Application.TextureProcessing;

public interface ITextureDimensionProbe
{
    bool TryGetDimensions(
        TextureContainerKind containerKind,
        ReadOnlyMemory<byte> sourceBytes,
        out int width,
        out int height,
        out string error);
}
