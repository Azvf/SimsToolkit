namespace SimsModDesktop.Infrastructure.TextureProcessing;

public interface ITextureResizeService
{
    TexturePixelBuffer Resize(
        TexturePixelBuffer source,
        int targetWidth,
        int targetHeight);
}
