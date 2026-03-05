using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.PackageCore.Performance;
using System.Diagnostics;

namespace SimsModDesktop.Application.Mods;

public sealed class ModItemIndexScheduler : IModItemIndexScheduler
{
    private const int FastBatchSize = 8;
    private const int DeepBatchSize = 4;
    private readonly IModItemIndexStore _store;
    private readonly IFastModItemIndexService _fastIndexService;
    private readonly IDeepModItemEnrichmentService _deepEnrichmentService;
    private readonly ILogger<ModItemIndexScheduler> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ModItemIndexScheduler(
        IModItemIndexStore store,
        IFastModItemIndexService fastIndexService,
        IDeepModItemEnrichmentService deepEnrichmentService,
        ILogger<ModItemIndexScheduler>? logger = null)
    {
        _store = store;
        _fastIndexService = fastIndexService;
        _deepEnrichmentService = deepEnrichmentService;
        _logger = logger ?? NullLogger<ModItemIndexScheduler>.Instance;
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
            var parseContextCache = new ConcurrentDictionary<string, Lazy<Task<ModPackageParseContext>>>(StringComparer.OrdinalIgnoreCase);
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
                var fastWorkerCount = PerformanceWorkerSizer.ResolveModsFastWorkers(request.FastWorkerCount);
                var fastStartedAt = DateTime.UtcNow;
                var fastResults = await ParsePackagesAsync(
                    fastPassPackages,
                    fastWorkerCount,
                    stage: "fast",
                    (packagePath, token) => BuildFastPackageAsync(packagePath, parseContextCache, token),
                    cancellationToken).ConfigureAwait(false);
                _logger.LogInformation(
                    "modcache.fastindex.batch PackageCount={PackageCount} WorkerCount={WorkerCount} ElapsedMs={ElapsedMs}",
                    fastResults.Count,
                    fastWorkerCount,
                    (DateTime.UtcNow - fastStartedAt).TotalMilliseconds);

                await PersistFastBatchesAsync(
                    fastResults,
                    request.AllowDeepEnrichment,
                    progress,
                    cancellationToken).ConfigureAwait(false);

                IsFastPassRunning = false;
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
            var deepWorkerCount = PerformanceWorkerSizer.ResolveModsDeepWorkers(request.DeepWorkerCount);
            var deepStartedAt = DateTime.UtcNow;
            var deepResults = await ParsePackagesAsync(
                deepPassPackages,
                deepWorkerCount,
                stage: "deep",
                (packagePath, token) => EnrichPackageAsync(packagePath, parseContextCache, token),
                cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "modcache.deepindex.batch PackageCount={PackageCount} WorkerCount={WorkerCount} ElapsedMs={ElapsedMs}",
                deepResults.Count,
                deepWorkerCount,
                (DateTime.UtcNow - deepStartedAt).TotalMilliseconds);

            await PersistDeepBatchesAsync(
                deepResults,
                progress,
                cancellationToken).ConfigureAwait(false);
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

    private async Task<ModItemFastIndexBuildResult> BuildFastPackageAsync(
        string packagePath,
        ConcurrentDictionary<string, Lazy<Task<ModPackageParseContext>>> parseContextCache,
        CancellationToken cancellationToken)
    {
        if (_fastIndexService is IContextAwareFastModItemIndexService contextAwareFast)
        {
            var parseContext = await GetOrCreateParseContextAsync(packagePath, parseContextCache).ConfigureAwait(false);
            return await contextAwareFast.BuildFastPackageAsync(parseContext, cancellationToken).ConfigureAwait(false);
        }

        return await _fastIndexService.BuildFastPackageAsync(packagePath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ModItemEnrichmentBatch> EnrichPackageAsync(
        string packagePath,
        ConcurrentDictionary<string, Lazy<Task<ModPackageParseContext>>> parseContextCache,
        CancellationToken cancellationToken)
    {
        if (_deepEnrichmentService is IContextAwareDeepModItemEnrichmentService contextAwareDeep)
        {
            var parseContext = await GetOrCreateParseContextAsync(packagePath, parseContextCache).ConfigureAwait(false);
            return await contextAwareDeep.EnrichPackageAsync(parseContext, cancellationToken).ConfigureAwait(false);
        }

        return await _deepEnrichmentService.EnrichPackageAsync(packagePath, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistFastBatchesAsync(
        IReadOnlyList<ModItemFastIndexBuildResult> fastResults,
        bool allowDeepEnrichment,
        IProgress<ModIndexRefreshProgress>? progress,
        CancellationToken cancellationToken)
    {
        var processed = 0;
        foreach (var batch in Chunk(fastResults, FastBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startedAt = DateTime.UtcNow;
            await _store.ReplacePackagesFastAsync(batch, cancellationToken).ConfigureAwait(false);
            processed += batch.Count;
            _logger.LogInformation(
                "modcache.storewriter.batch Stage={Stage} BatchSize={BatchSize} Current={Current} Total={Total} ElapsedMs={ElapsedMs}",
                "fast",
                batch.Count,
                processed,
                fastResults.Count,
                (DateTime.UtcNow - startedAt).TotalMilliseconds);
            FastBatchApplied?.Invoke(this, new ModFastBatchAppliedEventArgs
            {
                PackagePaths = batch.Select(item => item.PackageState.PackagePath).ToArray()
            });
            progress?.Report(new ModIndexRefreshProgress
            {
                Stage = "fast",
                Percent = allowDeepEnrichment
                    ? ScaleProgress(10, 55, processed, fastResults.Count)
                    : ScaleProgress(10, 100, processed, fastResults.Count),
                Current = processed,
                Total = fastResults.Count,
                Detail = $"Fast indexing {processed}/{fastResults.Count}"
            });
        }
    }

    private async Task PersistDeepBatchesAsync(
        IReadOnlyList<ModItemEnrichmentBatch> deepResults,
        IProgress<ModIndexRefreshProgress>? progress,
        CancellationToken cancellationToken)
    {
        var processed = 0;
        foreach (var batch in Chunk(deepResults, DeepBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startedAt = DateTime.UtcNow;
            await _store.ApplyItemEnrichmentBatchesAsync(batch, cancellationToken).ConfigureAwait(false);
            processed += batch.Count;
            _logger.LogInformation(
                "modcache.storewriter.batch Stage={Stage} BatchSize={BatchSize} Current={Current} Total={Total} ElapsedMs={ElapsedMs}",
                "deep",
                batch.Count,
                processed,
                deepResults.Count,
                (DateTime.UtcNow - startedAt).TotalMilliseconds);
            EnrichmentApplied?.Invoke(this, new ModEnrichmentAppliedEventArgs
            {
                PackagePaths = batch.Select(item => item.PackageState.PackagePath).ToArray(),
                AffectedItemKeys = batch.SelectMany(item => item.AffectedItemKeys).ToArray()
            });
            progress?.Report(new ModIndexRefreshProgress
            {
                Stage = "deep",
                Percent = ScaleProgress(55, 100, processed, deepResults.Count),
                Current = processed,
                Total = deepResults.Count,
                Detail = $"Deep enriching {processed}/{deepResults.Count}"
            });
        }
    }

    private async Task<IReadOnlyList<T>> ParsePackagesAsync<T>(
        IReadOnlyList<string> packagePaths,
        int workerCount,
        string stage,
        Func<string, CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        if (packagePaths.Count == 0)
        {
            return Array.Empty<T>();
        }

        var indexedPackages = packagePaths
            .Select((path, index) => new IndexedPackagePath(index, path))
            .ToArray();
        var results = new ConcurrentBag<IndexedPackageResult<T>>();
        var queue = new ConcurrentQueue<IndexedPackagePath>(indexedPackages);
        var baselineWorkingSet = Process.GetCurrentProcess().WorkingSet64;
        var minWorkers = string.Equals(stage, "deep", StringComparison.OrdinalIgnoreCase) ? 3 : 4;
        var throttle = new PerformanceAdaptiveThrottle(
            targetWorkers: workerCount,
            minWorkers: Math.Min(minWorkers, workerCount),
            startedAtUtc: DateTime.UtcNow);
        var allowedWorkers = workerCount;
        long processedCount = 0;

        using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var monitorTask = Task.Run(async () =>
        {
            while (!monitorCts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), monitorCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var decision = throttle.Update(
                    totalCompletedCount: Interlocked.Read(ref processedCount),
                    nowUtc: DateTime.UtcNow,
                    workingSetBytes: Process.GetCurrentProcess().WorkingSet64,
                    baselineWorkingSetBytes: baselineWorkingSet);
                if (!decision.Changed)
                {
                    continue;
                }

                Interlocked.Exchange(ref allowedWorkers, decision.RecommendedWorkers);
                _logger.LogInformation(
                    "modcache.dynamic.throttle Stage={Stage} WorkerCount={WorkerCount} Reason={Reason}",
                    stage,
                    decision.RecommendedWorkers,
                    decision.Reason);
            }
        }, CancellationToken.None);

        var workers = Enumerable.Range(0, workerCount)
            .Select(workerId => Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (workerId >= Volatile.Read(ref allowedWorkers))
                    {
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (!queue.TryDequeue(out var indexedPackage))
                    {
                        break;
                    }

                    var result = await factory(indexedPackage.PackagePath, cancellationToken).ConfigureAwait(false);
                    results.Add(new IndexedPackageResult<T>(indexedPackage.Index, result));
                    Interlocked.Increment(ref processedCount);
                }
            }, CancellationToken.None))
            .ToArray();
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        finally
        {
            monitorCts.Cancel();
            try
            {
                await monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        return results
            .OrderBy(item => item.Index)
            .Select(item => item.Result)
            .ToArray();
    }

    private static Task<ModPackageParseContext> GetOrCreateParseContextAsync(
        string packagePath,
        ConcurrentDictionary<string, Lazy<Task<ModPackageParseContext>>> parseContextCache)
    {
        var lazy = parseContextCache.GetOrAdd(
            Path.GetFullPath(packagePath),
            normalizedPath => new Lazy<Task<ModPackageParseContext>>(
                () => Task.Run(() => ModPackageParseContext.Create(normalizedPath)),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value;
    }

    private static IReadOnlyList<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> items, int batchSize)
    {
        if (items.Count == 0)
        {
            return Array.Empty<IReadOnlyList<T>>();
        }

        var chunks = new List<IReadOnlyList<T>>((items.Count + batchSize - 1) / batchSize);
        for (var index = 0; index < items.Count; index += batchSize)
        {
            var count = Math.Min(batchSize, items.Count - index);
            var chunk = new T[count];
            for (var inner = 0; inner < count; inner++)
            {
                chunk[inner] = items[index + inner];
            }

            chunks.Add(chunk);
        }

        return chunks;
    }

    private sealed record IndexedPackagePath(int Index, string PackagePath);

    private sealed record IndexedPackageResult<T>(int Index, T Result);
}
