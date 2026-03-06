using SimsModDesktop.Application.Models;
using SimsModDesktop.Application.Saves;

namespace SimsModDesktop.Infrastructure.Tray;

internal sealed class RootSnapshot
{
    public required PreviewSourceKind SourceKind { get; init; }
    public required string SourceKey { get; init; }
    public required string NormalizedTrayRoot { get; init; }
    public required long DirectoryWriteUtcTicks { get; init; }
    public required string RootFingerprint { get; init; }
    public IReadOnlyList<PreviewRowDescriptor> RowDescriptors { get; init; } = Array.Empty<PreviewRowDescriptor>();
    public required DateTime CachedAtUtc { get; init; }
}

internal sealed class CachedSnapshot
{
    public required string CacheKey { get; init; }
    public required string RootFingerprint { get; init; }
    public IReadOnlyList<SimsTrayPreviewItem> Rows { get; init; } = Array.Empty<SimsTrayPreviewItem>();
    public IReadOnlyList<PreviewRowDescriptor> RowDescriptors { get; init; } = Array.Empty<PreviewRowDescriptor>();
    public required SimsTrayPreviewSummary Summary { get; init; }
    public required DateTime CachedAtUtc { get; init; }
    public bool HasMaterializedRows => Rows.Count != 0;
    public int TotalItemCount => HasMaterializedRows ? Rows.Count : RowDescriptors.Count;
}

internal sealed class PreviewRowDescriptor
{
    public required GroupAccumulator Group { get; init; }
    public required IReadOnlyList<GroupAccumulator> ChildGroups { get; init; }
    public required string PresetType { get; init; }
    public required string ItemName { get; init; }
    public required string FileListPreview { get; init; }
    public required string NormalizedFallbackSearchText { get; init; }
    public required int FileCount { get; init; }
    public required long TotalBytes { get; init; }
    public required DateTime LatestWriteTimeLocal { get; init; }
    public SavePreviewDescriptorEntry? SaveDescriptorEntry { get; init; }
    public string SaveDescriptorSourcePath { get; init; } = string.Empty;
    public long SaveDescriptorSourceLastWriteUtcTicks { get; init; }
    public string SaveDescriptorSchemaVersion { get; init; } = string.Empty;
}

internal sealed class TrayMetadataIndexState
{
    public Dictionary<string, MetadataIndexEntry> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, TrayMetadataResult?> MetadataByTrayItemPath { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class MetadataIndexEntry
{
    public required string AuthorSearchText { get; init; }
    public required string NormalizedSearchText { get; init; }
}

internal sealed class GroupAccumulator
{
    public GroupAccumulator(string key)
    {
        Key = key;
    }

    public string Key { get; }
    public string ItemName { get; set; } = string.Empty;
    public string TrayInstanceId { get; set; } = string.Empty;
    public string TrayItemPath { get; set; } = string.Empty;
    public TrayIdentity? RepresentativeIdentity { get; set; }
    public bool HasHouseholdAnchorFile { get; set; }
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public DateTime LatestWriteTimeUtc { get; set; } = DateTime.MinValue;
    public HashSet<string> ResourceTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Extensions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> FileNames { get; } = new();
    public List<string> SourceFiles { get; } = new();
}

internal sealed class TrayFileEntry
{
    public TrayFileEntry(FileInfo file, TrayIdentity identity)
    {
        File = file;
        Identity = identity;
    }

    public FileInfo File { get; }
    public TrayIdentity Identity { get; }
}

internal sealed class TrayIdentity
{
    public bool ParseSuccess { get; init; }
    public string TypeHex { get; init; } = string.Empty;
    public string InstanceHex { get; init; } = string.Empty;
}
