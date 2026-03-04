using SimsModDesktop.Application.TextureCompression;
using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SimsModDesktop.Infrastructure.TextureProcessing;

public sealed class BcnTextureEncodeService : ITextureEncodeService
{
    public bool TryEncode(
        TexturePixelBuffer pixelBuffer,
        TextureTargetFormat targetFormat,
        bool generateMipMaps,
        out byte[] encodedBytes,
        out int mipMapCount,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(pixelBuffer);
        pixelBuffer.Validate();

        encodedBytes = Array.Empty<byte>();
        mipMapCount = 0;
        error = string.Empty;

        try
        {
            using var image = Image.LoadPixelData<Rgba32>(pixelBuffer.PixelBytes, pixelBuffer.Width, pixelBuffer.Height);
            using var stream = new MemoryStream(EstimateEncodedCapacity(pixelBuffer.Width, pixelBuffer.Height, generateMipMaps, targetFormat));

            var encoder = new BcEncoder
            {
                OutputOptions =
                {
                    FileFormat = OutputFileFormat.Dds,
                    GenerateMipMaps = generateMipMaps
                }
            };

            encoder.OutputOptions.Format = targetFormat switch
            {
                TextureTargetFormat.Bc1 => CompressionFormat.Bc1,
                TextureTargetFormat.Bc3 => CompressionFormat.Bc3,
                _ => throw new InvalidOperationException($"Unsupported target format: {targetFormat}.")
            };
            encoder.OutputOptions.Quality = CompressionQuality.Balanced;

            encoder.EncodeToStream(image, stream);

            encodedBytes = stream.ToArray();
            mipMapCount = generateMipMaps
                ? CalculateMipMapCount(pixelBuffer.Width, pixelBuffer.Height)
                : 1;

            if (encodedBytes.Length == 0)
            {
                error = "Encoded output was empty.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to encode BC texture: {ex.Message}";
            return false;
        }
    }

    private static int CalculateMipMapCount(int width, int height)
    {
        var levels = 1;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            levels++;
        }

        return levels;
    }

    private static int EstimateEncodedCapacity(int width, int height, bool generateMipMaps, TextureTargetFormat targetFormat)
    {
        var bytesPerBlock = targetFormat == TextureTargetFormat.Bc1 ? 8 : 16;
        var total = 128; // DDS header
        var currentWidth = Math.Max(1, width);
        var currentHeight = Math.Max(1, height);

        while (true)
        {
            var blockWidth = Math.Max(1, (currentWidth + 3) / 4);
            var blockHeight = Math.Max(1, (currentHeight + 3) / 4);
            total += blockWidth * blockHeight * bytesPerBlock;

            if (!generateMipMaps || (currentWidth == 1 && currentHeight == 1))
            {
                break;
            }

            currentWidth = Math.Max(1, currentWidth / 2);
            currentHeight = Math.Max(1, currentHeight / 2);
        }

        return Math.Max(256, total);
    }
}
