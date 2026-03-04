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
        ModIndexRefreshRequest request,
        IProgress<ModIndexRefreshProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var changedCandidates = NormalizePaths(request.ChangedPackages);
            var priorityCandidates = NormalizePaths(request.PriorityPackages);
            var removedCandidates = NormalizePaths(request.RemovedPackages);
            foreach (var removedPath in removedCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _store.DeletePackageAsync(removedPath, cancellationToken).ConfigureAwait(false);
            }

            if (removedCandidates.Length > 0)
            {
                progress?.Report(new ModIndexRefreshProgress
                {
                    Stage = "remove",
                    Percent = request.AllowDeepEnrichment ? 5 : 10,
                    Current = removedCandidates.Length,
                    Total = removedCandidates.Length,
                    Detail = $"Removed stale package rows ({removedCandidates.Length})."
                });
            }

            var fastPassPackages = new List<string>();
            var deepOnlyPackages = new List<string>();
            var candidates = changedCandidates
                .Concat(priorityCandidates)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var fullPath in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileInfo = new FileInfo(fullPath);
                if (!fileInfo.Exists)
                {
                    await _store.DeletePackageAsync(fullPath, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var packageState = await _store.TryGetPackageStateAsync(fullPath, cancellationToken).ConfigureAwait(false);
                var hasMatchingFingerprint = packageState is not null &&
                                            packageState.FileLength == fileInfo.Length &&
                                            packageState.LastWriteUtcTicks == fileInfo.LastWriteTimeUtc.Ticks;
                if (!hasMatchingFingerprint)
                {
                    fastPassPackages.Add(fullPath);
                    continue;
                }

                if (string.Equals(packageState!.Status, "Ready", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(packageState.Status, "FastReady", StringComparison.OrdinalIgnoreCase))
                {
                    deepOnlyPackages.Add(fullPath);
                    continue;
                }

                fastPassPackages.Add(fullPath);
            }

            if (fastPassPackages.Count == 0 && (!request.AllowDeepEnrichment || deepOnlyPackages.Count == 0))
            {
                return;
            }

            if (fastPassPackages.Count > 0)
            {
                IsFastPassRunning = true;
                for (var index = 0; index < fastPassPackages.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fullPath = fastPassPackages[index];
                    var fast = await _fastIndexService.BuildFastPackageAsync(fullPath, cancellationToken).ConfigureAwait(false);
                    await _store.ReplacePackageFastAsync(fast, cancellationToken).ConfigureAwait(false);
                    progress?.Report(new ModIndexRefreshProgress
                    {
                        Stage = "fast",
                        Percent = request.AllowDeepEnrichment
                            ? ScaleProgress(10, 55, index + 1, fastPassPackages.Count)
                            : ScaleProgress(10, 100, index + 1, fastPassPackages.Count),
                        Current = index + 1,
                        Total = fastPassPackages.Count,
                        Detail = $"Fast indexing {index + 1}/{fastPassPackages.Count}"
                    });
                }

                IsFastPassRunning = false;
                FastBatchApplied?.Invoke(this, new ModFastBatchAppliedEventArgs
                {
                    PackagePaths = fastPassPackages.ToArray()
                });
            }

            if (!request.AllowDeepEnrichment)
            {
                return;
            }

            var deepPassPackages = priorityCandidates
                .Concat(fastPassPackages)
                .Concat(deepOnlyPackages)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (deepPassPackages.Length == 0)
            {
                return;
            }

            IsDeepPassRunning = true;
            for (var index = 0; index < deepPassPackages.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullPath = deepPassPackages[index];
                var batch = await _deepEnrichmentService.EnrichPackageAsync(fullPath, cancellationToken).ConfigureAwait(false);
                await _store.ApplyItemEnrichmentBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                EnrichmentApplied?.Invoke(this, new ModEnrichmentAppliedEventArgs
                {
                    PackagePaths = [fullPath],
                    AffectedItemKeys = batch.AffectedItemKeys
                });
                progress?.Report(new ModIndexRefreshProgress
                {
                    Stage = "deep",
                    Percent = ScaleProgress(55, 100, index + 1, deepPassPackages.Length),
                    Current = index + 1,
                    Total = deepPassPackages.Length,
                    Detail = $"Deep enriching {index + 1}/{deepPassPackages.Length}"
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

    private static string[] NormalizePaths(IReadOnlyList<string> packagePaths)
    {
        if (packagePaths.Count == 0)
        {
            return Array.Empty<string>();
        }

        return packagePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int ScaleProgress(int start, int end, int current, int total)
    {
        if (total <= 0)
        {
            return end;
        }

        var safeCurrent = Math.Clamp(current, 0, total);
        return start + (int)Math.Round((end - start) * (safeCurrent / (double)total), MidpointRounding.AwayFromZero);
    }
}
