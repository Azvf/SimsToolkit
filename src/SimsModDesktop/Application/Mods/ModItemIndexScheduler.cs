namespace SimsModDesktop.Application.Mods;

public sealed class ModItemIndexScheduler : IModItemIndexScheduler
{
    private readonly IModItemIndexStore _store;
    private readonly IFastModItemIndexService _fastIndexService;
    private readonly IDeepModItemEnrichmentService _deepEnrichmentService;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ModItemIndexScheduler(
        IModItemIndexStore store,
        IFastModItemIndexService fastIndexService,
        IDeepModItemEnrichmentService deepEnrichmentService)
    {
        _store = store;
        _fastIndexService = fastIndexService;
        _deepEnrichmentService = deepEnrichmentService;
    }

    public event EventHandler<ModFastBatchAppliedEventArgs>? FastBatchApplied;
    public event EventHandler<ModEnrichmentAppliedEventArgs>? EnrichmentApplied;
    public event EventHandler? AllWorkCompleted;

    public bool IsFastPassRunning { get; private set; }
    public bool IsDeepPassRunning { get; private set; }

    public async Task QueueRefreshAsync(
        IReadOnlyList<string> packagePaths,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var candidates = packagePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(path.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var packagesToRefresh = new List<string>();

            foreach (var fullPath in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(fullPath))
                {
                    await _store.DeletePackageAsync(fullPath, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var fileInfo = new FileInfo(fullPath);
                var packageState = await _store.TryGetPackageStateAsync(fullPath, cancellationToken).ConfigureAwait(false);
                if (packageState is not null &&
                    packageState.FileLength == fileInfo.Length &&
                    packageState.LastWriteUtcTicks == fileInfo.LastWriteTimeUtc.Ticks &&
                    string.Equals(packageState.Status, "Ready", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                packagesToRefresh.Add(fullPath);
            }

            if (packagesToRefresh.Count == 0)
            {
                return;
            }

            IsFastPassRunning = true;
            foreach (var fullPath in packagesToRefresh)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fast = await _fastIndexService.BuildFastPackageAsync(fullPath, cancellationToken).ConfigureAwait(false);
                await _store.ReplacePackageFastAsync(fast, cancellationToken).ConfigureAwait(false);
            }

            IsFastPassRunning = false;
            FastBatchApplied?.Invoke(this, new ModFastBatchAppliedEventArgs
            {
                PackagePaths = packagesToRefresh.ToArray()
            });

            IsDeepPassRunning = true;
            foreach (var fullPath in packagesToRefresh)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = await _deepEnrichmentService.EnrichPackageAsync(fullPath, cancellationToken).ConfigureAwait(false);
                await _store.ApplyItemEnrichmentBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                EnrichmentApplied?.Invoke(this, new ModEnrichmentAppliedEventArgs
                {
                    PackagePaths = [fullPath],
                    AffectedItemKeys = batch.AffectedItemKeys
                });
            }
        }
        finally
        {
            IsFastPassRunning = false;
            IsDeepPassRunning = false;
            _gate.Release();
            AllWorkCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
}
