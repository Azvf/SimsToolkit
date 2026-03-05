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
    public required Ts4MorphReferencedResourceKind Kind { get; init; }
    public required bool Exists { get; init; }
    public required bool HeaderParsed { get; init; }
    public string HeaderSummary { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}

public sealed class Ts4MorphLinkGraph
{
    public required IReadOnlyDictionary<ulong, Ts4MorphReference> SimModifierLinks { get; init; }
    public required IReadOnlyDictionary<ulong, Ts4MorphReference> SculptLinks { get; init; }
    public required IReadOnlyList<string> Issues { get; init; }
    public required IReadOnlyList<Ts4MorphReferencedResourceHealth> ReferencedResources { get; init; }
}
