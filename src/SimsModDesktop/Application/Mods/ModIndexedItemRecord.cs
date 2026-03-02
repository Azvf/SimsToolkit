using SimsModDesktop.Application.TextureCompression;

namespace SimsModDesktop.Application.Mods;

public sealed class ModIndexedItemRecord
{
    public required string ItemKey { get; init; }
    public required string PackagePath { get; init; }
    public required long PackageFingerprintLength { get; init; }
    public required long PackageFingerprintLastWriteUtcTicks { get; init; }
    public required string EntityKind { get; init; }
    public required string EntitySubType { get; init; }
    public required string DisplayName { get; init; }
    public required string SortName { get; init; }
    public required string SearchText { get; init; }
    public required string ScopeText { get; init; }
    public required string ThumbnailStatus { get; init; }
    public string? PrimaryTextureResourceKey { get; init; }
    public string? PrimaryTextureFormat { get; init; }
    public int? PrimaryTextureWidth { get; init; }
    public int? PrimaryTextureHeight { get; init; }
    public required int TextureCount { get; init; }
    public required int EditableTextureCount { get; init; }
    public required bool HasTextureData { get; init; }
    public required string SourceResourceKey { get; init; }
    public required string SourceGroupText { get; init; }
    public required long CreatedUtcTicks { get; init; }
    public required long UpdatedUtcTicks { get; init; }
    public string SortKeyStable { get; init; } = string.Empty;
    public ModItemDisplayStage DisplayStage { get; init; } = ModItemDisplayStage.Fast;
    public ModItemThumbnailStage ThumbnailStage { get; init; } = ModItemThumbnailStage.None;
    public ModItemTextureStage TextureStage { get; init; } = ModItemTextureStage.Pending;
    public string? ThumbnailCacheKey { get; init; }
    public long LastFastParsedUtcTicks { get; init; }
    public long? LastDeepParsedUtcTicks { get; init; }
    public bool PendingDeepRefresh { get; init; }
    public string? PartNameRaw { get; init; }
    public string DisplayNameSource { get; init; } = "Fallback";
    public uint? TitleKey { get; init; }
    public uint? PartDescriptionKey { get; init; }
    public uint? BodyTypeNumeric { get; init; }
    public string? BodyTypeText { get; init; }
    public uint? BodySubTypeNumeric { get; init; }
    public uint? AgeGenderFlags { get; init; }
    public string? AgeGenderText { get; init; }
    public uint? SpeciesNumeric { get; init; }
    public string? SpeciesText { get; init; }
    public uint? OutfitId { get; init; }
    public required IReadOnlyList<ModPackageTextureCandidate> TextureCandidates { get; init; }
}
