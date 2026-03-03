namespace SimsModDesktop.Application.TextureCompression;

public sealed class ModPackageTextureEditExecutionResult
{
    public bool Success { get; init; }
    public string ResourceKeyText { get; init; } = string.Empty;
    public string? EditId { get; init; }
    public string StatusText { get; init; } = string.Empty;
    public string? Error { get; init; }
}
