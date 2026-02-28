namespace SimsModDesktop.Models;

public sealed class SimsTrayPreviewItem
{
    public required string TrayItemKey { get; init; }
    public required string PresetType { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public string AuthorId { get; init; } = string.Empty;
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public double TotalMB { get; init; }
    public DateTime LatestWriteTimeLocal { get; init; }
    public string ResourceTypes { get; init; } = string.Empty;
    public string Extensions { get; init; } = string.Empty;
    public string FileListPreview { get; init; } = string.Empty;
}
