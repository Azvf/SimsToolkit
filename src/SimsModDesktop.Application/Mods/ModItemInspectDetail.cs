using SimsModDesktop.Application.TextureCompression;

namespace SimsModDesktop.Application.Mods;

public sealed class ModItemInspectDetail
{
    public required string ItemKey { get; init; }
    public required string DisplayName { get; init; }
    public required string EntityKind { get; init; }
    public required string EntitySubType { get; init; }
    public required string PackagePath { get; init; }
    public required string SourceResourceKey { get; init; }
    public required string SourceGroupText { get; init; }
    public required long UpdatedUtcTicks { get; init; }
    public required bool HasTextureData { get; init; }
    public required string? PrimaryTextureResourceKey { get; init; }
    public required int UnclassifiedEntityCountForPackage { get; init; }
    public required int TextureCount { get; init; }
    public required int EditableTextureCount { get; init; }
    public ModItemDisplayStage DisplayStage { get; init; } = ModItemDisplayStage.Fast;
    public ModItemThumbnailStage ThumbnailStage { get; init; } = ModItemThumbnailStage.None;
    public ModItemTextureStage TextureStage { get; init; } = ModItemTextureStage.Pending;
    public bool PendingDeepRefresh { get; init; }
    public string? PartNameRaw { get; init; }
    public string DisplayNameSource { get; init; } = "Fallback";
    public uint? TitleKey { get; init; }
    public uint? PartDescriptionKey { get; init; }
    public string? BodyTypeText { get; init; }
    public string? AgeGenderText { get; init; }
    public string? SpeciesText { get; init; }
    public uint? OutfitId { get; init; }
    public required IReadOnlyList<ModPackageTextureCandidate> TextureRows { get; init; }
}
