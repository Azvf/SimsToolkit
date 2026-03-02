using System.Text;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Infrastructure.TextureProcessing;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Tests.TextureProcessing;

public sealed class TextureProcessingPipelineTests
{
    [Fact]
    public void ImageSharpPngDecoder_DecodesPngIntoRgbaBuffer()
    {
        var decoder = new ImageSharpPngDecoder();
        var pngBytes = ImageTestHelpers.CreatePngBytes(8, 4);

        var success = decoder.TryDecode(pngBytes, out var pixelBuffer, out var error);

        Assert.True(success, error);
        Assert.Equal(8, pixelBuffer.Width);
        Assert.Equal(4, pixelBuffer.Height);
        Assert.Equal(TexturePixelLayout.Rgba32, pixelBuffer.Layout);
        Assert.Equal(8 * 4 * 4, pixelBuffer.PixelBytes.Length);
    }

    [Fact]
    public void ImageSharpResizeService_ResizesWithoutChangingLayout()
    {
        var decoder = new ImageSharpPngDecoder();
        var resizeService = new ImageSharpResizeService();
        var pngBytes = ImageTestHelpers.CreatePngBytes(8, 8);
        Assert.True(decoder.TryDecode(pngBytes, out var source, out var error), error);

        var resized = resizeService.Resize(source, 4, 4);

        Assert.Equal(4, resized.Width);
        Assert.Equal(4, resized.Height);
        Assert.Equal(TexturePixelLayout.Rgba32, resized.Layout);
        Assert.Equal(4 * 4 * 4, resized.PixelBytes.Length);
    }

    [Fact]
    public void BcnTextureEncodeService_EncodesBc3Dds()
    {
        var encoder = new BcnTextureEncodeService();
        var pixelBuffer = CreateSolidPixelBuffer(4, 4, 255);

        var success = encoder.TryEncode(
            pixelBuffer,
            TextureTargetFormat.Bc3,
            generateMipMaps: true,
            out var encodedBytes,
            out var mipMapCount,
            out var error);

        Assert.True(success, error);
        Assert.True(encodedBytes.Length > 128);
        Assert.Equal("DDS ", Encoding.ASCII.GetString(encodedBytes, 0, 4));
        Assert.True(mipMapCount >= 1);
    }

    [Fact]
    public void PfimDdsDecoder_DecodesEncodedBc3Output()
    {
        var encoder = new BcnTextureEncodeService();
        var ddsDecoder = new PfimDdsDecoder();
        var pixelBuffer = CreateSolidPixelBuffer(4, 4, 255);
        Assert.True(
            encoder.TryEncode(
                pixelBuffer,
                TextureTargetFormat.Bc3,
                generateMipMaps: false,
                out var ddsBytes,
                out _,
                out var encodeError),
            encodeError);

        var success = ddsDecoder.TryDecode(ddsBytes, out var decoded, out var decodeError);

        Assert.True(success, decodeError);
        Assert.Equal(4, decoded.Width);
        Assert.Equal(4, decoded.Height);
        Assert.Equal(4 * 4 * 4, decoded.PixelBytes.Length);
    }

    [Fact]
    public void TextureTranscodePipeline_TranscodesPngToBc3()
    {
        var pipeline = CreatePipeline();
        var pngBytes = ImageTestHelpers.CreatePngBytes(8, 8);

        var result = pipeline.Transcode(new TextureTranscodeRequest
        {
            Source = CreateDescriptor(TextureContainerKind.Png, TexturePixelFormatKind.Rgba32, 8, 8, hasAlpha: true),
            SourceBytes = pngBytes,
            TargetFormat = TextureTargetFormat.Bc3,
            TargetWidth = 4,
            TargetHeight = 4,
            GenerateMipMaps = true
        });

        Assert.True(result.Success, result.Error);
        Assert.Equal(TextureTargetFormat.Bc3, result.OutputFormat);
        Assert.Equal(4, result.OutputWidth);
        Assert.Equal(4, result.OutputHeight);
        Assert.True(result.EncodedBytes.Length > 0);
    }

    [Fact]
    public void TextureCompressionService_SelectsBc1WhenSourceHasNoAlpha()
    {
        var decodeService = new CompositeTextureDecodeService(new ImageSharpPngDecoder(), new PfimDdsDecoder());
        var service = new TextureCompressionService(decodeService, CreatePipeline());
        var pngBytes = ImageTestHelpers.CreatePngBytes(8, 8);

        var result = service.Compress(new TextureCompressionRequest
        {
            Source = CreateDescriptor(TextureContainerKind.Png, TexturePixelFormatKind.Rgb24, 8, 8, hasAlpha: false),
            SourceBytes = pngBytes,
            TargetWidth = 4,
            TargetHeight = 4,
            GenerateMipMaps = false
        });

        Assert.True(result.Success, result.Error);
        Assert.Equal(TextureTargetFormat.Bc1, result.SelectedFormat);
        Assert.NotNull(result.TranscodeResult);
        Assert.True(result.TranscodeResult!.Success, result.TranscodeResult.Error);
    }

    [Fact]
    public void TextureTranscodePipeline_RejectsNonBlockAlignedTargets()
    {
        var pipeline = CreatePipeline();
        var pngBytes = ImageTestHelpers.CreatePngBytes(8, 8);

        var result = pipeline.Transcode(new TextureTranscodeRequest
        {
            Source = CreateDescriptor(TextureContainerKind.Png, TexturePixelFormatKind.Rgba32, 8, 8, hasAlpha: true),
            SourceBytes = pngBytes,
            TargetFormat = TextureTargetFormat.Bc3,
            TargetWidth = 6,
            TargetHeight = 4
        });

        Assert.False(result.Success);
        Assert.Contains("multiples of 4", result.Error);
    }

    private static ITextureTranscodePipeline CreatePipeline()
    {
        return new TextureTranscodePipeline(
            new CompositeTextureDecodeService(new ImageSharpPngDecoder(), new PfimDdsDecoder()),
            new ImageSharpResizeService(),
            new BcnTextureEncodeService());
    }

    private static TexturePixelBuffer CreateSolidPixelBuffer(int width, int height, byte alpha)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 64;
            pixels[i + 1] = 128;
            pixels[i + 2] = 255;
            pixels[i + 3] = alpha;
        }

        return new TexturePixelBuffer
        {
            Width = width,
            Height = height,
            Layout = TexturePixelLayout.Rgba32,
            PixelBytes = pixels
        };
    }

    private static TextureSourceDescriptor CreateDescriptor(
        TextureContainerKind containerKind,
        TexturePixelFormatKind pixelFormatKind,
        int width,
        int height,
        bool hasAlpha)
    {
        return new TextureSourceDescriptor
        {
            ResourceKey = new DbpfResourceKey(0x00B2D882, 0, 1),
            ContainerKind = containerKind,
            SourcePixelFormat = pixelFormatKind,
            Width = width,
            Height = height,
            HasAlpha = hasAlpha,
            MipMapCount = 1
        };
    }
}
