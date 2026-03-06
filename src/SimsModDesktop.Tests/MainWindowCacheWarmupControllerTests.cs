using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Presentation.Services;
using SimsModDesktop.Presentation.ViewModels;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Tests;

public sealed class MainWindowCacheWarmupControllerTests
{
    [Fact]
    public async Task EnsureModsWorkspaceReadyAsync_PauseThenResume_Completes()
    {
        using var modsRoot = new TempDirectory("warmup-mods");
        var packagePath = Path.Combine(modsRoot.Path, "sample.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var scheduler = new PauseAwareModScheduler();
        var controller = new MainWindowCacheWarmupController(
            new StaticInventoryService(packagePath),
            scheduler,
            new FastPackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance);

        var firstTask = controller.EnsureModsWorkspaceReadyAsync(modsRoot.Path, CreateNoOpHost());
        await scheduler.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        controller.PauseModsWarmup(modsRoot.Path, "test-pause");

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstTask);
        Assert.True(controller.TryGetWarmupState(modsRoot.Path, CacheWarmupDomain.ModsCatalog, out var pausedState));
        Assert.NotNull(pausedState);
        Assert.Equal(MainWindowCacheWarmupController.WarmupRunState.Paused, pausedState!.State);

        _ = await controller.EnsureModsWorkspaceReadyAsync(modsRoot.Path, CreateNoOpHost());

        Assert.True(controller.TryGetWarmupState(modsRoot.Path, CacheWarmupDomain.ModsCatalog, out var completedState));
        Assert.NotNull(completedState);
        Assert.Equal(MainWindowCacheWarmupController.WarmupRunState.Completed, completedState!.State);
        Assert.True(scheduler.CallCount >= 2);
    }

    [Fact]
    public async Task EnsureTrayWorkspaceReadyAsync_PauseThenResume_Completes()
    {
        using var modsRoot = new TempDirectory("warmup-tray");
        var packagePath = Path.Combine(modsRoot.Path, "sample.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var packageCache = new PauseAwarePackageIndexCache();
        var controller = new MainWindowCacheWarmupController(
            new StaticInventoryService(packagePath),
            new NoOpModScheduler(),
            packageCache,
            NullLogger<MainWindowCacheWarmupController>.Instance);

        var firstTask = controller.EnsureTrayWorkspaceReadyAsync(modsRoot.Path, CreateNoOpHost());
        await packageCache.FirstBuildStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        controller.PauseTrayWarmup(modsRoot.Path, "test-pause");

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstTask);
        Assert.True(controller.TryGetWarmupState(modsRoot.Path, CacheWarmupDomain.TrayDependency, out var pausedState));
        Assert.NotNull(pausedState);
        Assert.Equal(MainWindowCacheWarmupController.WarmupRunState.Paused, pausedState!.State);

        _ = await controller.EnsureTrayWorkspaceReadyAsync(modsRoot.Path, CreateNoOpHost());
        Assert.True(controller.TryGetReadyTraySnapshot(modsRoot.Path, out var readySnapshot));
        Assert.NotNull(readySnapshot);
        Assert.True(controller.TryGetWarmupState(modsRoot.Path, CacheWarmupDomain.TrayDependency, out var completedState));
        Assert.NotNull(completedState);
        Assert.Equal(MainWindowCacheWarmupController.WarmupRunState.Completed, completedState!.State);
        Assert.True(packageCache.BuildCallCount >= 2);
    }

    [Fact]
    public async Task EnsureTrayWorkspaceReadyAsync_PassesTrayCachePerformanceConfigurations()
    {
        using var modsRoot = new TempDirectory("warmup-tray-config");
        var packagePath = Path.Combine(modsRoot.Path, "sample.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var packageCache = new CapturingPackageIndexCache();
        var configurationProvider = new StaticConfigurationProvider(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Performance.Round2.TrayCachePipelineEnabled"] = true,
            ["Performance.TrayCache.ParseWorkers"] = 20,
            ["Performance.TrayCache.WriteBatchSize"] = 1024,
            ["Performance.TrayCache.ParseResultChannelCapacity"] = 8192,
            ["Performance.TrayCache.CommitBatchSize"] = 4096,
            ["Performance.TrayCache.CommitIntervalMs"] = 900,
            ["Performance.Round2.TrayCacheThrottleV2Enabled"] = true,
            ["Performance.TrayCache.IncrementalOrphanCleanup"] = false
        });
        var controller = new MainWindowCacheWarmupController(
            new StaticInventoryService(packagePath),
            new NoOpModScheduler(),
            packageCache,
            NullLogger<MainWindowCacheWarmupController>.Instance,
            pathIdentityResolver: null,
            configurationProvider: configurationProvider);

        _ = await controller.EnsureTrayWorkspaceReadyAsync(modsRoot.Path, CreateNoOpHost());

        var request = Assert.Single(packageCache.BuildRequests);
        Assert.Equal(20, request.ParseWorkerCount);
        Assert.Equal(1024, request.WriteBatchSize);
        Assert.Equal(8192, request.ParseResultChannelCapacity);
        Assert.Equal(4096, request.CommitBatchSize);
        Assert.Equal(900, request.CommitIntervalMs);
        Assert.True(request.UseAdaptiveThrottleV2);
        Assert.False(request.UseIncrementalOrphanCleanup);
        Assert.True(request.UseParallelPipeline);
    }

    [Fact]
    public async Task EnsureInventoryCore_ReusesInflightRefresh()
    {
        using var modsRoot = new TempDirectory("warmup-inflight");
        var packagePath = Path.Combine(modsRoot.Path, "sample.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var inventory = new BlockingInventoryService(packagePath);
        var controller = new MainWindowCacheWarmupController(
            inventory,
            new NoOpModScheduler(),
            new FastPackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance);

        var first = controller.EnsureModsWorkspaceReadyAsync(modsRoot.Path, CreateNoOpHost());
        await inventory.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = controller.EnsureModsWorkspaceReadyAsync(modsRoot.Path, CreateNoOpHost());

        inventory.ReleaseFirstCall();
        _ = await first;
        _ = await second;

        Assert.Equal(1, inventory.CallCount);
    }

    [Fact]
    public async Task AppIdlePrewarmBootstrapper_QueuesTrayDependencyWarmupAfterIdleDelay()
    {
        using var modsRoot = new TempDirectory("warmup-idle");
        var packagePath = Path.Combine(modsRoot.Path, "sample.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var configurationProvider = new StaticConfigurationProvider(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Performance.IdlePrewarm.Enabled"] = true,
            ["Performance.IdlePrewarm.DelayMs"] = 50
        });
        var backgroundCoordinator = new BackgroundCachePrewarmCoordinator();
        var controller = new MainWindowCacheWarmupController(
            new StaticInventoryService(packagePath),
            new NoOpModScheduler(),
            new FastPackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance,
            pathIdentityResolver: null,
            configurationProvider: configurationProvider,
            backgroundCachePrewarmCoordinator: backgroundCoordinator);
        var bootstrapper = new AppIdlePrewarmBootstrapper(
            controller,
            new UiActivityMonitor(),
            NullLogger<AppIdlePrewarmBootstrapper>.Instance,
            configurationProvider);

        bootstrapper.QueueTrayDependencyStartupPrewarm(modsRoot.Path);

        await WaitForAsync(
            () => controller.TryGetReadyTraySnapshot(modsRoot.Path, out _),
            timeoutMs: 3000);

        Assert.True(controller.TryGetReadyTraySnapshot(modsRoot.Path, out var snapshot));
        Assert.NotNull(snapshot);
        Assert.True(controller.TryGetTrayPrewarmState(modsRoot.Path, out var state));
        Assert.NotNull(state);
        Assert.Equal(BackgroundPrewarmJobRunState.Completed, state!.RunState);
    }

    [Fact]
    public async Task QueueModsQueryIdlePrewarm_PrimesCatalogQueryCache()
    {
        using var modsRoot = new TempDirectory("warmup-modquery");
        var packagePath = Path.Combine(modsRoot.Path, "sample.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var catalogService = new RecordingModItemCatalogService();
        var backgroundCoordinator = new BackgroundCachePrewarmCoordinator();
        var controller = new MainWindowCacheWarmupController(
            new StaticInventoryService(packagePath),
            new NoOpModScheduler(),
            new FastPackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance,
            catalogService,
            pathIdentityResolver: null,
            configurationProvider: null,
            backgroundCachePrewarmCoordinator: backgroundCoordinator);
        var query = new ModItemCatalogQuery
        {
            ModsRoot = modsRoot.Path,
            SearchQuery = "hair",
            EntityKindFilter = "Cas",
            SubTypeFilter = "Hair",
            SortBy = "Name",
            PageIndex = 1,
            PageSize = 50
        };

        Assert.True(controller.QueueModsQueryIdlePrewarm(query, "test"));

        await WaitForAsync(() => catalogService.CallCount > 0, timeoutMs: 3000);

        Assert.Equal(1, catalogService.CallCount);
        Assert.NotNull(catalogService.LastQuery);
        Assert.Equal(modsRoot.Path, catalogService.LastQuery!.ModsRoot);
        Assert.True(controller.TryGetModsQueryPrewarmState(query, out var state));
        Assert.NotNull(state);
        Assert.Equal(BackgroundPrewarmJobRunState.Completed, state!.RunState);
    }

    [Fact]
    public async Task AppIdlePrewarmBootstrapper_QueuesModCatalogWarmupAfterIdleDelay()
    {
        using var modsRoot = new TempDirectory("warmup-modquery-idle");
        var packagePath = Path.Combine(modsRoot.Path, "sample.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var configurationProvider = new StaticConfigurationProvider(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Performance.IdlePrewarm.Enabled"] = true,
            ["Performance.IdlePrewarm.DelayMs"] = 50,
            ["Performance.IdlePrewarm.ModQueryPrimeEnabled"] = true
        });
        var catalogService = new RecordingModItemCatalogService();
        var backgroundCoordinator = new BackgroundCachePrewarmCoordinator();
        var controller = new MainWindowCacheWarmupController(
            new StaticInventoryService(packagePath),
            new NoOpModScheduler(),
            new FastPackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance,
            catalogService,
            saveHouseholdCoordinator: null,
            pathIdentityResolver: null,
            configurationProvider: configurationProvider,
            backgroundCachePrewarmCoordinator: backgroundCoordinator);
        var bootstrapper = new AppIdlePrewarmBootstrapper(
            controller,
            new UiActivityMonitor(),
            NullLogger<AppIdlePrewarmBootstrapper>.Instance,
            configurationProvider);
        var query = new ModItemCatalogQuery
        {
            ModsRoot = modsRoot.Path,
            SearchQuery = string.Empty,
            EntityKindFilter = "All",
            SubTypeFilter = "All",
            SortBy = "Last Indexed",
            PageIndex = 1,
            PageSize = 50
        };

        bootstrapper.QueueModCatalogStartupPrewarm(query);

        await WaitForAsync(() => catalogService.CallCount > 0, timeoutMs: 3000);

        Assert.Equal(1, catalogService.CallCount);
        Assert.True(controller.TryGetModsQueryPrewarmState(query, out var state));
        Assert.NotNull(state);
        Assert.Equal(BackgroundPrewarmJobRunState.Completed, state!.RunState);
    }

    private static MainWindowCacheWarmupHost CreateNoOpHost()
    {
        return new MainWindowCacheWarmupHost
        {
            ReportProgress = _ => { },
            AppendLog = _ => { }
        };
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs)
    {
        var startedAt = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - startedAt > timeoutMs)
            {
                throw new TimeoutException("Condition was not met before timeout.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class StaticInventoryService : IModPackageInventoryService
    {
        private readonly string _packagePath;

        public StaticInventoryService(string packagePath)
        {
            _packagePath = packagePath;
        }

        public Task<ModPackageInventoryRefreshResult> RefreshAsync(
            string modsRoot,
            IProgress<ModPackageInventoryRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = new ModPackageInventoryEntry
            {
                PackagePath = Path.GetFullPath(_packagePath),
                FileLength = 4,
                LastWriteUtcTicks = DateTime.UtcNow.Ticks,
                PackageType = ".package",
                ScopeHint = "CAS"
            };
            return Task.FromResult(new ModPackageInventoryRefreshResult
            {
                Snapshot = new ModPackageInventorySnapshot
                {
                    ModsRootPath = modsRoot,
                    InventoryVersion = 1,
                    Entries = [entry],
                    LastValidatedUtcTicks = DateTime.UtcNow.Ticks
                },
                AddedEntries = [entry]
            });
        }
    }

    private sealed class RecordingModItemCatalogService : IModItemCatalogService
    {
        public int CallCount { get; private set; }

        public ModItemCatalogQuery? LastQuery { get; private set; }

        public Task<ModItemCatalogPage> QueryPageAsync(ModItemCatalogQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastQuery = query;
            return Task.FromResult(new ModItemCatalogPage
            {
                Items = Array.Empty<ModItemListRow>(),
                TotalItems = 0,
                PageIndex = query.PageIndex,
                PageSize = query.PageSize,
                TotalPages = 1
            });
        }
    }

    private sealed class BlockingInventoryService : IModPackageInventoryService
    {
        private readonly string _packagePath;
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> FirstCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount;

        public BlockingInventoryService(string packagePath)
        {
            _packagePath = packagePath;
        }

        public void ReleaseFirstCall() => _release.TrySetResult(true);

        public async Task<ModPackageInventoryRefreshResult> RefreshAsync(
            string modsRoot,
            IProgress<ModPackageInventoryRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref CallCount);
            if (call == 1)
            {
                FirstCallStarted.TrySetResult(true);
                await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            var entry = new ModPackageInventoryEntry
            {
                PackagePath = Path.GetFullPath(_packagePath),
                FileLength = 4,
                LastWriteUtcTicks = DateTime.UtcNow.Ticks,
                PackageType = ".package",
                ScopeHint = "CAS"
            };
            return new ModPackageInventoryRefreshResult
            {
                Snapshot = new ModPackageInventorySnapshot
                {
                    ModsRootPath = modsRoot,
                    InventoryVersion = 1,
                    Entries = [entry],
                    LastValidatedUtcTicks = DateTime.UtcNow.Ticks
                },
                AddedEntries = [entry]
            };
        }
    }

    private sealed class PauseAwareModScheduler : IModItemIndexScheduler
    {
        public TaskCompletionSource<bool> FirstCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount;

        public event EventHandler<ModFastBatchAppliedEventArgs>? FastBatchApplied;
        public event EventHandler<ModEnrichmentAppliedEventArgs>? EnrichmentApplied;
        public event EventHandler? AllWorkCompleted;
        public bool IsFastPassRunning => false;
        public bool IsDeepPassRunning => false;

        public async Task QueueRefreshAsync(
            ModIndexRefreshRequest request,
            IProgress<ModIndexRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref CallCount);
            if (call == 1)
            {
                FirstCallStarted.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new ModIndexRefreshProgress
            {
                Stage = "fast",
                Percent = 100,
                Current = 1,
                Total = 1,
                Detail = "done"
            });
            FastBatchApplied?.Invoke(this, new ModFastBatchAppliedEventArgs
            {
                PackagePaths = request.ChangedPackages
            });
            AllWorkCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class NoOpModScheduler : IModItemIndexScheduler
    {
        public event EventHandler<ModFastBatchAppliedEventArgs>? FastBatchApplied;
        public event EventHandler<ModEnrichmentAppliedEventArgs>? EnrichmentApplied;
        public event EventHandler? AllWorkCompleted;
        public bool IsFastPassRunning => false;
        public bool IsDeepPassRunning => false;

        public Task QueueRefreshAsync(
            ModIndexRefreshRequest request,
            IProgress<ModIndexRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new ModIndexRefreshProgress
            {
                Stage = "fast",
                Percent = 100,
                Current = 1,
                Total = 1,
                Detail = "done"
            });
            return Task.CompletedTask;
        }
    }

    private sealed class PauseAwarePackageIndexCache : IPackageIndexCache
    {
        public TaskCompletionSource<bool> FirstBuildStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int BuildCallCount;

        public Task<PackageIndexSnapshot?> TryLoadSnapshotAsync(
            string modsRootPath,
            long inventoryVersion,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PackageIndexSnapshot?>(null);
        }

        public async Task<PackageIndexSnapshot> BuildSnapshotAsync(
            PackageIndexBuildRequest request,
            IProgress<TrayDependencyExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref BuildCallCount);
            if (call == 1)
            {
                FirstBuildStarted.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new TrayDependencyExportProgress
            {
                Stage = TrayDependencyExportStage.Completed,
                Percent = 100,
                Detail = "ready"
            });
            return new PackageIndexSnapshot
            {
                ModsRootPath = request.ModsRootPath,
                InventoryVersion = request.InventoryVersion,
                Packages = Array.Empty<IndexedPackageFile>()
            };
        }
    }

    private sealed class FastPackageIndexCache : IPackageIndexCache
    {
        public Task<PackageIndexSnapshot?> TryLoadSnapshotAsync(
            string modsRootPath,
            long inventoryVersion,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PackageIndexSnapshot?>(new PackageIndexSnapshot
            {
                ModsRootPath = modsRootPath,
                InventoryVersion = inventoryVersion,
                Packages = Array.Empty<IndexedPackageFile>()
            });
        }

        public Task<PackageIndexSnapshot> BuildSnapshotAsync(
            PackageIndexBuildRequest request,
            IProgress<TrayDependencyExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PackageIndexSnapshot
            {
                ModsRootPath = request.ModsRootPath,
                InventoryVersion = request.InventoryVersion,
                Packages = Array.Empty<IndexedPackageFile>()
            });
        }
    }

    private sealed class CapturingPackageIndexCache : IPackageIndexCache
    {
        public List<PackageIndexBuildRequest> BuildRequests { get; } = [];

        public Task<PackageIndexSnapshot?> TryLoadSnapshotAsync(
            string modsRootPath,
            long inventoryVersion,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PackageIndexSnapshot?>(null);
        }

        public Task<PackageIndexSnapshot> BuildSnapshotAsync(
            PackageIndexBuildRequest request,
            IProgress<TrayDependencyExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            BuildRequests.Add(request);
            return Task.FromResult(new PackageIndexSnapshot
            {
                ModsRootPath = request.ModsRootPath,
                InventoryVersion = request.InventoryVersion,
                Packages = Array.Empty<IndexedPackageFile>()
            });
        }
    }

    private sealed class StaticConfigurationProvider : IConfigurationProvider
    {
        private readonly Dictionary<string, object?> _values;

        public StaticConfigurationProvider(Dictionary<string, object?> values)
        {
            _values = values;
        }

        public Task<T?> GetConfigurationAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (!_values.TryGetValue(key, out var value))
            {
                return Task.FromResult<T?>(default);
            }

            return Task.FromResult((T?)value);
        }

        public Task SetConfigurationAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_values.ContainsKey(key));
        }

        public Task<bool> RemoveConfigurationAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_values.Remove(key));
        }

        public Task<IReadOnlyList<string>> GetAllKeysAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(_values.Keys.ToArray());
        }

        public Task<bool> IsPlatformSpecificAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public string GetPlatformSpecificPrefix()
        {
            return string.Empty;
        }

        public T? GetDefaultValue<T>(string key)
        {
            return default;
        }

        public Task<bool> ResetToDefaultAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<IReadOnlyDictionary<string, object?>> GetConfigurationsAsync(
            IReadOnlyList<string> keys,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                if (_values.TryGetValue(key, out var value))
                {
                    result[key] = value;
                }
            }

            return Task.FromResult<IReadOnlyDictionary<string, object?>>(result);
        }

        public Task SetConfigurationsAsync(
            IReadOnlyDictionary<string, object> configurations,
            CancellationToken cancellationToken = default)
        {
            foreach (var entry in configurations)
            {
                _values[entry.Key] = entry.Value;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
