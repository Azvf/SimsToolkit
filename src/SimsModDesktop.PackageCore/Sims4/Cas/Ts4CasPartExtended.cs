namespace SimsModDesktop.PackageCore;

public sealed class Ts4CasPartExtended
{
    public required Sims4CasPartInfo BaseInfo { get; init; }
    public required byte ParameterFlags { get; init; }
    public required byte ParameterFlags2 { get; init; }
    public required uint MaterialHash { get; init; }
    public required uint TextureSpace { get; init; }
    public required byte RegionMapIndex { get; init; }
    public required IReadOnlyList<Ts4CasPartLodEntry> LodEntries { get; init; }
    public required IReadOnlyList<byte> SlotKeys { get; init; }
    public required IReadOnlyList<Ts4CasPartOverrideEntry> Overrides { get; init; }
    public DbpfResourceKey? RegionMapRef { get; init; }
}

public readonly record struct Ts4CasPartLodEntry(byte Lod, IReadOnlyList<DbpfResourceKey> MeshParts);

public readonly record struct Ts4CasPartOverrideEntry(byte Index, uint Value);
