using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SimsModDesktop.Infrastructure.TextureProcessing;

public sealed class ImageSharpPngDecoder
{
    public bool TryDecode(ReadOnlyMemory<byte> sourceBytes, out TexturePixelBuffer pixelBuffer, out string error)
    {
        pixelBuffer = null!;
        error = string.Empty;

        if (sourceBytes.IsEmpty)
        {
            error = "PNG source bytes are empty.";
            return false;
        }

        try
        {
            using var image = Image.Load<Rgba32>(sourceBytes.Span);
            var pixels = new byte[checked(image.Width * image.Height * 4)];
            image.CopyPixelDataTo(pixels);

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
            error = $"Failed to decode PNG: {ex.Message}";
            return false;
        }
    }
}
