using SimsModDesktop.Application.TextureProcessing;

namespace SimsModDesktop.Application.TextureCompression;

public sealed class TextureCompressionResult
{
    public bool Success { get; init; }
    public TextureTargetFormat SelectedFormat { get; init; }
    public TextureTranscodeResult? TranscodeResult { get; init; }
    public string? Error { get; init; }
}
