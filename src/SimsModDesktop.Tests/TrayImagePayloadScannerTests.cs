using SimsModDesktop.Services;
using SkiaSharp;

namespace SimsModDesktop.Tests;

public sealed class TrayImagePayloadScannerTests
{
    [Fact]
    public void TryExtractBestImage_DecodesExactEncodedTrayImageContainer()
    {
        var payload = ImageTestHelpers.CreateEncodedTrayImageBytes(
            ImageTestHelpers.CreateJpegBytes(64, 48));

        var image = TrayImagePayloadScanner.TryExtractBestImage(payload);

        Assert.NotNull(image);
        Assert.Equal(64, image!.Width);
        Assert.Equal(48, image.Height);
    }

    [Fact]
    public void TryExtractBestImage_AppliesEmbeddedAlphaMaskAndReturnsTransparentPng()
    {
        var jpegWithAlpha = ImageTestHelpers.CreateJpegWithEmbeddedAlphaSegment(
            ImageTestHelpers.CreateJpegBytes(8, 4),
            ImageTestHelpers.CreateAlphaMaskPngBytes(8, 4));
        var payload = ImageTestHelpers.CreateEncodedTrayImageBytes(jpegWithAlpha);

        var image = TrayImagePayloadScanner.TryExtractBestImage(payload);

        Assert.NotNull(image);
        Assert.True(image!.Data.Length > 8);
        Assert.Equal(0x89, image.Data[0]);
        Assert.Equal(0x50, image.Data[1]);

        using var bitmap = SKBitmap.Decode(image.Data);
        Assert.NotNull(bitmap);
        Assert.Equal((byte)0, bitmap!.GetPixel(1, 1).Alpha);
        Assert.Equal((byte)255, bitmap.GetPixel(6, 1).Alpha);
    }
}
