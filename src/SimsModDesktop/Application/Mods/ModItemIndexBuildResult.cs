namespace SimsModDesktop.Application.Mods;

public sealed class ModItemIndexBuildResult
{
    public required ModPackageIndexState PackageState { get; init; }
    public required IReadOnlyList<ModIndexedItemRecord> Items { get; init; }
}
