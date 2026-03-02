namespace SimsModDesktop.Application.Mods;

public sealed class ModFastBatchAppliedEventArgs : EventArgs
{
    public required IReadOnlyList<string> PackagePaths { get; init; }
}
