namespace SimsModDesktop.Application.Mods;

public sealed record ModPackageInventoryEntry
{
    public required string PackagePath { get; init; }
    public required long FileLength { get; init; }
    public required long LastWriteUtcTicks { get; init; }
    public required string PackageType { get; init; }
    public required string ScopeHint { get; init; }
}

public sealed record ModPackageInventorySnapshot
{
    public required string ModsRootPath { get; init; }
    public long InventoryVersion { get; init; }
    public IReadOnlyList<ModPackageInventoryEntry> Entries { get; init; } = Array.Empty<ModPackageInventoryEntry>();
    public long LastValidatedUtcTicks { get; init; }
}

public sealed record ModPackageInventoryRefreshResult
{
    public required ModPackageInventorySnapshot Snapshot { get; init; }
    public IReadOnlyList<ModPackageInventoryEntry> AddedEntries { get; init; } = Array.Empty<ModPackageInventoryEntry>();
    public IReadOnlyList<ModPackageInventoryEntry> ChangedEntries { get; init; } = Array.Empty<ModPackageInventoryEntry>();
    public IReadOnlyList<ModPackageInventoryEntry> UnchangedEntries { get; init; } = Array.Empty<ModPackageInventoryEntry>();
    public IReadOnlyList<string> RemovedPackagePaths { get; init; } = Array.Empty<string>();
}

public sealed record ModPackageInventoryRefreshProgress
{
    public string Stage { get; init; } = string.Empty;
    public int Percent { get; init; }
    public int Current { get; init; }
    public int Total { get; init; }
    public string Detail { get; init; } = string.Empty;
}
