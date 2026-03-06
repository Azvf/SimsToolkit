namespace SimsModDesktop.PackageCore;

public interface ITS4SimAppearanceService
{
    Task<Ts4SimAppearanceSnapshot> BuildSnapshotAsync(
        string savePath,
        string gameRoot,
        string modsRoot,
        CancellationToken cancellationToken = default);
}

public sealed class Ts4SimAppearanceSnapshot
{
    public required string SavePath { get; init; }
    public required DateTime LastWriteTimeLocal { get; init; }
    public required IReadOnlyList<Ts4SimAppearanceSim> Sims { get; init; }
    public required Ts4MorphLinkGraph MorphGraphSummary { get; init; }
    public required Ts4AppearanceResourceStats ResourceStats { get; init; }
    public required IReadOnlyList<Ts4AppearanceIssue> Issues { get; init; }
    public Ts4CasModifierTuningCatalog? ModifierTuningCatalog { get; init; }
    public Ts4RigBoneIndexSummary? RigBoneIndexSummary { get; init; }
}

public sealed class Ts4SimAppearanceSim
{
    public required ulong SimId { get; init; }
    public required string FullName { get; init; }
    public required uint Species { get; init; }
    public required int ModifierCount { get; init; }
    public required IReadOnlyList<Ts4OutfitAppearance> Outfits { get; init; }
    public DbpfResourceKey? SimInfoKey { get; init; }
    public Ts4SimInfoResource? SimInfo { get; init; }
    public Ts4ResourceResolution? SimInfoResolution { get; init; }
    public IReadOnlyList<Ts4SimModifierSemanticValue> ModifierSemantics { get; init; } = Array.Empty<Ts4SimModifierSemanticValue>();
    public DbpfResourceKey? ToneRef { get; init; }
    public Ts4Tone? Tone { get; init; }
    public Ts4ResourceResolution? ToneResolution { get; init; }
    public IReadOnlyList<Ts4SimPeltLayerAppearance> PeltLayers { get; init; } = Array.Empty<Ts4SimPeltLayerAppearance>();
}

public sealed class Ts4OutfitAppearance
{
    public required ulong OutfitId { get; init; }
    public required uint Category { get; init; }
    public required ulong OutfitFlags { get; init; }
    public required ulong CreatedTicks { get; init; }
    public required IReadOnlyList<Ts4OutfitPartAppearance> Parts { get; init; }
}

public sealed class Ts4OutfitPartAppearance
{
    public required DbpfResourceKey RequestedCasPartKey { get; init; }
    public DbpfResourceKey? ResolvedCasPartKey { get; init; }
    public Ts4ResourceResolution? CasPartResolution { get; init; }
    public required uint BodyType { get; init; }
    public required ulong ColorShift { get; init; }
    public Ts4CasPartExtended? CasPart { get; init; }
    public required IReadOnlyList<DbpfResourceKey> TextureRefs { get; init; }
    public required IReadOnlyList<DbpfResourceKey> MeshRefs { get; init; }
    public IReadOnlyList<Ts4OutfitMeshAppearance> Meshes { get; init; } = Array.Empty<Ts4OutfitMeshAppearance>();
    public IReadOnlyList<Ts4OutfitTextureAppearance> Textures { get; init; } = Array.Empty<Ts4OutfitTextureAppearance>();
    public DbpfResourceKey? RegionMapRef { get; init; }
    public DbpfResourceKey? ResolvedRegionMapKey { get; init; }
    public Ts4ResourceResolution? RegionMapResolution { get; init; }
    public Ts4RegionMap? RegionMap { get; init; }
}

public enum Ts4CasTextureSlot
{
    Unknown = 0,
    Diffuse = 1,
    Shadow = 2,
    Normal = 3,
    Specular = 4,
    Emission = 5
}

public sealed class Ts4OutfitMeshAppearance
{
    public required DbpfResourceKey RequestedMeshKey { get; init; }
    public DbpfResourceKey? ResolvedMeshKey { get; init; }
    public Ts4ResourceResolution? Resolution { get; init; }
    public Ts4Geom? Geom { get; init; }
}

public sealed class Ts4OutfitTextureAppearance
{
    public required Ts4CasTextureSlot Slot { get; init; }
    public required DbpfResourceKey RequestedTextureKey { get; init; }
    public DbpfResourceKey? ResolvedTextureKey { get; init; }
    public Ts4ResourceResolution? Resolution { get; init; }
    public Ts4TextureReadMetadata? Metadata { get; init; }
}

public sealed class Ts4AppearanceResourceStats
{
    public required int TotalReferences { get; init; }
    public required int ResolvedReferences { get; init; }
    public required int MissingReferences { get; init; }
    public required int ParseFailures { get; init; }
}

public sealed class Ts4AppearanceIssue
{
    public required string Code { get; init; }
    public required Ts4AppearanceIssueSeverity Severity { get; init; }
    public required Ts4AppearanceIssueScope Scope { get; init; }
    public required string Message { get; init; }
    public DbpfResourceKey? ResourceKey { get; init; }
    public ulong? SimId { get; init; }
    public Ts4ResourceResolution? Resolution { get; init; }
}

public enum Ts4AppearanceIssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public enum Ts4AppearanceIssueScope
{
    Snapshot = 0,
    Sim = 1,
    Outfit = 2,
    Part = 3,
    Morph = 4
}

public sealed class Ts4SimModifierSemanticValue
{
    public required ulong ModifierHash { get; init; }
    public required float Weight { get; init; }
    public required bool IsFaceModifier { get; init; }
    public required string ModifierName { get; init; }
    public float? Scale { get; init; }
    public bool SemanticResolved { get; init; }
}

public sealed class Ts4SimPeltLayerAppearance
{
    public required ulong LayerId { get; init; }
    public required uint Color { get; init; }
    public DbpfResourceKey? ResolvedPeltLayerKey { get; init; }
    public Ts4ResourceResolution? PeltLayerResolution { get; init; }
    public Ts4PeltLayer? PeltLayer { get; init; }
}

public sealed class Ts4RigBoneIndexSummary
{
    public required int ParsedRigCount { get; init; }
    public required int BoneHashCount { get; init; }
    public required int DuplicateHashCount { get; init; }
}
