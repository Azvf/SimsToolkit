using Pfim;

namespace SimsModDesktop.Infrastructure.TextureProcessing;

public sealed class PfimDdsDecoder
{
    public bool TryDecode(ReadOnlyMemory<byte> sourceBytes, out TexturePixelBuffer pixelBuffer, out string error)
    {
        pixelBuffer = null!;
        error = string.Empty;

        if (sourceBytes.IsEmpty)
        {
            error = "DDS/TGA source bytes are empty.";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(sourceBytes.ToArray(), writable: false);
            using var image = Pfimage.FromStream(stream);

            if (image.Width < 1 || image.Height < 1)
            {
                error = "Decoded image has invalid dimensions.";
                return false;
            }

            if (!TryConvertToRgba32(image, out var pixels, out error))
            {
                return false;
            }

            pixelBuffer = new TexturePixelBuffer
            {
                Width = image.Width,
                Height = image.Height,
                Layout = TexturePixelLayout.Rgba32,
                PixelBytes = pixels
            };
            pixelBuffer.Validate();
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to decode DDS/TGA: {ex.Message}";
            return false;
        }
    }

    private static bool TryConvertToRgba32(IImage image, out byte[] pixels, out string error)
    {
        error = string.Empty;
        pixels = Array.Empty<byte>();

        switch (image.BitsPerPixel)
        {
            case 32:
                pixels = ConvertBgra32ToRgba32(image.Data, image.Width, image.Height);
                return true;
            case 24:
                pixels = ConvertBgr24ToRgba32(image.Data, image.Width, image.Height);
                return true;
            default:
                error = $"Unsupported decoded pixel format: {image.Format} ({image.BitsPerPixel} bpp).";
                return false;
        }
    }

    private static byte[] ConvertBgra32ToRgba32(byte[] source, int width, int height)
    {
        var destination = new byte[checked(width * height * 4)];
        for (var i = 0; i < source.Length; i += 4)
        {
            destination[i] = source[i + 2];
            destination[i + 1] = source[i + 1];
            destination[i + 2] = source[i];
            destination[i + 3] = source[i + 3];
        }

        return destination;
    }

    private static byte[] ConvertBgr24ToRgba32(byte[] source, int width, int height)
    {
        var destination = new byte[checked(width * height * 4)];
        for (int srcIndex = 0, dstIndex = 0; srcIndex < source.Length; srcIndex += 3, dstIndex += 4)
        {
            destination[dstIndex] = source[srcIndex + 2];
            destination[dstIndex + 1] = source[srcIndex + 1];
            destination[dstIndex + 2] = source[srcIndex];
            destination[dstIndex + 3] = 255;
        }

        return destination;
    }
}
