using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Warmup;
using SimsModDesktop.Presentation.Diagnostics;
using SimsModDesktop.Presentation.ViewModels;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Presentation.Warmup;

public sealed class TrayWarmupService : ITrayWarmupService
{
    private const string TrayDependencySnapshotPrewarmJobType = "TrayDependencySnapshotPrewarm";
    private readonly MainWindowCacheWarmupController _controller;
    private readonly ILogger<TrayWarmupService> _logger;
    private readonly ConcurrentDictionary<string, PackageIndexSnapshot> _readySnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WarmupStateSnapshot> _warmupStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _queuedPrewarmRoots = new(StringComparer.OrdinalIgnoreCase);

    public TrayWarmupService(
        MainWindowCacheWarmupController controller,
        ILogger<TrayWarmupService>? logger = null)
    {
        _controller = controller;
        _logger = logger ?? NullLogger<TrayWarmupService>.Instance;
        _controller.ModsRootInvalidated += OnModsRootInvalidated;
    }

    public Task<PackageIndexSnapshot> EnsureDependencyReadyAsync(
        string modsRootPath,
        CacheWarmupObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        return EnsureDependencyReadyCoreAsync(
            modsRootPath,
            MainWindowCacheWarmupController.CreateHost(observer),
            cancellationToken);
    }

    public Task<PackageIndexSnapshot?> AttachToInflightDependencyWarmupIfAny(
        string modsRootPath,
        CacheWarmupObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        return AttachToInflightDependencyWarmupIfAnyCoreAsync(
            modsRootPath,
            MainWindowCacheWarmupController.CreateHost(observer),
            cancellationToken);
    }

    public bool QueueDependencyIdlePrewarm(string modsRootPath, string trigger)
    {
        if (_controller.BackgroundCachePrewarmCoordinator is null || string.IsNullOrWhiteSpace(modsRootPath))
        {
            return false;
        }

        var normalizedRoot = _controller.ResolveDirectoryPath(modsRootPath);
        if (string.IsNullOrWhiteSpace(normalizedRoot) || !Directory.Exists(normalizedRoot))
        {
            return false;
        }

        var queued = _controller.BackgroundCachePrewarmCoordinator.TryQueue(
            BuildTrayPrewarmJobKey(normalizedRoot),
            cancellationToken => EnsureDependencyReadyCoreAsync(
                normalizedRoot,
                _controller.CreateDetachedWarmupHost("traycache", trigger),
                cancellationToken),
            $"Tray dependency snapshot prewarm for {normalizedRoot}");
        if (queued)
        {
            _queuedPrewarmRoots[normalizedRoot] = 0;
            _logger.LogInformation(
                "traycache.prewarm.queue modsRoot={ModsRoot} trigger={Trigger}",
                normalizedRoot,
                trigger);
        }

        return queued;
    }

    public bool TryGetWarmupState(string modsRootPath, out WarmupStateSnapshot? state)
    {
        state = null;
        if (string.IsNullOrWhiteSpace(modsRootPath))
        {
            return false;
        }

        return _warmupStates.TryGetValue(_controller.ResolveDirectoryPath(modsRootPath), out state);
    }

    public bool TryGetReadySnapshot(string modsRootPath, out PackageIndexSnapshot? snapshot)
    {
        snapshot = null;
        if (string.IsNullOrWhiteSpace(modsRootPath))
        {
            return false;
        }

        return _readySnapshots.TryGetValue(_controller.ResolveDirectoryPath(modsRootPath), out snapshot);
    }

    public void Reset()
    {
        foreach (var root in EnumerateKnownRoots())
        {
            InvalidateRoot(root, "reset");
        }

        _readySnapshots.Clear();
        _warmupStates.Clear();
        _queuedPrewarmRoots.Clear();
    }

    private async Task<PackageIndexSnapshot> EnsureDependencyReadyCoreAsync(
        string modsRootPath,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsRootPath);
        ArgumentNullException.ThrowIfNull(host);

        var resolvedRoot = _controller.ResolveDirectory(modsRootPath);
        var normalizedRoot = resolvedRoot.CanonicalPath;
        host.AppendLog(
            $"[path.resolve] component=traycache.warmup rawPath={resolvedRoot.FullPath} canonicalPath={resolvedRoot.CanonicalPath} exists={resolvedRoot.Exists} isReparse={resolvedRoot.IsReparsePoint} linkTarget={resolvedRoot.LinkTarget ?? string.Empty}");

        var inventory = await _controller.EnsureInventoryAsync(normalizedRoot, host, cancellationToken).ConfigureAwait(false);
        if (_readySnapshots.TryGetValue(normalizedRoot, out var readySnapshot) &&
            readySnapshot.InventoryVersion == inventory.Snapshot.InventoryVersion)
        {
            var readyProgress = BuildReadyProgress(readySnapshot);
            host.ReportProgress(readyProgress);
            _warmupStates[normalizedRoot] = new WarmupStateSnapshot
            {
                State = WarmupRunState.Completed,
                InventoryVersion = inventory.Snapshot.InventoryVersion,
                Message = "Warmup completed.",
                Progress = readyProgress
            };
            return readySnapshot;
        }

        CleanupStaleSessions(normalizedRoot, inventory.Snapshot.InventoryVersion);
        var sessionKey = WarmupSessionKey.ForTrayRoot(normalizedRoot, inventory.Snapshot.InventoryVersion);
        var rootGate = _controller.GetRootGate(normalizedRoot);
        WarmupTaskSession<PackageIndexSnapshot> session;
        await rootGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_controller.SessionRegistry.TryGet<PackageIndexSnapshot>(sessionKey, out var existingSession) &&
                existingSession is not null &&
                existingSession.State == WarmupRunState.Running &&
                !existingSession.Task.IsCompleted)
            {
                session = existingSession;
                session.PublishLog($"[traycache.snapshot.inflight.wait] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion}");
            }
            else
            {
                session = CreateWarmupTaskSession(normalizedRoot, inventory);
                _controller.SessionRegistry.Set(sessionKey, session);
            }
        }
        finally
        {
            rootGate.Release();
        }

        var hostHandle = session.AttachHost(host);
        try
        {
            return await session.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            session.DetachHost(hostHandle);
        }
    }

    private async Task<PackageIndexSnapshot?> AttachToInflightDependencyWarmupIfAnyCoreAsync(
        string modsRootPath,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsRootPath);
        ArgumentNullException.ThrowIfNull(host);

        var normalizedRoot = _controller.ResolveDirectoryPath(modsRootPath);
        var session = _controller.SessionRegistry.FindByDomainAndSource<PackageIndexSnapshot>(
                normalizedRoot,
                CacheWarmupDomain.TrayDependency)
            .Select(entry => entry.Value)
            .Where(candidate => candidate.State == WarmupRunState.Running && !candidate.Task.IsCompleted)
            .OrderByDescending(candidate => candidate.InventoryVersion)
            .FirstOrDefault();
        if (session is null)
        {
            return null;
        }

        var hostHandle = session.AttachHost(host);
        try
        {
            return await session.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            session.DetachHost(hostHandle);
        }
    }

    private WarmupTaskSession<PackageIndexSnapshot> CreateWarmupTaskSession(
        string normalizedRoot,
        ModPackageInventoryRefreshResult inventory)
    {
        var sessionKey = WarmupSessionKey.ForTrayRoot(normalizedRoot, inventory.Snapshot.InventoryVersion);
        var session = new WarmupTaskSession<PackageIndexSnapshot>
        {
            WarmupKey = sessionKey.ToString(),
            ModsRoot = normalizedRoot,
            InventoryVersion = inventory.Snapshot.InventoryVersion,
            Domain = CacheWarmupDomain.TrayDependency,
            WorkerCts = new CancellationTokenSource(),
            Task = Task.FromResult(new PackageIndexSnapshot
            {
                ModsRootPath = normalizedRoot,
                InventoryVersion = inventory.Snapshot.InventoryVersion,
                Packages = Array.Empty<IndexedPackageFile>()
            })
        };
        session.MarkRunning("Warmup running.");
        _warmupStates[normalizedRoot] = session.ToStateSnapshot();
        session.Task = RunWarmupSessionAsync(normalizedRoot, inventory, session);
        _ = session.Task.ContinueWith(
            _ =>
            {
                session.WorkerCts.Dispose();
                _warmupStates[normalizedRoot] = session.ToStateSnapshot();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return session;
    }

    private async Task<PackageIndexSnapshot> RunWarmupSessionAsync(
        string normalizedRoot,
        ModPackageInventoryRefreshResult inventory,
        WarmupTaskSession<PackageIndexSnapshot> session)
    {
        try
        {
            var snapshot = await LoadOrBuildTraySnapshotAsync(
                normalizedRoot,
                inventory,
                session,
                session.WorkerCts.Token).ConfigureAwait(false);

            _readySnapshots[normalizedRoot] = snapshot;
            var readyProgress = BuildReadyProgress(snapshot);
            session.PublishProgress(readyProgress);
            session.PublishLog(
                $"[traycache.ready] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={snapshot.Packages.Count}");
            session.MarkCompleted(readyProgress, "Warmup completed.");
            _warmupStates[normalizedRoot] = session.ToStateSnapshot();
            return snapshot;
        }
        catch (OperationCanceledException) when (session.WorkerCts.IsCancellationRequested)
        {
            session.MarkPaused("Warmup paused.");
            session.PublishProgress(MainWindowCacheWarmupController.BuildPausedProgress(
                CacheWarmupDomain.TrayDependency,
                session.LastProgress,
                "Tray warmup paused. Switch back to resume."));
            _warmupStates[normalizedRoot] = session.ToStateSnapshot();
            throw;
        }
        catch (Exception ex)
        {
            session.MarkFailed(ex, "Warmup failed.");
            _warmupStates[normalizedRoot] = session.ToStateSnapshot();
            throw;
        }
    }

    private async Task<PackageIndexSnapshot> LoadOrBuildTraySnapshotAsync(
        string normalizedRoot,
        ModPackageInventoryRefreshResult inventory,
        WarmupTaskSession<PackageIndexSnapshot> session,
        CancellationToken cancellationToken)
    {
        PackageIndexSnapshot? snapshot;
        using var timing = PerformanceLogScope.Begin(
            _logger,
            "traycache.snapshot",
            ("modsRoot", normalizedRoot),
            ("inventoryVersion", inventory.Snapshot.InventoryVersion),
            ("packageCount", inventory.Snapshot.Entries.Count));

        using var loadTiming = PerformanceLogScope.Begin(
            _logger,
            "traycache.snapshot.load",
            ("modsRoot", normalizedRoot),
            ("inventoryVersion", inventory.Snapshot.InventoryVersion));
        try
        {
            snapshot = await _controller.PackageIndexCache.TryLoadSnapshotAsync(
                normalizedRoot,
                inventory.Snapshot.InventoryVersion,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            loadTiming.Cancel("load-cancelled");
            throw;
        }
        catch (Exception ex)
        {
            loadTiming.Fail(ex, "load-failed");
            throw;
        }

        if (snapshot is not null)
        {
            session.PublishLog(
                $"[traycache.snapshot.load-hit] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={snapshot.Packages.Count}");
            loadTiming.Success("load-hit", ("packageCount", snapshot.Packages.Count));
            timing.Mark("snapshot.load-hit", ("packageCount", snapshot.Packages.Count));
            timing.Success("ready", ("inventoryVersion", inventory.Snapshot.InventoryVersion), ("packageCount", snapshot.Packages.Count));
            return snapshot;
        }

        loadTiming.Skip("load-miss");
        session.PublishLog(
            $"[traycache.snapshot.build.start] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={inventory.Snapshot.Entries.Count}");
        using var buildTiming = PerformanceLogScope.Begin(
            _logger,
            "traycache.snapshot.build",
            ("modsRoot", normalizedRoot),
            ("inventoryVersion", inventory.Snapshot.InventoryVersion),
            ("packageCount", inventory.Snapshot.Entries.Count));
        var stageStopwatch = new Stopwatch();
        var currentStageLabel = string.Empty;
        var currentStagePercent = 0;
        var useRound2TrayCachePipeline = await _controller.GetRound2ConfigBoolAsync(
            "Performance.Round2.TrayCachePipelineEnabled",
            true,
            cancellationToken).ConfigureAwait(false);
        var parseWorkers = await _controller.GetConfigIntAsync("Performance.TrayCache.ParseWorkers", null, cancellationToken).ConfigureAwait(false);
        var writeBatchSize = await _controller.GetConfigIntAsync("Performance.TrayCache.WriteBatchSize", null, cancellationToken).ConfigureAwait(false);
        var parseResultChannelCapacity = await _controller.GetConfigIntAsync("Performance.TrayCache.ParseResultChannelCapacity", null, cancellationToken).ConfigureAwait(false);
        var commitBatchSize = await _controller.GetConfigIntAsync("Performance.TrayCache.CommitBatchSize", null, cancellationToken).ConfigureAwait(false);
        var commitIntervalMs = await _controller.GetConfigIntAsync("Performance.TrayCache.CommitIntervalMs", null, cancellationToken).ConfigureAwait(false);
        var useAdaptiveThrottleV2 = await _controller.GetRound2ConfigBoolAsync(
            "Performance.Round2.TrayCacheThrottleV2Enabled",
            true,
            cancellationToken).ConfigureAwait(false);
        var useIncrementalOrphanCleanup = await _controller.GetRound2ConfigBoolAsync(
            "Performance.TrayCache.IncrementalOrphanCleanup",
            true,
            cancellationToken).ConfigureAwait(false);
        try
        {
            snapshot = await _controller.PackageIndexCache.BuildSnapshotAsync(
                new PackageIndexBuildRequest
                {
                    ModsRootPath = normalizedRoot,
                    InventoryVersion = inventory.Snapshot.InventoryVersion,
                    PackageFiles = inventory.Snapshot.Entries
                        .Select(entry => new PackageIndexBuildFile
                        {
                            FilePath = entry.PackagePath,
                            Length = entry.FileLength,
                            LastWriteUtcTicks = entry.LastWriteUtcTicks
                        })
                        .ToArray(),
                    ChangedPackageFiles = inventory.AddedEntries
                        .Concat(inventory.ChangedEntries)
                        .Select(entry => new PackageIndexBuildFile
                        {
                            FilePath = entry.PackagePath,
                            Length = entry.FileLength,
                            LastWriteUtcTicks = entry.LastWriteUtcTicks
                        })
                        .ToArray(),
                    RemovedPackagePaths = inventory.RemovedPackagePaths,
                    ParseWorkerCount = parseWorkers,
                    WriteBatchSize = writeBatchSize,
                    ParseResultChannelCapacity = parseResultChannelCapacity,
                    CommitBatchSize = commitBatchSize,
                    CommitIntervalMs = commitIntervalMs,
                    UseAdaptiveThrottleV2 = useAdaptiveThrottleV2,
                    UseIncrementalOrphanCleanup = useIncrementalOrphanCleanup,
                    UseParallelPipeline = useRound2TrayCachePipeline
                },
                new Progress<TrayDependencyExportProgress>(progress =>
                {
                    var stageLabel = BuildTrayCacheStageLabel(progress);
                    if (!string.Equals(currentStageLabel, stageLabel, StringComparison.Ordinal))
                    {
                        if (stageStopwatch.IsRunning && !string.IsNullOrWhiteSpace(currentStageLabel))
                        {
                            session.PublishLog(
                                $"[traycache.stage.done] stage={currentStageLabel} percent={currentStagePercent} elapsedMs={stageStopwatch.ElapsedMilliseconds}");
                        }

                        currentStageLabel = stageLabel;
                        stageStopwatch.Restart();
                        session.PublishLog($"[traycache.stage.start] stage={currentStageLabel} percent={progress.Percent}");
                    }

                    currentStagePercent = progress.Percent;
                    session.PublishProgress(new CacheWarmupProgress
                    {
                        Domain = CacheWarmupDomain.TrayDependency,
                        Stage = progress.Stage.ToString(),
                        Percent = progress.Percent,
                        Current = 0,
                        Total = inventory.Snapshot.Entries.Count,
                        Detail = string.IsNullOrWhiteSpace(progress.Detail)
                            ? "Preparing tray dependency cache..."
                            : progress.Detail,
                        IsBlocking = true
                    });
                    _warmupStates[normalizedRoot] = session.ToStateSnapshot();
                }),
                cancellationToken).ConfigureAwait(false);

            if (stageStopwatch.IsRunning && !string.IsNullOrWhiteSpace(currentStageLabel))
            {
                session.PublishLog(
                    $"[traycache.stage.done] stage={currentStageLabel} percent={currentStagePercent} elapsedMs={stageStopwatch.ElapsedMilliseconds}");
            }

            buildTiming.Success("build-done", ("packageCount", snapshot.Packages.Count));
        }
        catch (OperationCanceledException)
        {
            if (stageStopwatch.IsRunning && !string.IsNullOrWhiteSpace(currentStageLabel))
            {
                session.PublishLog(
                    $"[traycache.stage.cancel] stage={currentStageLabel} percent={currentStagePercent} elapsedMs={stageStopwatch.ElapsedMilliseconds}");
            }

            buildTiming.Cancel("build-cancelled");
            throw;
        }
        catch (Exception ex)
        {
            if (stageStopwatch.IsRunning && !string.IsNullOrWhiteSpace(currentStageLabel))
            {
                session.PublishLog(
                    $"[traycache.stage.fail] stage={currentStageLabel} percent={currentStagePercent} elapsedMs={stageStopwatch.ElapsedMilliseconds}");
            }

            buildTiming.Fail(ex, "build-failed");
            throw;
        }

        session.PublishLog(
            $"[traycache.snapshot.build.done] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={snapshot.Packages.Count}");
        timing.Mark("snapshot.build-done", ("packageCount", snapshot.Packages.Count));
        timing.Success("ready", ("inventoryVersion", inventory.Snapshot.InventoryVersion), ("packageCount", snapshot.Packages.Count));
        return snapshot;
    }

    private void CleanupStaleSessions(string normalizedRoot, long inventoryVersion)
    {
        foreach (var entry in _controller.SessionRegistry.FindByDomainAndSource<PackageIndexSnapshot>(
                     normalizedRoot,
                     CacheWarmupDomain.TrayDependency).ToArray())
        {
            if (entry.Value.InventoryVersion == inventoryVersion)
            {
                continue;
            }

            MainWindowCacheWarmupController.SafeCancelToken(entry.Value.WorkerCts);
            _controller.SessionRegistry.TryRemove(entry.Key, out WarmupTaskSession<PackageIndexSnapshot>? _);
        }
    }

    private void InvalidateRoot(string normalizedRoot, string reason)
    {
        _controller.BackgroundCachePrewarmCoordinator?.CancelBySource(
            normalizedRoot,
            reason,
            TrayDependencySnapshotPrewarmJobType);
        foreach (var entry in _controller.SessionRegistry.FindByDomainAndSource<PackageIndexSnapshot>(
                     normalizedRoot,
                     CacheWarmupDomain.TrayDependency).ToArray())
        {
            MainWindowCacheWarmupController.SafeCancelToken(entry.Value.WorkerCts);
            _controller.SessionRegistry.TryRemove(entry.Key, out WarmupTaskSession<PackageIndexSnapshot>? _);
        }

        _readySnapshots.TryRemove(normalizedRoot, out _);
        _warmupStates.TryRemove(normalizedRoot, out _);
        _queuedPrewarmRoots.TryRemove(normalizedRoot, out _);
    }

    private void OnModsRootInvalidated(string normalizedRoot, string reason, string changedPath)
    {
        InvalidateRoot(normalizedRoot, $"invalidate:{reason}");
    }

    private static CacheWarmupProgress BuildReadyProgress(PackageIndexSnapshot snapshot)
    {
        return new CacheWarmupProgress
        {
            Domain = CacheWarmupDomain.TrayDependency,
            Stage = "ready",
            Percent = 100,
            Current = snapshot.Packages.Count,
            Total = snapshot.Packages.Count,
            Detail = "Tray dependency cache is ready.",
            IsBlocking = true
        };
    }

    private static BackgroundPrewarmJobKey BuildTrayPrewarmJobKey(string normalizedRoot)
    {
        return new BackgroundPrewarmJobKey
        {
            JobType = TrayDependencySnapshotPrewarmJobType,
            SourceKey = normalizedRoot
        };
    }

    private static string BuildTrayCacheStageLabel(TrayDependencyExportProgress progress)
    {
        var detail = progress.Detail?.Trim() ?? string.Empty;
        if (detail.Length == 0)
        {
            return progress.Stage.ToString();
        }

        var markerIndex = detail.IndexOf("...", StringComparison.Ordinal);
        if (markerIndex >= 0)
        {
            return detail[..(markerIndex + 3)];
        }

        return detail;
    }

    private IEnumerable<string> EnumerateKnownRoots()
    {
        return _warmupStates.Keys
            .Concat(_readySnapshots.Keys)
            .Concat(_queuedPrewarmRoots.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
