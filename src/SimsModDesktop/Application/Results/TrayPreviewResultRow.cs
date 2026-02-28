namespace SimsModDesktop.Application.Results;

public sealed record TrayPreviewResultRow
{
    public string TrayItemKey { get; init; } = string.Empty;
    public string PresetType { get; init; } = string.Empty;
    public int FileCount { get; init; }
    public double TotalMB { get; init; }
    public DateTime LatestWriteTimeLocal { get; init; }
    public string Extensions { get; init; } = string.Empty;
    public string ResourceTypes { get; init; } = string.Empty;
    public string FileListPreview { get; init; } = string.Empty;
}
