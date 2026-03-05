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
    public required uint BodyType { get; init; }
    public required ulong ColorShift { get; init; }
    public Ts4CasPartExtended? CasPart { get; init; }
    public required IReadOnlyList<DbpfResourceKey> TextureRefs { get; init; }
    public required IReadOnlyList<DbpfResourceKey> MeshRefs { get; init; }
    public DbpfResourceKey? RegionMapRef { get; init; }
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
