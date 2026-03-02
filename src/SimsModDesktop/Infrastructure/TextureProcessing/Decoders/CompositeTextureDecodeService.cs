namespace SimsModDesktop.Infrastructure.TextureProcessing;

public sealed class CompositeTextureDecodeService : ITextureDecodeService
{
    private readonly ImageSharpPngDecoder _pngDecoder;
    private readonly PfimDdsDecoder _ddsDecoder;

    public CompositeTextureDecodeService(
        ImageSharpPngDecoder pngDecoder,
        PfimDdsDecoder ddsDecoder)
    {
        _pngDecoder = pngDecoder;
        _ddsDecoder = ddsDecoder;
    }

    public bool TryDecode(
        TextureContainerKind containerKind,
        ReadOnlyMemory<byte> sourceBytes,
        out TexturePixelBuffer pixelBuffer,
        out string error)
    {
        return containerKind switch
        {
            TextureContainerKind.Png => _pngDecoder.TryDecode(sourceBytes, out pixelBuffer, out error),
            TextureContainerKind.Dds => _ddsDecoder.TryDecode(sourceBytes, out pixelBuffer, out error),
            TextureContainerKind.Tga => _ddsDecoder.TryDecode(sourceBytes, out pixelBuffer, out error),
            _ => FailUnsupported(out pixelBuffer, out error, containerKind)
        };
    }

    private static bool FailUnsupported(out TexturePixelBuffer pixelBuffer, out string error, TextureContainerKind containerKind)
    {
        pixelBuffer = null!;
        error = $"Unsupported container kind: {containerKind}.";
        return false;
    }
}
