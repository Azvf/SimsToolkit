namespace SimsModDesktop.Application.Mods;

public sealed class ModItemFastIndexBuildResult
{
    public required ModPackageIndexState PackageState { get; init; }
    public required IReadOnlyList<ModIndexedItemRecord> Items { get; init; }
}
