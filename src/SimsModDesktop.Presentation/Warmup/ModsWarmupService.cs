using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.Warmup;
using SimsModDesktop.Presentation.ViewModels;

namespace SimsModDesktop.Presentation.Warmup;

public sealed class ModsWarmupService : IModsWarmupService
{
    private const string ModCatalogQueryPrimeJobType = "ModCatalogQueryPrime";
    private const string SourceKeyScopeSeparator = "\u001F";
    private readonly MainWindowCacheWarmupController _controller;
    private readonly ILogger<ModsWarmupService> _logger;
    private readonly ConcurrentDictionary<string, long> _readyInventoryVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WarmupStateSnapshot> _warmupStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _queuedPrewarmRoots = new(StringComparer.OrdinalIgnoreCase);

    public ModsWarmupService(
        MainWindowCacheWarmupController controller,
        ILogger<ModsWarmupService>? logger = null)
    {
        _controller = controller;
        _logger = logger ?? NullLogger<ModsWarmupService>.Instance;
        _controller.ModsRootInvalidated += OnModsRootInvalidated;
    }

    public Task<ModPackageInventoryRefreshResult> EnsureWorkspaceReadyAsync(
        string modsRootPath,
        CacheWarmupObserver? observer = null,
        CancellationToken cancellationToken = default)
    {
        return EnsureWorkspaceReadyCoreAsync(
            modsRootPath,
            MainWindowCacheWarmupController.CreateHost(observer),
            cancellationToken);
    }

    public void PauseWarmup(string modsRootPath, string reason)
    {
        if (string.IsNullOrWhiteSpace(modsRootPath))
        {
            return;
        }

        var normalizedRoot = _controller.ResolveDirectoryPath(modsRootPath);
        _controller.TryCancelInventoryRefresh(normalizedRoot);
        var message = string.IsNullOrWhiteSpace(reason) ? "paused" : reason;
        foreach (var entry in _controller.SessionRegistry.FindByDomainAndSource<ModPackageInventoryRefreshResult>(
                     normalizedRoot,
                     CacheWarmupDomain.ModsCatalog))
        {
            if (!entry.Value.RequestPause(message))
            {
                continue;
            }

            entry.Value.PublishLog($"[modcache.fastindex.pause] modsRoot={normalizedRoot} inventoryVersion={entry.Value.InventoryVersion} reason={message}");
            _warmupStates[normalizedRoot] = entry.Value.ToStateSnapshot();
            _logger.LogInformation(
                "modcache.fastindex.pause modsRoot={ModsRoot} inventoryVersion={InventoryVersion} reason={Reason}",
                normalizedRoot,
                entry.Value.InventoryVersion,
                message);
        }
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

    public bool QueueQueryIdlePrewarm(ModItemCatalogQuery query, string trigger)
    {
        if (_controller.BackgroundCachePrewarmCoordinator is null ||
            _controller.ModItemCatalogService is null ||
            string.IsNullOrWhiteSpace(query.ModsRoot))
        {
            return false;
        }

        var normalizedQuery = NormalizeQuery(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery.ModsRoot) || !Directory.Exists(normalizedQuery.ModsRoot))
        {
            return false;
        }

        var key = BuildModCatalogPrewarmJobKey(normalizedQuery);
        var queued = _controller.BackgroundCachePrewarmCoordinator.TryQueue(
            key,
            cancellationToken => PrimeModCatalogQueryAsync(normalizedQuery, trigger, cancellationToken),
            $"Mods catalog query prewarm for {normalizedQuery.ModsRoot}");
        if (queued)
        {
            _queuedPrewarmRoots[normalizedQuery.ModsRoot] = 0;
            _logger.LogInformation(
                "modquery.prewarm.queue modsRoot={ModsRoot} trigger={Trigger} fingerprint={Fingerprint}",
                normalizedQuery.ModsRoot,
                trigger,
                BuildModQueryFingerprint(normalizedQuery));
        }

        return queued;
    }

    public void QueuePriorityDeepEnrichment(string modsRootPath, IReadOnlyCollection<string> itemKeys)
    {
        if (string.IsNullOrWhiteSpace(modsRootPath) || itemKeys.Count == 0)
        {
            return;
        }

        var normalizedRoot = _controller.ResolveDirectoryPath(modsRootPath);
        if (!_readyInventoryVersions.ContainsKey(normalizedRoot))
        {
            return;
        }

        var priorities = itemKeys
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(_controller.ResolveFilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (priorities.Length == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _controller.IndexScheduler.QueueRefreshAsync(
                    new ModIndexRefreshRequest
                    {
                        ModsRootPath = normalizedRoot,
                        PriorityPackages = priorities,
                        AllowDeepEnrichment = true
                    }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "modcache.deepindex.priority.fail modsRoot={ModsRoot} priorityCount={PriorityCount}",
                    normalizedRoot,
                    priorities.Length);
            }
        });
    }

    public void Reset()
    {
        foreach (var root in EnumerateKnownRoots())
        {
            InvalidateRoot(root, "reset");
        }

        _readyInventoryVersions.Clear();
        _warmupStates.Clear();
        _queuedPrewarmRoots.Clear();
    }

    private async Task<ModPackageInventoryRefreshResult> EnsureWorkspaceReadyCoreAsync(
        string modsRootPath,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsRootPath);
        ArgumentNullException.ThrowIfNull(host);

        var resolvedRoot = _controller.ResolveDirectory(modsRootPath);
        var normalizedRoot = resolvedRoot.CanonicalPath;
        host.AppendLog(
            $"[path.resolve] component=modcache.warmup rawPath={resolvedRoot.FullPath} canonicalPath={resolvedRoot.CanonicalPath} exists={resolvedRoot.Exists} isReparse={resolvedRoot.IsReparsePoint} linkTarget={resolvedRoot.LinkTarget ?? string.Empty}");

        var inventory = await _controller.EnsureInventoryAsync(normalizedRoot, host, cancellationToken).ConfigureAwait(false);
        if (_readyInventoryVersions.TryGetValue(normalizedRoot, out var readyInventoryVersion) &&
            readyInventoryVersion == inventory.Snapshot.InventoryVersion)
        {
            var readyProgress = BuildReadyProgress(inventory);
            host.ReportProgress(readyProgress);
            _warmupStates[normalizedRoot] = new WarmupStateSnapshot
            {
                State = WarmupRunState.Completed,
                InventoryVersion = inventory.Snapshot.InventoryVersion,
                Message = "Warmup completed.",
                Progress = readyProgress
            };
            return inventory;
        }

        CleanupStaleSessions(normalizedRoot, inventory.Snapshot.InventoryVersion);
        var sessionKey = WarmupSessionKey.ForModsRoot(normalizedRoot, inventory.Snapshot.InventoryVersion);
        var rootGate = _controller.GetRootGate(normalizedRoot);
        WarmupTaskSession<ModPackageInventoryRefreshResult> session;
        await rootGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_controller.SessionRegistry.TryGet<ModPackageInventoryRefreshResult>(sessionKey, out var existingSession) &&
                existingSession is not null &&
                existingSession.State == WarmupRunState.Running &&
                !existingSession.Task.IsCompleted)
            {
                session = existingSession;
                session.PublishLog($"[modcache.fastindex.session.reuse] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion}");
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

    private WarmupTaskSession<ModPackageInventoryRefreshResult> CreateWarmupTaskSession(
        string normalizedRoot,
        ModPackageInventoryRefreshResult inventory)
    {
        var sessionKey = WarmupSessionKey.ForModsRoot(normalizedRoot, inventory.Snapshot.InventoryVersion);
        var session = new WarmupTaskSession<ModPackageInventoryRefreshResult>
        {
            WarmupKey = sessionKey.ToString(),
            ModsRoot = normalizedRoot,
            InventoryVersion = inventory.Snapshot.InventoryVersion,
            Domain = CacheWarmupDomain.ModsCatalog,
            WorkerCts = new CancellationTokenSource(),
            Task = Task.FromResult(inventory)
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

    private async Task<ModPackageInventoryRefreshResult> RunWarmupSessionAsync(
        string normalizedRoot,
        ModPackageInventoryRefreshResult inventory,
        WarmupTaskSession<ModPackageInventoryRefreshResult> session)
    {
        var changedPackages = inventory.AddedEntries
            .Concat(inventory.ChangedEntries)
            .Select(entry => entry.PackagePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        try
        {
            await _controller.IndexScheduler.QueueRefreshAsync(
                new ModIndexRefreshRequest
                {
                    ModsRootPath = normalizedRoot,
                    ChangedPackages = changedPackages,
                    RemovedPackages = inventory.RemovedPackagePaths,
                    AllowDeepEnrichment = false
                },
                new Progress<ModIndexRefreshProgress>(progress =>
                {
                    session.PublishProgress(new CacheWarmupProgress
                    {
                        Domain = CacheWarmupDomain.ModsCatalog,
                        Stage = progress.Stage,
                        Percent = progress.Percent,
                        Current = progress.Current,
                        Total = progress.Total,
                        Detail = string.IsNullOrWhiteSpace(progress.Detail)
                            ? "Preparing Mod catalog cache..."
                            : progress.Detail,
                        IsBlocking = true
                    });
                    _warmupStates[normalizedRoot] = session.ToStateSnapshot();
                }),
                session.WorkerCts.Token).ConfigureAwait(false);

            _readyInventoryVersions[normalizedRoot] = inventory.Snapshot.InventoryVersion;
            var readyProgress = BuildReadyProgress(inventory);
            session.PublishProgress(readyProgress);
            session.PublishLog(
                $"[modcache.ready] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={inventory.Snapshot.Entries.Count}");
            session.MarkCompleted(readyProgress, "Warmup completed.");
            _warmupStates[normalizedRoot] = session.ToStateSnapshot();
            return inventory;
        }
        catch (OperationCanceledException) when (session.WorkerCts.IsCancellationRequested)
        {
            session.MarkPaused("Warmup paused.");
            session.PublishProgress(MainWindowCacheWarmupController.BuildPausedProgress(
                CacheWarmupDomain.ModsCatalog,
                session.LastProgress,
                "Mods warmup paused. Switch back to resume."));
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

    private void CleanupStaleSessions(string normalizedRoot, long inventoryVersion)
    {
        foreach (var entry in _controller.SessionRegistry.FindByDomainAndSource<ModPackageInventoryRefreshResult>(
                     normalizedRoot,
                     CacheWarmupDomain.ModsCatalog).ToArray())
        {
            if (entry.Value.InventoryVersion == inventoryVersion)
            {
                continue;
            }

            MainWindowCacheWarmupController.SafeCancelToken(entry.Value.WorkerCts);
            _controller.SessionRegistry.TryRemove(entry.Key, out WarmupTaskSession<ModPackageInventoryRefreshResult>? _);
        }
    }

    private void InvalidateRoot(string normalizedRoot, string reason)
    {
        _controller.BackgroundCachePrewarmCoordinator?.CancelBySource(
            normalizedRoot,
            reason,
            ModCatalogQueryPrimeJobType);
        _controller.TryCancelInventoryRefresh(normalizedRoot);
        foreach (var entry in _controller.SessionRegistry.FindByDomainAndSource<ModPackageInventoryRefreshResult>(
                     normalizedRoot,
                     CacheWarmupDomain.ModsCatalog).ToArray())
        {
            MainWindowCacheWarmupController.SafeCancelToken(entry.Value.WorkerCts);
            _controller.SessionRegistry.TryRemove(entry.Key, out WarmupTaskSession<ModPackageInventoryRefreshResult>? _);
        }

        _readyInventoryVersions.TryRemove(normalizedRoot, out _);
        _warmupStates.TryRemove(normalizedRoot, out _);
        _queuedPrewarmRoots.TryRemove(normalizedRoot, out _);
    }

    private void OnModsRootInvalidated(string normalizedRoot, string reason, string changedPath)
    {
        InvalidateRoot(normalizedRoot, $"invalidate:{reason}");
    }

    private async Task PrimeModCatalogQueryAsync(
        ModItemCatalogQuery query,
        string trigger,
        CancellationToken cancellationToken)
    {
        await EnsureWorkspaceReadyCoreAsync(
            query.ModsRoot,
            _controller.CreateDetachedWarmupHost("modquery", trigger),
            cancellationToken).ConfigureAwait(false);
        _ = await _controller.ModItemCatalogService!
            .QueryPageAsync(query, cancellationToken)
            .ConfigureAwait(false);
    }

    private ModItemCatalogQuery NormalizeQuery(ModItemCatalogQuery query)
    {
        return new ModItemCatalogQuery
        {
            ModsRoot = _controller.ResolveDirectoryPath(query.ModsRoot),
            SearchQuery = query.SearchQuery?.Trim() ?? string.Empty,
            EntityKindFilter = NormalizeQueryValue(query.EntityKindFilter, "All"),
            SubTypeFilter = NormalizeQueryValue(query.SubTypeFilter, "All"),
            SortBy = NormalizeQueryValue(query.SortBy, "Last Indexed"),
            PageIndex = Math.Max(1, query.PageIndex),
            PageSize = Math.Max(1, query.PageSize)
        };
    }

    private BackgroundPrewarmJobKey BuildModCatalogPrewarmJobKey(ModItemCatalogQuery query)
    {
        var sourceVersion = new CacheSourceVersion
        {
            SourceKind = "mods-query",
            SourceKey = query.ModsRoot,
            VersionToken = BuildModQueryFingerprint(query)
        };

        return new BackgroundPrewarmJobKey
        {
            JobType = ModCatalogQueryPrimeJobType,
            SourceKey = string.Join(
                SourceKeyScopeSeparator,
                sourceVersion.SourceKey,
                sourceVersion.SourceKind,
                sourceVersion.VersionToken)
        };
    }

    private static string BuildModQueryFingerprint(ModItemCatalogQuery query)
    {
        return string.Join(
            SourceKeyScopeSeparator,
            query.EntityKindFilter,
            query.SubTypeFilter,
            query.SortBy,
            query.SearchQuery,
            query.PageIndex.ToString(),
            query.PageSize.ToString());
    }

    private static string NormalizeQueryValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static CacheWarmupProgress BuildReadyProgress(ModPackageInventoryRefreshResult inventory)
    {
        return new CacheWarmupProgress
        {
            Domain = CacheWarmupDomain.ModsCatalog,
            Stage = "ready",
            Percent = 100,
            Current = inventory.Snapshot.Entries.Count,
            Total = inventory.Snapshot.Entries.Count,
            Detail = "Mods catalog cache is ready.",
            IsBlocking = true
        };
    }

    private IEnumerable<string> EnumerateKnownRoots()
    {
        return _warmupStates.Keys
            .Concat(_readyInventoryVersions.Keys)
            .Concat(_queuedPrewarmRoots.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
