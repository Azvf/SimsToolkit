namespace SimsModDesktop.Application.TextureCompression;

public sealed class TextureCompressionFileRequest
{
    public required string SourcePath { get; init; }
    public required string OutputPath { get; init; }
    public int? TargetWidth { get; init; }
    public int? TargetHeight { get; init; }
    public bool HasAlphaHint { get; init; } = true;
    public bool GenerateMipMaps { get; init; } = true;
    public TextureTargetFormat? PreferredFormat { get; init; }
    public bool WhatIf { get; init; }
}
