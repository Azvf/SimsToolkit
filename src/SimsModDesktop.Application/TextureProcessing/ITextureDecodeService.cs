namespace SimsModDesktop.Application.TextureProcessing;

public interface ITextureDecodeService
{
    bool TryDecode(
        TextureContainerKind containerKind,
        ReadOnlyMemory<byte> sourceBytes,
        out TexturePixelBuffer pixelBuffer,
        out string error);
}
