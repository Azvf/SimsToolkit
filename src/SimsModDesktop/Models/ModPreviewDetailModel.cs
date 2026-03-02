namespace SimsModDesktop.Models;

public sealed record ModPreviewDetailModel
{
    public string DisplayTitle { get; init; } = string.Empty;
    public string DisplaySubtitle { get; init; } = string.Empty;
    public string PackageType { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string PreviewStatusText { get; init; } = string.Empty;
    public string TextureStatusText { get; init; } = string.Empty;
    public string TextureLastAnalyzedText { get; init; } = string.Empty;
    public bool CanAnalyzeTextures { get; init; }
    public IReadOnlyList<ModPreviewDetailRow> OverviewRows { get; init; } = [];
    public IReadOnlyList<ModPreviewDetailRow> ResourceRows { get; init; } = [];
    public IReadOnlyList<ModPreviewDetailRow> TextureRows { get; init; } = [];
    public IReadOnlyList<ModPreviewTextureCandidateModel> TextureCandidates { get; init; } = [];
    public bool HasTextureCandidates { get; init; }
    public IReadOnlyList<ModPreviewDetailRow> ConflictRows { get; init; } = [];
    public IReadOnlyList<ModPreviewDetailRow> FileRows { get; init; } = [];
}
