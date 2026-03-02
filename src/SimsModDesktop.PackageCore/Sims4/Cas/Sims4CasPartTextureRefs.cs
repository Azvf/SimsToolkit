namespace SimsModDesktop.PackageCore;

public sealed class Sims4CasPartTextureRefs
{
    public DbpfResourceKey? Diffuse { get; init; }
    public DbpfResourceKey? Shadow { get; init; }
    public DbpfResourceKey? Normal { get; init; }
    public DbpfResourceKey? Specular { get; init; }
    public DbpfResourceKey? Emission { get; init; }
    public required IReadOnlyList<DbpfResourceKey> AllDistinct { get; init; }
}
