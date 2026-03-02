namespace SimsModDesktop.Application.Mods;

public interface IModItemIndexScheduler
{
    event EventHandler<ModFastBatchAppliedEventArgs>? FastBatchApplied;
    event EventHandler<ModEnrichmentAppliedEventArgs>? EnrichmentApplied;
    event EventHandler? AllWorkCompleted;

    bool IsFastPassRunning { get; }
    bool IsDeepPassRunning { get; }

    Task QueueRefreshAsync(
        IReadOnlyList<string> packagePaths,
        CancellationToken cancellationToken = default);
}
