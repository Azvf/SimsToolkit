namespace SimsModDesktop.Application.Mods;

public sealed class ModItemEnrichmentBatch
{
    public required ModPackageIndexState PackageState { get; init; }
    public required IReadOnlyList<ModIndexedItemRecord> Items { get; init; }
    public required IReadOnlyList<string> AffectedItemKeys { get; init; }
}
