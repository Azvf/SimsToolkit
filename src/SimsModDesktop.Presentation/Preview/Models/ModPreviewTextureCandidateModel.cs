namespace SimsModDesktop.Presentation.ViewModels.Preview.Models;

public sealed record ModPreviewTextureCandidateModel
{
    public string ResourceKey { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;
    public string Resolution { get; init; } = string.Empty;
    public string LinkRole { get; init; } = string.Empty;
    public string SuggestedAction { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public bool CanEdit { get; init; }
    public bool IsSelected { get; init; }
}
