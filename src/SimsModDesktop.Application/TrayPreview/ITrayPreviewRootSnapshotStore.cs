namespace SimsModDesktop.Application.TrayPreview;

public interface ITrayPreviewRootSnapshotStore
{
    bool TryLoad(
        string trayRootPath,
        long directoryWriteUtcTicks,
        out TrayPreviewRootSnapshotRecord snapshot);

    void Save(TrayPreviewRootSnapshotRecord snapshot);

    void Clear(string? trayRootPath = null);
}

public sealed record TrayPreviewRootSnapshotRecord
{
    public required string TrayRootPath { get; init; }
    public long DirectoryWriteUtcTicks { get; init; }
    public required string RootFingerprint { get; init; }
    public DateTime CachedAtUtc { get; init; } = DateTime.UtcNow;
    public IReadOnlyList<TrayPreviewRootItemRecord> Items { get; init; } = Array.Empty<TrayPreviewRootItemRecord>();
}

public sealed record TrayPreviewRootItemRecord
{
    public required TrayPreviewGroupRecord Group { get; init; }
    public IReadOnlyList<TrayPreviewGroupRecord> ChildGroups { get; init; } = Array.Empty<TrayPreviewGroupRecord>();
    public required string PresetType { get; init; }
    public required string ItemName { get; init; }
    public required string FileListPreview { get; init; }
    public required string NormalizedFallbackSearchText { get; init; }
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public long LatestWriteUtcTicks { get; init; }
}

public sealed record TrayPreviewGroupRecord
{
    public required string Key { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public string TrayInstanceId { get; init; } = string.Empty;
    public string TrayItemPath { get; init; } = string.Empty;
    public bool HasHouseholdAnchorFile { get; init; }
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public long LatestWriteUtcTicks { get; init; }
    public IReadOnlyList<string> ResourceTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FileNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SourceFiles { get; init; } = Array.Empty<string>();
}
