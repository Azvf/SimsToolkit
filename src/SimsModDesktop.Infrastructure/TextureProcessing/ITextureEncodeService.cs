namespace SimsModDesktop.Infrastructure.TextureProcessing;

public interface ITextureEncodeService
{
    bool TryEncode(
        TexturePixelBuffer pixelBuffer,
        TextureTargetFormat targetFormat,
        bool generateMipMaps,
        out byte[] encodedBytes,
        out int mipMapCount,
        out string error);
}
