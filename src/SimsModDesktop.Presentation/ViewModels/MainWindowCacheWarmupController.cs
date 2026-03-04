using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Presentation.Diagnostics;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowCacheWarmupController
{
    private readonly IModPackageInventoryService _inventoryService;
    private readonly IModItemIndexScheduler _indexScheduler;
    private readonly IPackageIndexCache _packageIndexCache;
    private readonly ILogger<MainWindowCacheWarmupController> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _currentRoot;
    private ModPackageInventoryRefreshResult? _inventoryResult;
    private string? _modsReadyRoot;
    private long _modsReadyInventoryVersion;
    private PackageIndexSnapshot? _trayReadySnapshot;

    public MainWindowCacheWarmupController(
        IModPackageInventoryService inventoryService,
        IModItemIndexScheduler indexScheduler,
        IPackageIndexCache packageIndexCache,
        ILogger<MainWindowCacheWarmupController> logger)
    {
        _inventoryService = inventoryService;
        _indexScheduler = indexScheduler;
        _packageIndexCache = packageIndexCache;
        _logger = logger;
    }

    internal async Task<ModPackageInventoryRefreshResult> EnsureModsWorkspaceReadyAsync(
        string modsRootPath,
        MainWindowCacheWarmupHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modsRootPath);
        ArgumentNullException.ThrowIfNull(host);

        var normalizedRoot = Path.GetFullPath(modsRootPath.Trim());
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

        var normalizedRoot = Path.GetFullPath(modsRootPath.Trim());
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var inventory = await EnsureInventoryCoreAsync(normalizedRoot, host, cancellationToken).ConfigureAwait(false);
            if (_trayReadySnapshot is not null &&
                string.Equals(_trayReadySnapshot.ModsRootPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) &&
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

            PackageIndexSnapshot? snapshot;
            using var timing = PerformanceLogScope.Begin(
                _logger,
                "traycache.snapshot",
                host.AppendLog,
                ("modsRoot", normalizedRoot),
                ("inventoryVersion", inventory.Snapshot.InventoryVersion),
                ("packageCount", inventory.Snapshot.Entries.Count));

            snapshot = await _packageIndexCache.TryLoadSnapshotAsync(
                normalizedRoot,
                inventory.Snapshot.InventoryVersion,
                cancellationToken).ConfigureAwait(false);
            if (snapshot is not null)
            {
                host.AppendLog(
                    $"[traycache.snapshot.load-hit] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={snapshot.Packages.Count}");
            }
            else
            {
                host.AppendLog(
                    $"[traycache.snapshot.build.start] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={inventory.Snapshot.Entries.Count}");
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
                host.AppendLog(
                    $"[traycache.snapshot.build.done] modsRoot={normalizedRoot} inventoryVersion={inventory.Snapshot.InventoryVersion} packages={snapshot.Packages.Count}");
            }

            _trayReadySnapshot = snapshot;
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
            timing.Success(
                "ready",
                ("inventoryVersion", inventory.Snapshot.InventoryVersion),
                ("packageCount", snapshot.Packages.Count));
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
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

        var normalizedRoot = Path.GetFullPath(modsRootPath.Trim());
        if (!string.Equals(_modsReadyRoot, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var priorities = priorityPackages
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim()))
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

        var normalizedRoot = Path.GetFullPath(modsRootPath.Trim());
        if (!string.Equals(_trayReadySnapshot.ModsRootPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
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
        }

        using var timing = PerformanceLogScope.Begin(
            _logger,
            "modcache.inventory",
            host.AppendLog,
            ("modsRoot", normalizedRoot));
        host.AppendLog($"[modcache.inventory.start] modsRoot={normalizedRoot}");

        try
        {
            var result = await _inventoryService.RefreshAsync(
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
                cancellationToken).ConfigureAwait(false);

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
        catch (Exception ex)
        {
            host.AppendLog($"[modcache.inventory.fail] modsRoot={normalizedRoot} error={ex.Message}");
            timing.Fail(ex, "inventory failed", ("modsRoot", normalizedRoot));
            throw;
        }
    }
}
