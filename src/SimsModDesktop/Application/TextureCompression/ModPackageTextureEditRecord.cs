namespace SimsModDesktop.Application.TextureCompression;

public sealed class ModPackageTextureEditRecord
{
    public required string EditId { get; init; }
    public required string PackagePath { get; init; }
    public required string ResourceKeyText { get; init; }
    public required string RecordKind { get; init; }
    public required string AppliedAction { get; init; }
    public required byte[] OriginalBytes { get; init; }
    public required byte[] ReplacementBytes { get; init; }
    public required long AppliedUtcTicks { get; init; }
    public string? TargetEditId { get; init; }
    public long? RolledBackUtcTicks { get; init; }
    public string? Notes { get; init; }
}
