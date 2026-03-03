namespace SimsModDesktop.Models;

public sealed class TrayMetadataResult
{
    public string TrayItemPath { get; init; } = string.Empty;
    public string TrayMetadataId { get; init; } = string.Empty;
    public string ItemType { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string DescriptionHashtags { get; init; } = string.Empty;
    public string CreatorName { get; init; } = string.Empty;
    public string CreatorId { get; init; } = string.Empty;
    public string ModifierName { get; init; } = string.Empty;
    public string ModifierId { get; init; } = string.Empty;
    public ulong? Favorites { get; init; }
    public ulong? Downloads { get; init; }
    public ulong? ItemTimestamp { get; init; }
    public IReadOnlyList<ulong> MtxIds { get; init; } = Array.Empty<ulong>();
    public IReadOnlyList<uint> MetaInfo { get; init; } = Array.Empty<uint>();
    public int? VerifyCode { get; init; }
    public uint? CustomImageCount { get; init; }
    public uint? MannequinCount { get; init; }
    public ulong? IndexedCounter { get; init; }
    public uint? CreatorPlatform { get; init; }
    public uint? ModifierPlatform { get; init; }
    public ulong? CreatorPlatformId { get; init; }
    public string CreatorPlatformName { get; init; } = string.Empty;
    public ulong? ModifierPlatformId { get; init; }
    public string ModifierPlatformName { get; init; } = string.Empty;
    public uint? ImageUriType { get; init; }
    public ulong? SharedTimestamp { get; init; }
    public bool? Liked { get; init; }
    public int? FamilySize { get; init; }
    public int? PendingBabies { get; init; }
    public int? SizeX { get; init; }
    public int? SizeZ { get; init; }
    public int? PriceValue { get; init; }
    public int? NumBedrooms { get; init; }
    public int? NumBathrooms { get; init; }
    public int? Height { get; init; }
    public bool IsModdedContent { get; init; }
    public bool? IsHidden { get; init; }
    public bool? IsDownloadTemp { get; init; }
    public ulong? LanguageId { get; init; }
    public ulong? SkuId { get; init; }
    public bool? IsMaxisContent { get; init; }
    public uint? PayloadSize { get; init; }
    public bool? WasReported { get; init; }
    public bool? WasReviewedAndCleared { get; init; }
    public bool? IsImageModdedContent { get; init; }
    public uint? SpecificCreatorPlatform { get; init; }
    public uint? SpecificModifierPlatform { get; init; }
    public ulong? SpecificCreatorPlatformPersonaId { get; init; }
    public ulong? SpecificModifierPlatformPersonaId { get; init; }
    public bool? IsCgItem { get; init; }
    public bool? IsCgInterested { get; init; }
    public string CgName { get; init; } = string.Empty;
    public ulong? Sku2Id { get; init; }
    public IReadOnlyList<uint> CdsPatchBaseChangelists { get; init; } = Array.Empty<uint>();
    public bool? CdsContentPatchMounted { get; init; }
    public uint? SpecificDataVersion { get; init; }
    public ulong? VenueType { get; init; }
    public uint? PriceLevel { get; init; }
    public uint? ArchitectureValue { get; init; }
    public uint? NumThumbnails { get; init; }
    public uint? FrontSide { get; init; }
    public uint? VenueTypeStringKey { get; init; }
    public uint? GroundFloorIndex { get; init; }
    public IReadOnlyList<uint> OptionalRuleSatisfiedStringKeys { get; init; } = Array.Empty<uint>();
    public IReadOnlyList<ulong> LotTraits { get; init; } = Array.Empty<ulong>();
    public uint? BuildingType { get; init; }
    public ulong? LotTemplateId { get; init; }
    public bool HasUniversityHousingConfiguration { get; init; }
    public uint? TileCount { get; init; }
    public uint? UnitCount { get; init; }
    public int? UnitTraitCount { get; init; }
    public IReadOnlyList<uint> DynamicAreas { get; init; } = Array.Empty<uint>();
    public uint? RoomType { get; init; }
    public uint? RoomTypeStringKey { get; init; }
    public uint? PartBodyType { get; init; }
    public IReadOnlyList<TrayMemberDisplayMetadata> Members { get; init; } = Array.Empty<TrayMemberDisplayMetadata>();
}
