namespace SimsModDesktop.Services;

internal sealed class ExtractedTrayImage
{
    public required byte[] Data { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }

    public long PixelArea => (long)Width * Height;
}
