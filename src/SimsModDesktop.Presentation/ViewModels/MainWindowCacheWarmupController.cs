using Microsoft.Extensions.Logging;
using System.Diagnostics;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.PackageCore;
using SimsModDesktop.Presentation.Diagnostics;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowCacheWarmupController
{
    private readonly IModPackageInventoryService _inventoryService;
    private readonly IModItemIndexScheduler _indexScheduler;
    private readonly IPackageIndexCache _packageIndexCache;
    private readonly ILogger<MainWindowCacheWarmupController> _logger;
    private readonly IPathIdentityResolver _pathIdentityResolver;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _currentRoot;
    private ModPackageInventoryRefreshResult? _inventoryResult;
    private string? _modsReadyRoot;
    private long _modsReadyInventoryVersion;
    private PackageIndexSnapshot? _trayReadySnapshot;
    private readonly Dictionary<string, Task<PackageIndexSnapshot>> _trayWarmupTasks = new(StringComparer.OrdinalIgnoreCase);

    public MainWindowCacheWarmupController(
        IModPackageInventoryService inventoryService,
        IModItemIndexScheduler indexScheduler,
        IPackageIndexCache packageIndexCache,
        ILogger<MainWindowCacheWarmupController> logger,
        IPathIdentityResolver? pathIdentityResolver = null)
    {
        _inventoryService = inventoryService;
        _indexScheduler = indexScheduler;
        _packageIndexCache = packageIndexCache;
        _logger = logger;
        _pathIdentityResolver = pathIdentityResolver ?? new SystemPathIdentityResolver();
    }

    internal async Task<ModPackageInventoryRefreshResult> EnsureModsWorkspaceReadyAsync(
        string modsRootPath,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsRootPath);
        ArgumentNullException.ThrowIfNull(host);

        var resolvedRoot = ResolveDirectory(modsRootPath);
        var normalizedRoot = resolvedRoot.CanonicalPath;
        host.AppendLog(
            $"[path.resolve] component=modcache.warmup rawPath={resolvedRoot.FullPath} canonicalPath={resolvedRoot.CanonicalPath} exists={resolvedRoot.Exists} isReparse={resolvedRoot.IsReparsePoint} linkTarget={resolvedRoot.LinkTarget ?? string.Empty}");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var inventory = await EnsureInventoryCoreAsync(normalizedRoot, host, cancellationToken).ConfigureAwait(false);
            if (string.Equals(_modsReadyRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
                _modsReadyInventoryVersion == inventory.Snapshot.InventoryVersion)
            {
                host.ReportProgress(new CacheWarmupProgress
                {
                    Domain = CacheWarmupDomain.ModsCatalog,
                    Stage = "ready",
                    Percent = 100,
                    Current = inventory.Snapshot.Entries.Count,
                    Total = inventory.Snapshot.Entries.Count,
                    Detail = "Mods catalog cache is ready.",
                    IsBlocking = true
                });
                return inventory;
            }

            var changedPackages = inventory.AddedEntries
                .Concat(inventory.ChangedEntries)
                .Select(entry => entry.PackagePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            using var timing = PerformanceLogScope.Begin(
                _logger,
                "modcache.fastindex",
                host.AppendLog,
                ("modsRoot", normalizedRoot),
                ("inventoryVersion", inventory.Snapshot.InventoryVersion),
                ("changedCount", changedPackages.Length),
                ("removedCount", inventory.RemovedPackagePaths.Count));
            host.AppendLog(
                $"[modcache.fastindex.start] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} changed={changedPackages.Length} removed={inventory.RemovedPackagePaths.Count}");

            await _indexScheduler.QueueRefreshAsync(
                new ModIndexRefreshRequest
                {
                    ModsRootPath = normalizedRoot,
                    ChangedPackages = changedPackages,
                    RemovedPackages = inventory.RemovedPackagePaths,
                    AllowDeepEnrichment = false
                },
                new Progress<ModIndexRefreshProgress>(progress =>
                {
                    host.ReportProgress(new CacheWarmupProgress
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
                }),
                cancellationToken).ConfigureAwait(false);

            _modsReadyRoot = normalizedRoot;
            _modsReadyInventoryVersion = inventory.Snapshot.InventoryVersion;
            host.ReportProgress(new CacheWarmupProgress
            {
                Domain = CacheWarmupDomain.ModsCatalog,
                Stage = "ready",
                Percent = 100,
                Current = inventory.Snapshot.Entries.Count,
                Total = inventory.Snapshot.Entries.Count,
                Detail = "Mods catalog cache is ready.",
                IsBlocking = true
            });
            host.AppendLog(
                $"[modcache.ready] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={inventory.Snapshot.Entries.Count}");
            timing.Success(
                "ready",
                ("packageCount", inventory.Snapshot.Entries.Count),
                ("inventoryVersion", inventory.Snapshot.InventoryVersion));
            return inventory;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<PackageIndexSnapshot> EnsureTrayWorkspaceReadyAsync(
        string modsRootPath,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsRootPath);
        ArgumentNullException.ThrowIfNull(host);

        var resolvedRoot = ResolveDirectory(modsRootPath);
        var normalizedRoot = resolvedRoot.CanonicalPath;
        host.AppendLog(
            $"[path.resolve] component=traycache.warmup rawPath={resolvedRoot.FullPath} canonicalPath={resolvedRoot.CanonicalPath} exists={resolvedRoot.Exists} isReparse={resolvedRoot.IsReparsePoint} linkTarget={resolvedRoot.LinkTarget ?? string.Empty}");
        ModPackageInventoryRefreshResult inventory;
        Task<PackageIndexSnapshot> trayWarmupTask;
        var trayWarmupKey = string.Empty;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            inventory = await EnsureInventoryCoreAsync(normalizedRoot, host, cancellationToken).ConfigureAwait(false);
            if (_trayReadySnapshot is not null &&
                _pathIdentityResolver.EqualsDirectory(_trayReadySnapshot.ModsRootPath, normalizedRoot) &&
                _trayReadySnapshot.InventoryVersion == inventory.Snapshot.InventoryVersion)
            {
                host.ReportProgress(new CacheWarmupProgress
                {
                    Domain = CacheWarmupDomain.TrayDependency,
                    Stage = "ready",
                    Percent = 100,
                    Current = inventory.Snapshot.Entries.Count,
                    Total = inventory.Snapshot.Entries.Count,
                    Detail = "Tray dependency cache is ready.",
                    IsBlocking = true
                });
                return _trayReadySnapshot;
            }

            trayWarmupKey = BuildTrayWarmupKey(normalizedRoot, inventory.Snapshot.InventoryVersion);
            if (!_trayWarmupTasks.TryGetValue(trayWarmupKey, out trayWarmupTask!))
            {
                // Run warmup construction on a worker thread so tray activation never blocks the UI thread.
                trayWarmupTask = Task.Run(
                    () => LoadOrBuildTraySnapshotAsync(normalizedRoot, inventory, host, CancellationToken.None),
                    CancellationToken.None);
                _trayWarmupTasks[trayWarmupKey] = trayWarmupTask;
            }
            else
            {
                host.AppendLog(
                    $"[traycache.snapshot.inflight.wait] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion}");
            }
        }
        finally
        {
            _gate.Release();
        }

        PackageIndexSnapshot snapshot;
        try
        {
            snapshot = await trayWarmupTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_trayWarmupTasks.TryGetValue(trayWarmupKey, out var existingTask) &&
                    ReferenceEquals(existingTask, trayWarmupTask))
                {
                    _trayWarmupTasks.Remove(trayWarmupKey);
                }
            }
            finally
            {
                _gate.Release();
            }

            throw;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_trayWarmupTasks.TryGetValue(trayWarmupKey, out var existingTask) &&
                ReferenceEquals(existingTask, trayWarmupTask))
            {
                _trayWarmupTasks.Remove(trayWarmupKey);
            }

            _trayReadySnapshot = snapshot;
        }
        finally
        {
            _gate.Release();
        }

        host.ReportProgress(new CacheWarmupProgress
        {
            Domain = CacheWarmupDomain.TrayDependency,
            Stage = "ready",
            Percent = 100,
            Current = snapshot.Packages.Count,
            Total = snapshot.Packages.Count,
            Detail = "Tray dependency cache is ready.",
            IsBlocking = true
        });
        host.AppendLog(
            $"[traycache.ready] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={snapshot.Packages.Count}");
        return snapshot;
    }

    private async Task<PackageIndexSnapshot> LoadOrBuildTraySnapshotAsync(
        string normalizedRoot,
        ModPackageInventoryRefreshResult inventory,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken)
    {
        PackageIndexSnapshot? snapshot;
        using var timing = PerformanceLogScope.Begin(
            _logger,
            "traycache.snapshot",
            host.AppendLog,
            ("modsRoot", normalizedRoot),
            ("inventoryVersion", inventory.Snapshot.InventoryVersion),
            ("packageCount", inventory.Snapshot.Entries.Count));

        using var loadTiming = PerformanceLogScope.Begin(
            _logger,
            "traycache.snapshot.load",
            host.AppendLog,
            ("modsRoot", normalizedRoot),
            ("inventoryVersion", inventory.Snapshot.InventoryVersion));
        try
        {
            snapshot = await _packageIndexCache.TryLoadSnapshotAsync(
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
            host.AppendLog(
                $"[traycache.snapshot.load-hit] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={snapshot.Packages.Count}");
            loadTiming.Success(
                "load-hit",
                ("packageCount", snapshot.Packages.Count));
            timing.Mark(
                "snapshot.load-hit",
                ("packageCount", snapshot.Packages.Count));
            timing.Success(
                "ready",
                ("inventoryVersion", inventory.Snapshot.InventoryVersion),
                ("packageCount", snapshot.Packages.Count));
            return snapshot;
        }

        loadTiming.Skip("load-miss");
        host.AppendLog(
            $"[traycache.snapshot.build.start] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={inventory.Snapshot.Entries.Count}");
        using var buildTiming = PerformanceLogScope.Begin(
            _logger,
            "traycache.snapshot.build",
            host.AppendLog,
            ("modsRoot", normalizedRoot),
            ("inventoryVersion", inventory.Snapshot.InventoryVersion),
            ("packageCount", inventory.Snapshot.Entries.Count));
        var stageStopwatch = new Stopwatch();
        var currentStageLabel = string.Empty;
        var currentStagePercent = 0;
        try
        {
            snapshot = await _packageIndexCache.BuildSnapshotAsync(
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
                    RemovedPackagePaths = inventory.RemovedPackagePaths
                },
                new Progress<TrayDependencyExportProgress>(progress =>
                {
                    var stageLabel = BuildTrayCacheStageLabel(progress);
                    if (!string.Equals(currentStageLabel, stageLabel, StringComparison.Ordinal))
                    {
                        if (stageStopwatch.IsRunning && !string.IsNullOrWhiteSpace(currentStageLabel))
                        {
                            host.AppendLog(
                                $"[traycache.stage.done] stage={currentStageLabel} percent={currentStagePercent} elapsedMs={stageStopwatch.ElapsedMilliseconds}");
                        }

                        currentStageLabel = stageLabel;
                        stageStopwatch.Restart();
                        host.AppendLog(
                            $"[traycache.stage.start] stage={currentStageLabel} percent={progress.Percent}");
                    }

                    currentStagePercent = progress.Percent;
                    host.ReportProgress(new CacheWarmupProgress
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
                }),
                cancellationToken).ConfigureAwait(false);

            if (stageStopwatch.IsRunning && !string.IsNullOrWhiteSpace(currentStageLabel))
            {
                host.AppendLog(
                    $"[traycache.stage.done] stage={currentStageLabel} percent={currentStagePercent} elapsedMs={stageStopwatch.ElapsedMilliseconds}");
            }

            buildTiming.Success(
                "build-done",
                ("packageCount", snapshot.Packages.Count));
        }
        catch (OperationCanceledException)
        {
            if (stageStopwatch.IsRunning && !string.IsNullOrWhiteSpace(currentStageLabel))
            {
                host.AppendLog(
                    $"[traycache.stage.cancel] stage={currentStageLabel} percent={currentStagePercent} elapsedMs={stageStopwatch.ElapsedMilliseconds}");
            }

            buildTiming.Cancel("build-cancelled");
            throw;
        }
        catch (Exception ex)
        {
            if (stageStopwatch.IsRunning && !string.IsNullOrWhiteSpace(currentStageLabel))
            {
                host.AppendLog(
                    $"[traycache.stage.fail] stage={currentStageLabel} percent={currentStagePercent} elapsedMs={stageStopwatch.ElapsedMilliseconds}");
            }

            buildTiming.Fail(ex, "build-failed");
            throw;
        }

        host.AppendLog(
            $"[traycache.snapshot.build.done] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={snapshot.Packages.Count}");
        timing.Mark(
            "snapshot.build-done",
            ("packageCount", snapshot.Packages.Count));
        timing.Success(
            "ready",
            ("inventoryVersion", inventory.Snapshot.InventoryVersion),
            ("packageCount", snapshot.Packages.Count));
        return snapshot;
    }

    internal void QueueModsPriorityDeepEnrichment(
        string modsRootPath,
        IReadOnlyList<string> priorityPackages,
        Action<string>? appendLog = null)
    {
        if (string.IsNullOrWhiteSpace(modsRootPath) || priorityPackages.Count == 0)
        {
            return;
        }

        var normalizedRoot = ResolveDirectoryPath(modsRootPath);
        if (!string.Equals(_modsReadyRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var priorities = priorityPackages
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(ResolveFilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (priorities.Length == 0)
        {
            return;
        }

        appendLog?.Invoke($"[modcache.deepindex.priority.start] modsRoot={normalizedRoot} priorityCount={priorities.Length}");
        _ = Task.Run(async () =>
        {
            try
            {
                await _indexScheduler.QueueRefreshAsync(
                    new ModIndexRefreshRequest
                    {
                        ModsRootPath = normalizedRoot,
                        PriorityPackages = priorities,
                        AllowDeepEnrichment = true
                    }).ConfigureAwait(false);
                appendLog?.Invoke($"[modcache.deepindex.priority.done] modsRoot={normalizedRoot} priorityCount={priorities.Length}");
            }
            catch (Exception ex)
            {
                appendLog?.Invoke($"[modcache.deepindex.priority.fail] {ex.Message}");
            }
        });
    }

    internal bool TryGetReadyTraySnapshot(string modsRootPath, out PackageIndexSnapshot snapshot)
    {
        snapshot = null!;
        if (_trayReadySnapshot is null || string.IsNullOrWhiteSpace(modsRootPath))
        {
            return false;
        }

        var normalizedRoot = ResolveDirectoryPath(modsRootPath);
        if (!_pathIdentityResolver.EqualsDirectory(_trayReadySnapshot.ModsRootPath, normalizedRoot))
        {
            return false;
        }

        snapshot = _trayReadySnapshot;
        return true;
    }

    internal void Reset()
    {
        _currentRoot = null;
        _inventoryResult = null;
        _modsReadyRoot = null;
        _modsReadyInventoryVersion = 0;
        _trayReadySnapshot = null;
        _trayWarmupTasks.Clear();
    }

    private async Task<ModPackageInventoryRefreshResult> EnsureInventoryCoreAsync(
        string normalizedRoot,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken)
    {
        if (string.Equals(_currentRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
            _inventoryResult is not null)
        {
            return _inventoryResult;
        }

        if (!string.Equals(_currentRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            _modsReadyRoot = null;
            _modsReadyInventoryVersion = 0;
            _trayReadySnapshot = null;
            _trayWarmupTasks.Clear();
        }

        using var timing = PerformanceLogScope.Begin(
            _logger,
            "modcache.inventory",
            host.AppendLog,
            ("modsRoot", normalizedRoot));
        host.AppendLog($"[modcache.inventory.start] modsRoot={normalizedRoot}");

        try
        {
            // SqliteModPackageInventoryService.RefreshAsync is mostly synchronous work wrapped in Task.
            // Offload it explicitly to keep UI thread responsive during tray/mod warmup.
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
                    CancellationToken.None),
                CancellationToken.None);
            var result = await refreshTask.WaitAsync(cancellationToken).ConfigureAwait(false);

            _currentRoot = normalizedRoot;
            _inventoryResult = result;
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

    private static string BuildTrayWarmupKey(string normalizedRoot, long inventoryVersion)
    {
        return normalizedRoot + "|" + inventoryVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private ResolvedPathInfo ResolveDirectory(string path)
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

    private string ResolveDirectoryPath(string path)
    {
        return ResolveDirectory(path).CanonicalPath;
    }

    private string ResolveFilePath(string path)
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
}
