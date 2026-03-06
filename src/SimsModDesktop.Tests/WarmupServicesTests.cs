using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Application.Warmup;
using SimsModDesktop.Presentation.Services;
using SimsModDesktop.Presentation.Warmup;
using SimsModDesktop.Presentation.ViewModels;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Tests;

public sealed class WarmupServicesTests
{
    [Fact]
    public async Task ModsWarmupService_EnsureWorkspaceReadyAsync_PauseThenResume_Completes()
    {
        using var modsRoot = new TempDirectory("warmup-mods");
        var packagePath = Path.Combine(modsRoot.Path, "sample.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var scheduler = new PauseAwareModScheduler();
        var controller = CreateController(new StaticInventoryService(packagePath), scheduler, new FastPackageIndexCache());
        var service = new ModsWarmupService(controller);

        var firstTask = service.EnsureWorkspaceReadyAsync(modsRoot.Path);
        await scheduler.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        service.PauseWarmup(modsRoot.Path, "test-pause");

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstTask);
        Assert.True(service.TryGetWarmupState(modsRoot.Path, out var pausedState));
        Assert.NotNull(pausedState);
        Assert.Equal(WarmupRunState.Paused, pausedState!.State);

        _ = await service.EnsureWorkspaceReadyAsync(modsRoot.Path);

        Assert.True(service.TryGetWarmupState(modsRoot.Path, out var completedState));
        Assert.NotNull(completedState);
        Assert.Equal(WarmupRunState.Completed, completedState!.State);
        Assert.True(scheduler.CallCount >= 2);
    }

    [Fact]
    public async Task TrayWarmupService_EnsureDependencyReadyAsync_CachesReadySnapshot()
    {
        using var modsRoot = new TempDirectory("warmup-tray");
        var packagePath = Path.Combine(modsRoot.Path, "sample.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var packageCache = new FastPackageIndexCache();
        var controller = CreateController(new StaticInventoryService(packagePath), new NoOpModScheduler(), packageCache);
        var service = new TrayWarmupService(controller);

        var first = await service.EnsureDependencyReadyAsync(modsRoot.Path);
        var second = await service.EnsureDependencyReadyAsync(modsRoot.Path);

        Assert.Equal(first.InventoryVersion, second.InventoryVersion);
        Assert.True(service.TryGetReadySnapshot(modsRoot.Path, out var readySnapshot));
        Assert.NotNull(readySnapshot);
        Assert.True(service.TryGetWarmupState(modsRoot.Path, out var completedState));
        Assert.NotNull(completedState);
        Assert.Equal(WarmupRunState.Completed, completedState!.State);
    }

    [Fact]
    public async Task TrayWarmupService_PassesTrayCachePerformanceConfigurations()
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
        var service = new TrayWarmupService(controller);

        _ = await service.EnsureDependencyReadyAsync(modsRoot.Path);

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
    public async Task EnsureInventoryAsync_ReusesInflightRefresh()
    {
        using var modsRoot = new TempDirectory("warmup-inflight");
        var packagePath = Path.Combine(modsRoot.Path, "sample.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var inventory = new BlockingInventoryService(packagePath);
        var controller = CreateController(inventory, new NoOpModScheduler(), new FastPackageIndexCache());

        var first = controller.EnsureInventoryAsync(modsRoot.Path, CreateNoOpHost(), CancellationToken.None);
        await inventory.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = controller.EnsureInventoryAsync(modsRoot.Path, CreateNoOpHost(), CancellationToken.None);

        inventory.ReleaseFirstCall();
        _ = await first;
        _ = await second;

        Assert.Equal(1, inventory.CallCount);
    }

    [Fact]
    public async Task StartupPrewarmService_QueuesTrayDependencyWarmupAfterIdleDelay()
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
        var trayWarmupService = new TrayWarmupService(controller);
        var bootstrapper = new StartupPrewarmService(
            trayWarmupService,
            new ModsWarmupService(controller),
            new SaveWarmupService(controller),
            new UiActivityMonitor(),
            NullLogger<StartupPrewarmService>.Instance,
            configurationProvider);

        bootstrapper.QueueTrayDependencyStartupPrewarm(modsRoot.Path);

        await WaitForAsync(
            () => trayWarmupService.TryGetReadySnapshot(modsRoot.Path, out _),
            timeoutMs: 3000);

        Assert.True(trayWarmupService.TryGetReadySnapshot(modsRoot.Path, out var snapshot));
        Assert.NotNull(snapshot);
        Assert.True(backgroundCoordinator.TryGetState(
            new BackgroundPrewarmJobKey
            {
                JobType = "TrayDependencySnapshotPrewarm",
                SourceKey = controller.ResolveDirectoryPath(modsRoot.Path)
            },
            out var state));
        Assert.NotNull(state);
        Assert.Equal(BackgroundPrewarmJobRunState.Completed, state!.RunState);
    }

    [Fact]
    public async Task ModsWarmupService_QueueQueryIdlePrewarm_PrimesCatalogQueryCache()
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
        var service = new ModsWarmupService(controller);
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

        Assert.True(service.QueueQueryIdlePrewarm(query, "test"));
        await WaitForAsync(() => catalogService.CallCount > 0, timeoutMs: 3000);

        Assert.Equal(1, catalogService.CallCount);
        Assert.NotNull(catalogService.LastQuery);
        Assert.Equal(modsRoot.Path, catalogService.LastQuery!.ModsRoot);
    }

    [Fact]
    public async Task StartupPrewarmService_QueuesModCatalogWarmupAfterIdleDelay()
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
        var bootstrapper = new StartupPrewarmService(
            new TrayWarmupService(controller),
            new ModsWarmupService(controller),
            new SaveWarmupService(controller),
            new UiActivityMonitor(),
            NullLogger<StartupPrewarmService>.Instance,
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
    }

    [Fact]
    public async Task ModsWarmupService_Reset_CancelsQueuedPrewarm_WithoutClearingSharedRuntime()
    {
        using var modsRoot = new TempDirectory("warmup-mods-reset");
        var packagePath = Path.Combine(modsRoot.Path, "sample.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var inventory = new CountingInventoryService(packagePath);
        var backgroundCoordinator = new RecordingBackgroundCachePrewarmCoordinator();
        var controller = new MainWindowCacheWarmupController(
            inventory,
            new NoOpModScheduler(),
            new FastPackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance,
            new RecordingModItemCatalogService(),
            pathIdentityResolver: null,
            configurationProvider: null,
            backgroundCachePrewarmCoordinator: backgroundCoordinator);
        var service = new ModsWarmupService(controller);
        var query = new ModItemCatalogQuery
        {
            ModsRoot = modsRoot.Path,
            SearchQuery = "hair",
            EntityKindFilter = "All",
            SubTypeFilter = "All",
            SortBy = "Last Indexed",
            PageIndex = 1,
            PageSize = 50
        };

        _ = await controller.EnsureInventoryAsync(modsRoot.Path, CreateNoOpHost(), CancellationToken.None);
        Assert.True(service.QueueQueryIdlePrewarm(query, "test"));

        service.Reset();

        Assert.Contains(backgroundCoordinator.Cancellations, entry =>
            entry.JobType == "ModCatalogQueryPrime" &&
            string.Equals(entry.SourceKey, modsRoot.Path, StringComparison.OrdinalIgnoreCase));

        _ = await controller.EnsureInventoryAsync(modsRoot.Path, CreateNoOpHost(), CancellationToken.None);
        Assert.Equal(1, inventory.CallCount);
    }

    [Fact]
    public void TrayWarmupService_Reset_CancelsQueuedPrewarm_WithoutState()
    {
        using var modsRoot = new TempDirectory("warmup-tray-reset");
        var packagePath = Path.Combine(modsRoot.Path, "sample.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var backgroundCoordinator = new RecordingBackgroundCachePrewarmCoordinator();
        var controller = new MainWindowCacheWarmupController(
            new StaticInventoryService(packagePath),
            new NoOpModScheduler(),
            new FastPackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance,
            pathIdentityResolver: null,
            configurationProvider: null,
            backgroundCachePrewarmCoordinator: backgroundCoordinator);
        var service = new TrayWarmupService(controller);

        Assert.True(service.QueueDependencyIdlePrewarm(modsRoot.Path, "test"));

        service.Reset();

        Assert.Contains(backgroundCoordinator.Cancellations, entry =>
            entry.JobType == "TrayDependencySnapshotPrewarm" &&
            string.Equals(entry.SourceKey, modsRoot.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SaveWarmupService_Reset_CancelsQueuedArtifactPrewarm_WithoutDescriptorState()
    {
        using var saveRoot = new TempDirectory("warmup-save-reset");
        var savePath = Path.Combine(saveRoot.Path, "slot_00000001.save");
        File.WriteAllBytes(savePath, [1, 2, 3, 4]);
        var backgroundCoordinator = new RecordingBackgroundCachePrewarmCoordinator();
        var controller = new MainWindowCacheWarmupController(
            new StaticInventoryService(savePath),
            new NoOpModScheduler(),
            new FastPackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance,
            saveHouseholdCoordinator: new StubSaveHouseholdCoordinator(),
            pathIdentityResolver: null,
            configurationProvider: null,
            backgroundCachePrewarmCoordinator: backgroundCoordinator);
        var service = new SaveWarmupService(controller);

        Assert.True(service.QueueArtifactIdlePrewarm(savePath, "household-1", "test"));

        service.Reset();

        Assert.Contains(backgroundCoordinator.Cancellations, entry =>
            entry.JobType == "SavePreviewArtifactPrime" &&
            string.Equals(entry.SourceKey, savePath, StringComparison.OrdinalIgnoreCase));
    }

    private static MainWindowCacheWarmupController CreateController(
        IModPackageInventoryService inventoryService,
        IModItemIndexScheduler scheduler,
        IPackageIndexCache packageIndexCache)
    {
        return new MainWindowCacheWarmupController(
            inventoryService,
            scheduler,
            packageIndexCache,
            NullLogger<MainWindowCacheWarmupController>.Instance);
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

    private sealed class CountingInventoryService : IModPackageInventoryService
    {
        private readonly string _packagePath;

        public CountingInventoryService(string packagePath)
        {
            _packagePath = packagePath;
        }

        public int CallCount { get; private set; }

        public Task<ModPackageInventoryRefreshResult> RefreshAsync(
            string modsRoot,
            IProgress<ModPackageInventoryRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
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

        public Task<ModItemCatalogPage> QueryPageAsync(
            ModItemCatalogQuery query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastQuery = query;
            return Task.FromResult(new ModItemCatalogPage
            {
                PageIndex = query.PageIndex,
                PageSize = query.PageSize,
                TotalItems = 0,
                TotalPages = 0,
                Items = []
            });
        }
    }

    private sealed class RecordingBackgroundCachePrewarmCoordinator : IBackgroundCachePrewarmCoordinator
    {
        public List<(string JobType, string SourceKey, string Reason)> Cancellations { get; } = [];

        public bool TryQueue(
            BackgroundPrewarmJobKey key,
            Func<CancellationToken, Task> work,
            string description)
        {
            return true;
        }

        public bool TryGetState(
            BackgroundPrewarmJobKey key,
            out BackgroundPrewarmJobState? state)
        {
            state = null;
            return false;
        }

        public void Cancel(BackgroundPrewarmJobKey key, string reason)
        {
            Cancellations.Add((key.JobType, key.SourceKey, reason));
        }

        public void CancelBySource(string sourceKey, string reason, string? jobType = null)
        {
            Cancellations.Add((jobType ?? string.Empty, sourceKey, reason));
        }

        public void Reset(string reason = "reset")
        {
        }
    }

    private sealed class StubSaveHouseholdCoordinator : ISaveHouseholdCoordinator
    {
        public IReadOnlyList<SaveFileEntry> GetSaveFiles(string savesRootPath) => [];

        public bool TryLoadHouseholds(string saveFilePath, out SaveHouseholdSnapshot? snapshot, out string error)
        {
            snapshot = null;
            error = string.Empty;
            return false;
        }

        public bool TryGetPreviewDescriptor(string saveFilePath, out SavePreviewDescriptorManifest manifest)
        {
            manifest = new SavePreviewDescriptorManifest();
            return false;
        }

        public bool IsPreviewDescriptorCurrent(string saveFilePath, SavePreviewDescriptorManifest manifest) => false;

        public PreviewSourceRef GetPreviewSource(string saveFilePath) => PreviewSourceRef.ForSaveDescriptor(saveFilePath);

        public Task<SavePreviewDescriptorBuildResult> BuildPreviewDescriptorAsync(
            string saveFilePath,
            IProgress<SavePreviewDescriptorBuildProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<string?> EnsurePreviewArtifactAsync(
            string saveFilePath,
            string householdKey,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void ClearPreviewData(string saveFilePath)
        {
        }

        public SaveHouseholdExportResult Export(SaveHouseholdExportRequest request)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class NoOpModScheduler : IModItemIndexScheduler
    {
        public event EventHandler<ModFastBatchAppliedEventArgs>? FastBatchApplied
        {
            add { }
            remove { }
        }

        public event EventHandler<ModEnrichmentAppliedEventArgs>? EnrichmentApplied
        {
            add { }
            remove { }
        }

        public event EventHandler? AllWorkCompleted
        {
            add { }
            remove { }
        }

        public bool IsFastPassRunning => false;
        public bool IsDeepPassRunning => false;

        public Task QueueRefreshAsync(
            ModIndexRefreshRequest request,
            IProgress<ModIndexRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new ModIndexRefreshProgress
            {
                Stage = "ready",
                Percent = 100,
                Current = request.ChangedPackages.Count + request.PriorityPackages.Count,
                Total = request.ChangedPackages.Count + request.PriorityPackages.Count,
                Detail = "Completed"
            });
            return Task.CompletedTask;
        }
    }

    private sealed class PauseAwareModScheduler : IModItemIndexScheduler
    {
        private readonly TaskCompletionSource _firstCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstCallStarted => _firstCallStarted;
        public int CallCount { get; private set; }

        public event EventHandler<ModFastBatchAppliedEventArgs>? FastBatchApplied
        {
            add { }
            remove { }
        }

        public event EventHandler<ModEnrichmentAppliedEventArgs>? EnrichmentApplied
        {
            add { }
            remove { }
        }

        public event EventHandler? AllWorkCompleted
        {
            add { }
            remove { }
        }

        public bool IsFastPassRunning => false;
        public bool IsDeepPassRunning => false;

        public async Task QueueRefreshAsync(
            ModIndexRefreshRequest request,
            IProgress<ModIndexRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 1)
            {
                _firstCallStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            progress?.Report(new ModIndexRefreshProgress
            {
                Stage = "ready",
                Percent = 100,
                Current = request.ChangedPackages.Count,
                Total = request.ChangedPackages.Count,
                Detail = "Completed"
            });
        }
    }

    private sealed class BlockingInventoryService : IModPackageInventoryService
    {
        private readonly string _packagePath;
        private readonly TaskCompletionSource _firstCallStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public BlockingInventoryService(string packagePath)
        {
            _packagePath = packagePath;
        }

        public TaskCompletionSource FirstCallStarted => _firstCallStarted;
        public int CallCount { get; private set; }

        public void ReleaseFirstCall()
        {
            _releaseFirstCall.TrySetResult();
        }

        public async Task<ModPackageInventoryRefreshResult> RefreshAsync(
            string modsRoot,
            IProgress<ModPackageInventoryRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (CallCount == 1)
            {
                _firstCallStarted.TrySetResult();
                await _releaseFirstCall.Task.WaitAsync(cancellationToken);
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

    private sealed class FastPackageIndexCache : IPackageIndexCache
    {
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
            progress?.Report(new TrayDependencyExportProgress
            {
                Stage = TrayDependencyExportStage.IndexingPackages,
                Percent = 100,
                Detail = "done"
            });
            return Task.FromResult(new PackageIndexSnapshot
            {
                ModsRootPath = request.ModsRootPath,
                InventoryVersion = request.InventoryVersion,
                Packages = request.PackageFiles.Select(file => new IndexedPackageFile
                {
                    FilePath = file.FilePath,
                    Length = file.Length,
                    LastWriteTimeUtc = new DateTime(file.LastWriteUtcTicks, DateTimeKind.Utc),
                    Entries = Array.Empty<PackageIndexEntry>(),
                    TypeIndexes = new Dictionary<uint, PackageTypeIndex>()
                }).ToArray()
            });
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> MaintainAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
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
                Packages = []
            });
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> MaintainAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class StaticConfigurationProvider : IConfigurationProvider
    {
        private readonly IReadOnlyDictionary<string, object?> _values;

        public StaticConfigurationProvider(IReadOnlyDictionary<string, object?> values)
        {
            _values = values;
        }

        public Task<T?> GetConfigurationAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (_values.TryGetValue(key, out var value) && value is T typed)
            {
                return Task.FromResult<T?>(typed);
            }

            return Task.FromResult<T?>(default);
        }

        public Task SetConfigurationAsync<T>(string key, T value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_values.ContainsKey(key));

        public Task<bool> RemoveConfigurationAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<string>> GetAllKeysAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(_values.Keys.ToArray());

        public Task<bool> IsPlatformSpecificAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public string GetPlatformSpecificPrefix()
            => string.Empty;

        public T? GetDefaultValue<T>(string key)
            => default;

        public Task<bool> ResetToDefaultAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, object?>> GetConfigurationsAsync(
            IReadOnlyList<string> keys,
            CancellationToken cancellationToken = default)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                values[key] = _values.TryGetValue(key, out var value) ? value : null;
            }

            return Task.FromResult<IReadOnlyDictionary<string, object?>>(values);
        }

        public Task SetConfigurationsAsync(
            IReadOnlyDictionary<string, object> configurations,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
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
