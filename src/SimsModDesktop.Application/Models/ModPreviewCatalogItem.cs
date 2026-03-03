namespace SimsModDesktop.Application.Models;

public sealed record ModPreviewCatalogItem
{
    public required string Key { get; init; }
    public string DisplayTitle { get; init; } = string.Empty;
    public string DisplaySubtitle { get; init; } = string.Empty;
    public string PackageType { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string FileSizeText { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public DateTime LastUpdatedLocal { get; init; }
    public string LastUpdatedText { get; init; } = string.Empty;
    public bool IsOverride { get; init; }
    public bool HasConflictHint { get; init; }
    public string ConflictHintText { get; init; } = string.Empty;
    public string Classification { get; init; } = string.Empty;
    public string FileExtension { get; init; } = string.Empty;
}
