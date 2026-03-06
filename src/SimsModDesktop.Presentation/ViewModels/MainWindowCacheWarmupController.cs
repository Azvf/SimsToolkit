using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.Warmup;
using SimsModDesktop.PackageCore;
using SimsModDesktop.Presentation.Diagnostics;
using SimsModDesktop.Presentation.Warmup;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowCacheWarmupController
{
    private readonly IModPackageInventoryService _inventoryService;
    private readonly IModItemIndexScheduler _indexScheduler;
    private readonly IModItemCatalogService? _modItemCatalogService;
    private readonly ISaveHouseholdCoordinator? _saveHouseholdCoordinator;
    private readonly IPackageIndexCache _packageIndexCache;
    private readonly ILogger<MainWindowCacheWarmupController> _logger;
    private readonly IPathIdentityResolver _pathIdentityResolver;
    private readonly IConfigurationProvider? _configurationProvider;
    private readonly IBackgroundCachePrewarmCoordinator? _backgroundCachePrewarmCoordinator;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _rootGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ModPackageInventoryRefreshResult> _inventoryResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, InventoryRefreshTaskSession> _inventoryRefreshTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _rootWatchers = new(StringComparer.OrdinalIgnoreCase);

    internal event Action<string, string, string>? ModsRootInvalidated;

    public MainWindowCacheWarmupController(
        IModPackageInventoryService inventoryService,
        IModItemIndexScheduler indexScheduler,
        IPackageIndexCache packageIndexCache,
        ILogger<MainWindowCacheWarmupController> logger,
        IModItemCatalogService? modItemCatalogService = null,
        ISaveHouseholdCoordinator? saveHouseholdCoordinator = null,
        IPathIdentityResolver? pathIdentityResolver = null,
        IConfigurationProvider? configurationProvider = null,
        IBackgroundCachePrewarmCoordinator? backgroundCachePrewarmCoordinator = null)
    {
        _inventoryService = inventoryService;
        _indexScheduler = indexScheduler;
        _modItemCatalogService = modItemCatalogService;
        _saveHouseholdCoordinator = saveHouseholdCoordinator;
        _packageIndexCache = packageIndexCache;
        _logger = logger;
        _pathIdentityResolver = pathIdentityResolver ?? new SystemPathIdentityResolver();
        _configurationProvider = configurationProvider;
        _backgroundCachePrewarmCoordinator = backgroundCachePrewarmCoordinator;
        SessionRegistry = new WarmupSessionRegistry();
    }

    internal WarmupSessionRegistry SessionRegistry { get; }
    internal IModItemIndexScheduler IndexScheduler => _indexScheduler;
    internal IModItemCatalogService? ModItemCatalogService => _modItemCatalogService;
    internal ISaveHouseholdCoordinator? SaveHouseholdCoordinator => _saveHouseholdCoordinator;
    internal IPackageIndexCache PackageIndexCache => _packageIndexCache;
    internal IConfigurationProvider? ConfigurationProvider => _configurationProvider;
    internal IBackgroundCachePrewarmCoordinator? BackgroundCachePrewarmCoordinator => _backgroundCachePrewarmCoordinator;
    internal ILogger<MainWindowCacheWarmupController> Logger => _logger;

    internal async Task<ModPackageInventoryRefreshResult> EnsureInventoryAsync(
        string normalizedRoot,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRoot);
        ArgumentNullException.ThrowIfNull(host);
        return await EnsureInventoryCoreAsync(normalizedRoot, host, cancellationToken).ConfigureAwait(false);
    }

    internal SemaphoreSlim GetRootGate(string normalizedRoot)
    {
        return _rootGates.GetOrAdd(normalizedRoot, _ => new SemaphoreSlim(1, 1));
    }

    internal bool TryCancelInventoryRefresh(string normalizedRoot)
    {
        if (_inventoryRefreshTasks.TryRemove(normalizedRoot, out var session))
        {
            SafeCancelToken(session.WorkerCts);
            return true;
        }

        return false;
    }

    internal MainWindowCacheWarmupHost CreateDetachedWarmupHost(string category, string trigger)
    {
        return new MainWindowCacheWarmupHost
        {
            ReportProgress = _ => { },
            AppendLog = message =>
            {
                if (!string.IsNullOrWhiteSpace(message))
                {
                    _logger.LogInformation(
                        "{Category}.prewarm.log trigger={Trigger} message={Message}",
                        category,
                        trigger,
                        message);
                }
            }
        };
    }

    internal void ResetRuntime()
    {
        foreach (var session in _inventoryRefreshTasks.Values)
        {
            SafeCancelToken(session.WorkerCts);
        }

        _inventoryResults.Clear();
        _inventoryRefreshTasks.Clear();
        _rootGates.Clear();
        foreach (var watcher in _rootWatchers.Values)
        {
            try
            {
                watcher.Dispose();
            }
            catch
            {
            }
        }

        _rootWatchers.Clear();
    }

    public void Reset()
    {
        ResetRuntime();
    }

    internal async Task<bool> GetRound2ConfigBoolAsync(
        string key,
        bool defaultValue,
        CancellationToken cancellationToken)
    {
        if (_configurationProvider is null)
        {
            return defaultValue;
        }

        var configured = await _configurationProvider.GetConfigurationAsync<bool?>(key, cancellationToken).ConfigureAwait(false);
        return configured ?? defaultValue;
    }

    internal async Task<int?> GetConfigIntAsync(
        string key,
        int? defaultValue,
        CancellationToken cancellationToken)
    {
        if (_configurationProvider is null)
        {
            return defaultValue;
        }

        var configured = await _configurationProvider.GetConfigurationAsync<int?>(key, cancellationToken).ConfigureAwait(false);
        return configured ?? defaultValue;
    }

    internal ResolvedPathInfo ResolveDirectory(string path)
    {
        var resolved = _pathIdentityResolver.ResolveDirectory(path);
        var fullPath = !string.IsNullOrWhiteSpace(resolved.FullPath)
            ? resolved.FullPath
            : path.Trim().Trim('"');
        var canonicalPath = !string.IsNullOrWhiteSpace(resolved.CanonicalPath)
            ? resolved.CanonicalPath
            : fullPath;
        return resolved with
        {
            FullPath = fullPath,
            CanonicalPath = canonicalPath
        };
    }

    internal string ResolveDirectoryPath(string path)
    {
        return ResolveDirectory(path).CanonicalPath;
    }

    internal string ResolveFilePath(string path)
    {
        var resolved = _pathIdentityResolver.ResolveFile(path);
        if (!string.IsNullOrWhiteSpace(resolved.CanonicalPath))
        {
            return resolved.CanonicalPath;
        }

        if (!string.IsNullOrWhiteSpace(resolved.FullPath))
        {
            return resolved.FullPath;
        }

        return path.Trim().Trim('"');
    }

    private async Task<ModPackageInventoryRefreshResult> EnsureInventoryCoreAsync(
        string normalizedRoot,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken)
    {
        if (_inventoryResults.TryGetValue(normalizedRoot, out var cachedInventory))
        {
            EnsureRootWatcher(normalizedRoot);
            return cachedInventory;
        }

        InventoryRefreshTaskSession? session;
        if (_inventoryRefreshTasks.TryGetValue(normalizedRoot, out var inflight))
        {
            session = inflight;
            _logger.LogInformation(
                "modcache.inventory.inflight.reuse modsRoot={ModsRoot}",
                normalizedRoot);
            host.AppendLog($"[modcache.inventory.inflight.reuse] modsRoot={normalizedRoot}");
        }
        else
        {
            var workerCts = new CancellationTokenSource();
            var workerTask = RunInventoryRefreshTaskAsync(normalizedRoot, host, workerCts.Token);
            var candidate = new InventoryRefreshTaskSession
            {
                ModsRoot = normalizedRoot,
                WorkerCts = workerCts,
                Task = workerTask
            };
            if (_inventoryRefreshTasks.TryAdd(normalizedRoot, candidate))
            {
                session = candidate;
                _ = workerTask.ContinueWith(
                    _ =>
                    {
                        _inventoryRefreshTasks.TryRemove(normalizedRoot, out InventoryRefreshTaskSession? _);
                        workerCts.Dispose();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            else
            {
                workerCts.Cancel();
                workerCts.Dispose();
                if (!_inventoryRefreshTasks.TryGetValue(normalizedRoot, out session))
                {
                    return await EnsureInventoryCoreAsync(normalizedRoot, host, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation(
                    "modcache.inventory.inflight.reuse modsRoot={ModsRoot}",
                    normalizedRoot);
                host.AppendLog($"[modcache.inventory.inflight.reuse] modsRoot={normalizedRoot}");
            }
        }

        return await session!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ModPackageInventoryRefreshResult> RunInventoryRefreshTaskAsync(
        string normalizedRoot,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken)
    {
        using var timing = PerformanceLogScope.Begin(
            _logger,
            "modcache.inventory",
            ("modsRoot", normalizedRoot));
        host.AppendLog($"[modcache.inventory.start] modsRoot={normalizedRoot}");
        try
        {
            var refreshTask = Task.Run(
                () => _inventoryService.RefreshAsync(
                    normalizedRoot,
                    new Progress<ModPackageInventoryRefreshProgress>(progress =>
                    {
                        host.ReportProgress(new CacheWarmupProgress
                        {
                            Domain = CacheWarmupDomain.ModsCatalog,
                            Stage = progress.Stage,
                            Percent = progress.Percent,
                            Current = progress.Current,
                            Total = progress.Total,
                            Detail = string.IsNullOrWhiteSpace(progress.Detail)
                                ? "Validating package inventory..."
                                : progress.Detail,
                            IsBlocking = true
                        });
                    }),
                    cancellationToken),
                cancellationToken);
            var result = await refreshTask.ConfigureAwait(false);

            _inventoryResults[normalizedRoot] = result;
            EnsureRootWatcher(normalizedRoot);
            host.AppendLog(
                $"[modcache.inventory.done] modsRoot={normalizedRoot} inventoryVersion={result.Snapshot.InventoryVersion} packages={result.Snapshot.Entries.Count} changed={result.AddedEntries.Count + result.ChangedEntries.Count} removed={result.RemovedPackagePaths.Count}");
            timing.Success(
                "validated",
                ("inventoryVersion", result.Snapshot.InventoryVersion),
                ("packageCount", result.Snapshot.Entries.Count),
                ("changedCount", result.AddedEntries.Count + result.ChangedEntries.Count),
                ("removedCount", result.RemovedPackagePaths.Count));
            return result;
        }
        catch (OperationCanceledException)
        {
            host.AppendLog($"[modcache.inventory.cancel] modsRoot={normalizedRoot}");
            timing.Cancel("inventory cancelled");
            throw;
        }
        catch (Exception ex)
        {
            host.AppendLog($"[modcache.inventory.fail] modsRoot={normalizedRoot} error={ex.Message}");
            timing.Fail(ex, "inventory failed", ("modsRoot", normalizedRoot));
            throw;
        }
    }

    private void EnsureRootWatcher(string normalizedRoot)
    {
        if (string.IsNullOrWhiteSpace(normalizedRoot) || !Directory.Exists(normalizedRoot))
        {
            return;
        }

        _rootWatchers.GetOrAdd(normalizedRoot, root =>
        {
            var watcher = new FileSystemWatcher(root, "*.package")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };

            watcher.Changed += (_, args) => InvalidateRootCaches(root, "changed", args.FullPath);
            watcher.Created += (_, args) => InvalidateRootCaches(root, "created", args.FullPath);
            watcher.Deleted += (_, args) => InvalidateRootCaches(root, "deleted", args.FullPath);
            watcher.Renamed += (_, args) => InvalidateRootCaches(root, "renamed", args.FullPath);
            watcher.Error += (_, args) =>
            {
                _logger.LogWarning(
                    args.GetException(),
                    "modcache.inventory.watch.error modsRoot={ModsRoot}",
                    root);
                InvalidateRootCaches(root, "watcher-error", string.Empty);
            };
            watcher.EnableRaisingEvents = true;
            _logger.LogInformation("modcache.inventory.watch.start modsRoot={ModsRoot}", root);
            return watcher;
        });
    }

    private void InvalidateRootCaches(string normalizedRoot, string reason, string changedPath)
    {
        var invalidated = false;
        invalidated |= _inventoryResults.TryRemove(normalizedRoot, out _);

        if (_inventoryRefreshTasks.TryRemove(normalizedRoot, out var inventorySession))
        {
            SafeCancelToken(inventorySession.WorkerCts);
            invalidated = true;
        }

        ModsRootInvalidated?.Invoke(normalizedRoot, reason, changedPath);

        if (!invalidated)
        {
            return;
        }

        _logger.LogInformation(
            "modcache.inventory.invalidate modsRoot={ModsRoot} reason={Reason} changedPath={ChangedPath}",
            normalizedRoot,
            reason,
            changedPath);
    }

    internal static CacheWarmupProgress BuildPausedProgress(
        CacheWarmupDomain domain,
        CacheWarmupProgress lastProgress,
        string detail)
    {
        var percent = Math.Clamp(lastProgress.Percent, 0, 99);
        return new CacheWarmupProgress
        {
            Domain = domain,
            Stage = "Paused",
            Percent = percent,
            Current = lastProgress.Current,
            Total = lastProgress.Total,
            Detail = detail,
            IsBlocking = false
        };
    }

    internal static MainWindowCacheWarmupHost CreateHost(CacheWarmupObserver? observer)
    {
        return new MainWindowCacheWarmupHost
        {
            ReportProgress = observer?.ReportProgress ?? (_ => { }),
            AppendLog = observer?.AppendLog ?? (_ => { })
        };
    }

    internal static void SafeCancelToken(CancellationTokenSource? cancellationTokenSource)
    {
        if (cancellationTokenSource is null)
        {
            return;
        }

        try
        {
            cancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
