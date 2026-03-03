namespace SimsModDesktop.Models;

public sealed class SimsTrayPreviewItem
{
    private string? _contentFingerprint;

    public required string TrayItemKey { get; init; }
    public required string PresetType { get; init; }
    public IReadOnlyList<SimsTrayPreviewItem> ChildItems { get; init; } = Array.Empty<SimsTrayPreviewItem>();
    public string DisplayTitle { get; init; } = string.Empty;
    public string DisplaySubtitle { get; init; } = string.Empty;
    public string DisplayDescription { get; init; } = string.Empty;
    public string DisplayPrimaryMeta { get; init; } = string.Empty;
    public string DisplaySecondaryMeta { get; init; } = string.Empty;
    public string DisplayTertiaryMeta { get; init; } = string.Empty;
    public TrayDebugMetadata DebugMetadata { get; init; } = new();
    public string TrayRootPath { get; init; } = string.Empty;
    public string TrayInstanceId { get; init; } = string.Empty;
    public string ContentFingerprint
    {
        get
        {
            if (!string.IsNullOrEmpty(_contentFingerprint))
            {
                return _contentFingerprint;
            }

            _contentFingerprint = TrayContentFingerprint.Compute(SourceFilePaths);
            return _contentFingerprint;
        }
        init => _contentFingerprint = value;
    }

    public IReadOnlyList<string> SourceFilePaths { get; init; } = Array.Empty<string>();
    public string ItemName { get; init; } = string.Empty;
    public string AuthorId { get; init; } = string.Empty;
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public double TotalMB { get; init; }
    public DateTime LatestWriteTimeLocal { get; init; }
    public string ResourceTypes { get; init; } = string.Empty;
    public string Extensions { get; init; } = string.Empty;
    public string FileListPreview { get; init; } = string.Empty;

    public bool HasDisplaySubtitle => !string.IsNullOrWhiteSpace(DisplaySubtitle);
    public bool HasDisplayDescription => !string.IsNullOrWhiteSpace(DisplayDescription);
    public bool HasDisplayPrimaryMeta => !string.IsNullOrWhiteSpace(DisplayPrimaryMeta);
    public bool HasDisplaySecondaryMeta => !string.IsNullOrWhiteSpace(DisplaySecondaryMeta);
    public bool HasDisplayTertiaryMeta => !string.IsNullOrWhiteSpace(DisplayTertiaryMeta);
}
