namespace SimsModDesktop.Application.Mods;

public sealed class ModEnrichmentAppliedEventArgs : EventArgs
{
    public required IReadOnlyList<string> PackagePaths { get; init; }
    public required IReadOnlyList<string> AffectedItemKeys { get; init; }
}
