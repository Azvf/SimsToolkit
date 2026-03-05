namespace SimsModDesktop.PackageCore;

public sealed class Ts4SimInfoResource
{
    public required uint Version { get; init; }
    public required IReadOnlyList<float> Physique { get; init; }
    public required uint AgeGenderAgeFlags { get; init; }
    public required uint AgeGenderGenderFlags { get; init; }
    public required uint Species { get; init; }
    public required ulong SkinToneRef { get; init; }
    public required float SkinToneShift { get; init; }
    public required IReadOnlyList<DbpfResourceKey> Sculpts { get; init; }
    public required IReadOnlyList<Ts4SimModifierValue> FaceModifiers { get; init; }
    public required IReadOnlyList<Ts4SimModifierValue> BodyModifiers { get; init; }
    public required IReadOnlyList<Ts4OutfitEntry> Outfits { get; init; }
    public required IReadOnlyList<DbpfResourceKey> GeneticSculpts { get; init; }
    public required IReadOnlyList<Ts4SimModifierValue> GeneticFaceModifiers { get; init; }
    public required IReadOnlyList<Ts4SimModifierValue> GeneticBodyModifiers { get; init; }
    public required IReadOnlyList<float> GeneticPhysique { get; init; }
    public required IReadOnlyList<Ts4OutfitPartRef> GeneticParts { get; init; }
    public required IReadOnlyList<ulong> TraitRefs { get; init; }
}

public readonly record struct Ts4SimModifierValue(DbpfResourceKey Key, float Weight);

public sealed class Ts4OutfitEntry
{
    public required ulong OutfitId { get; init; }
    public required ulong OutfitFlags { get; init; }
    public required ulong CreatedTicks { get; init; }
    public required uint Category { get; init; }
    public required IReadOnlyList<Ts4OutfitPartRef> Parts { get; init; }
}

public readonly record struct Ts4OutfitPartRef(DbpfResourceKey CasPartKey, uint BodyType, ulong ColorShift);
