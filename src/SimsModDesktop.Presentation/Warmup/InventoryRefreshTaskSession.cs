namespace SimsModDesktop.Presentation.Warmup;

internal sealed class InventoryRefreshTaskSession
{
    public required string ModsRoot { get; init; }
    public required CancellationTokenSource WorkerCts { get; init; }
    public required Task<ModPackageInventoryRefreshResult> Task { get; init; }
}
