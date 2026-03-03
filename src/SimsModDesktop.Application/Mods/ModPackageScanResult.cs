namespace SimsModDesktop.Application.Mods;

public sealed class ModPackageScanResult
{
    public required string PackagePath { get; init; }
    public required long FileLength { get; init; }
    public required long LastWriteUtcTicks { get; init; }
    public required string PackageType { get; init; }
    public required string ScopeHint { get; init; }
}
