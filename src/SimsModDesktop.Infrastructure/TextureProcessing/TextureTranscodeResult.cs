using SimsModDesktop.Application.TextureCompression;

namespace SimsModDesktop.Infrastructure.TextureProcessing;

public sealed class TextureTranscodeResult
{
    public bool Success { get; init; }
    public byte[] EncodedBytes { get; init; } = Array.Empty<byte>();
    public TextureTargetFormat OutputFormat { get; init; }
    public int OutputWidth { get; init; }
    public int OutputHeight { get; init; }
    public int MipMapCount { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
