namespace SimsModDesktop.Application.Mods;

public sealed class ModItemListRow
{
    public required string ItemKey { get; init; }
    public required string DisplayName { get; init; }
    public required string EntityKind { get; init; }
    public required string EntitySubType { get; init; }
    public required string PackagePath { get; init; }
    public required string PackageName { get; init; }
    public required string ScopeText { get; init; }
    public required string ThumbnailStatus { get; init; }
    public required int TextureCount { get; init; }
    public required int EditableTextureCount { get; init; }
    public required string TextureSummaryText { get; init; }
    public string? PrimaryTextureResourceKey { get; init; }
    public string? PrimaryTextureFormat { get; init; }
    public int? PrimaryTextureWidth { get; init; }
    public int? PrimaryTextureHeight { get; init; }
    public bool IsPlaceholder { get; init; }
    public ModItemDisplayStage DisplayStage { get; init; } = ModItemDisplayStage.Fast;
    public ModItemThumbnailStage ThumbnailStage { get; init; } = ModItemThumbnailStage.None;
    public ModItemTextureStage TextureStage { get; init; } = ModItemTextureStage.Pending;
    public bool HasDeepData { get; init; }
    public bool ShowThumbnailPlaceholder { get; init; }
    public bool ShowMetadataPlaceholder { get; init; }
    public string StableSubtitleText { get; init; } = string.Empty;
    public string SortKeyStable { get; init; } = string.Empty;
    public string? BodyTypeText { get; init; }
    public string? AgeGenderText { get; init; }
    public string DisplayNameSource { get; init; } = "Fallback";
    public required long UpdatedUtcTicks { get; init; }
}
