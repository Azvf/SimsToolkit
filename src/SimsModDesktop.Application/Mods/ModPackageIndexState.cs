namespace SimsModDesktop.Application.Mods;

public sealed class ModPackageIndexState
{
    public required string PackagePath { get; init; }
    public required long FileLength { get; init; }
    public required long LastWriteUtcTicks { get; init; }
    public required string PackageType { get; init; }
    public required string ScopeHint { get; init; }
    public required long IndexedUtcTicks { get; init; }
    public required int ItemCount { get; init; }
    public required int CasItemCount { get; init; }
    public required int BuildBuyItemCount { get; init; }
    public required int UnclassifiedEntityCount { get; init; }
    public required int TextureResourceCount { get; init; }
    public required int EditableTextureCount { get; init; }
    public required string Status { get; init; }
    public string? FailureMessage { get; init; }
}
