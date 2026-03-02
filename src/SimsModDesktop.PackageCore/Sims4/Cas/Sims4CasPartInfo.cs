namespace SimsModDesktop.PackageCore;

public sealed class Sims4CasPartInfo
{
    public required DbpfResourceKey ResourceKey { get; init; }
    public required uint Version { get; init; }
    public string? PartNameRaw { get; init; }
    public required uint TitleKey { get; init; }
    public required uint PartDescriptionKey { get; init; }
    public required uint BodyTypeNumeric { get; init; }
    public required uint BodySubTypeNumeric { get; init; }
    public required uint SpeciesNumeric { get; init; }
    public required uint AgeGenderFlags { get; init; }
    public required uint OutfitId { get; init; }
    public required byte TextureIndex { get; init; }
    public required byte ShadowIndex { get; init; }
    public required byte NormalMapIndex { get; init; }
    public required byte SpecularIndex { get; init; }
    public required byte EmissionIndex { get; init; }
    public required IReadOnlyList<Sims4Tgi> ResourceTable { get; init; }
    public required Sims4CasPartTextureRefs TextureRefs { get; init; }
}
