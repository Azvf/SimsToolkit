namespace SimsModDesktop.Infrastructure.TextureProcessing;

public sealed class TexturePixelBuffer
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required TexturePixelLayout Layout { get; init; }
    public required byte[] PixelBytes { get; init; }

    public void Validate()
    {
        if (Width < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "Width must be greater than zero.");
        }

        if (Height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Height), "Height must be greater than zero.");
        }

        if (Layout != TexturePixelLayout.Rgba32)
        {
            throw new InvalidOperationException($"Unsupported pixel layout: {Layout}.");
        }

        var expectedLength = checked(Width * Height * 4);
        if (PixelBytes.Length != expectedLength)
        {
            throw new InvalidOperationException(
                $"Pixel buffer length mismatch. Expected {expectedLength} bytes, got {PixelBytes.Length}.");
        }
    }
}
