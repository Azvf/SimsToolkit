namespace SimsModDesktop.Application.TextureCompression;

public sealed class ModPackageTexturePreviewResult
{
    public bool Success { get; init; }
    public byte[] PngBytes { get; init; } = Array.Empty<byte>();
    public int Width { get; init; }
    public int Height { get; init; }
    public string Format { get; init; } = string.Empty;
    public string? Error { get; init; }
}
