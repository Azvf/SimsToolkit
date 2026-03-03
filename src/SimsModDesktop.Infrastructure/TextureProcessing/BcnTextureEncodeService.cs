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
            using var stream = new MemoryStream();

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
}
