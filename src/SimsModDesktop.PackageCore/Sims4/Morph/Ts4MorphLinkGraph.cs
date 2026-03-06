namespace SimsModDesktop.PackageCore;

public sealed class Ts4MorphReference
{
    public required DbpfResourceKey SourceKey { get; init; }
    public required IReadOnlyList<DbpfResourceKey> BgeoRefs { get; init; }
    public DbpfResourceKey? DmapShapeRef { get; init; }
    public DbpfResourceKey? DmapNormalRef { get; init; }
    public DbpfResourceKey? BoneDeltaRef { get; init; }
}

public enum Ts4MorphReferencedResourceKind
{
    Unknown = 0,
    BlendGeometry = 1,
    DeformerMap = 2,
    BoneDelta = 3
}

public sealed class Ts4MorphReferencedResourceHealth
{
    public required DbpfResourceKey Key { get; init; }
    public DbpfResourceKey? RequestedKey { get; init; }
    public DbpfResourceKey? ResolvedKey { get; init; }
    public Ts4ResourceResolution? Resolution { get; init; }
    public required Ts4MorphReferencedResourceKind Kind { get; init; }
    public required bool Exists { get; init; }
    public required bool HeaderParsed { get; init; }
    public string HeaderSummary { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public int BondAdjustmentCount { get; init; }
    public int BondMappedSlotCount { get; init; }
    public int BondUnmappedSlotCount { get; init; }
    public IReadOnlyList<Ts4BondAdjustmentInterpretation> BondAdjustments { get; init; } = Array.Empty<Ts4BondAdjustmentInterpretation>();
}

public sealed class Ts4MorphLinkGraph
{
    public required IReadOnlyDictionary<ulong, Ts4MorphReference> SimModifierLinks { get; init; }
    public required IReadOnlyDictionary<ulong, Ts4MorphReference> SculptLinks { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<Ts4MorphReferencedResourceHealth> ReferencedResources { get; init; }
}

public sealed class Ts4BondAdjustmentInterpretation
{
    public required uint SlotHash { get; init; }
    public required bool NameResolved { get; init; }
    public string BoneName { get; init; } = string.Empty;
    public required float OffsetX { get; init; }
    public required float OffsetY { get; init; }
    public required float OffsetZ { get; init; }
    public required float ScaleX { get; init; }
    public required float ScaleY { get; init; }
    public required float ScaleZ { get; init; }
}
