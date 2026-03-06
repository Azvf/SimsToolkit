using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Application.Warmup;
using SimsModDesktop.Presentation.Services;
using SimsModDesktop.Presentation.Dialogs;
using SimsModDesktop.Presentation.Warmup;
using SimsModDesktop.Presentation.ViewModels;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Tests;

public sealed class ModPreviewWorkspaceViewModelTests
{
    [Fact]
    public async Task RefreshAsync_LoadsIndexedItemsAndPagination()
    {
        using var modsRoot = new TempDirectory();
        var filter = new ModPreviewPanelViewModel
        {
            ModsRoot = modsRoot.Path
        };

        var workspace = new ModPreviewWorkspaceViewModel(
            filter,
            catalogService: new FakeCatalogService(),
            indexScheduler: new NoOpScheduler(),
            cacheWarmupController: CreateModsWarmupService(),
            inspectService: new FakeInspectService(),
            textureEditService: NullModPackageTextureEditService.Instance,
            fileDialogService: new FakeFileDialogService());

        await workspace.RefreshAsync();
        Dispatcher.UIThread.RunJobs(null);

        Assert.Single(workspace.CatalogItems);
        Assert.Equal("Page 1/2", workspace.PageText);
        Assert.Equal("Items: 55 | Page Size: 50", workspace.SummaryText);
        Assert.Equal("CAS 00000001", workspace.CatalogItems[0].Item.DisplayName);
    }

    [Fact]
    public async Task SetIsActive_False_PausesWarmupWithoutForcing100Percent()
    {
        using var modsRoot = new TempDirectory();
        var filter = new ModPreviewPanelViewModel
        {
            ModsRoot = modsRoot.Path
        };
        var scheduler = new BlockingScheduler();
        var warmupController = new MainWindowCacheWarmupController(
            new FakeInventoryService(),
            scheduler,
            new FakePackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance);
        var workspace = new ModPreviewWorkspaceViewModel(
            filter,
            catalogService: new FakeCatalogService(),
            indexScheduler: scheduler,
            cacheWarmupController: new ModsWarmupService(warmupController),
            inspectService: new FakeInspectService(),
            textureEditService: NullModPackageTextureEditService.Instance,
            fileDialogService: new FakeFileDialogService());

        workspace.SetIsActive(true);
        await scheduler.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        workspace.SetIsActive(false);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs(null);

        Assert.Equal("Paused", workspace.CacheWarmupStageText);
        Assert.InRange(workspace.CacheWarmupPercent, 0, 99);
    }

    [Fact]
    public async Task SetIsActive_True_WhenIdlePrimeEnabled_PrimesNextPageInBackground()
    {
        using var modsRoot = new TempDirectory();
        var filter = new ModPreviewPanelViewModel
        {
            ModsRoot = modsRoot.Path
        };
        var configurationProvider = new StaticConfigurationProvider(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Performance.IdlePrewarm.ModQueryPrimeEnabled"] = true,
            ["Performance.IdlePrewarm.DelayMs"] = 50
        });
        var catalogService = new RecordingCatalogService();
        var scheduler = new SilentScheduler();
        var warmupController = new MainWindowCacheWarmupController(
            new FakeInventoryService(),
            scheduler,
            new FakePackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance,
            catalogService,
            pathIdentityResolver: null,
            configurationProvider: configurationProvider,
            backgroundCachePrewarmCoordinator: new BackgroundCachePrewarmCoordinator());
        var workspace = new ModPreviewWorkspaceViewModel(
            filter,
            catalogService: catalogService,
            indexScheduler: scheduler,
            cacheWarmupController: new ModsWarmupService(warmupController),
            inspectService: new FakeInspectService(),
            textureEditService: NullModPackageTextureEditService.Instance,
            fileDialogService: new FakeFileDialogService(),
            uiActivityMonitor: new FakeUiActivityMonitor(DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1)),
            configurationProvider: configurationProvider);

        workspace.SetIsActive(true);
        await WaitForAsync(() => catalogService.Calls.Count >= 2, timeoutMs: 3000);
        Dispatcher.UIThread.RunJobs(null);

        Assert.Equal([1, 2], catalogService.Calls.Select(query => query.PageIndex).Take(2).ToArray());
    }

    private sealed class FakeCatalogService : IModItemCatalogService
    {
        public Task<ModItemCatalogPage> QueryPageAsync(ModItemCatalogQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ModItemCatalogPage
            {
                Items =
                [
                    new ModItemListRow
                    {
                        ItemKey = "item-1",
                        DisplayName = "CAS 00000001",
                        EntityKind = "Cas",
                        EntitySubType = "CAS Part",
                        PackagePath = @"D:\Mods\demo.package",
                        PackageName = "demo",
                        ScopeText = "Cas",
                        ThumbnailStatus = "None",
                        TextureCount = 0,
                        EditableTextureCount = 0,
                        TextureSummaryText = "Textures 0 | Editable 0 | No texture",
                        UpdatedUtcTicks = DateTime.UtcNow.Ticks
                    }
                ],
                TotalItems = 55,
                PageIndex = 1,
                PageSize = 50,
                TotalPages = 2
            });
        }
    }

    private sealed class RecordingCatalogService : IModItemCatalogService
    {
        private readonly object _gate = new();

        public List<ModItemCatalogQuery> Calls { get; } = new();

        public Task<ModItemCatalogPage> QueryPageAsync(ModItemCatalogQuery query, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Calls.Add(new ModItemCatalogQuery
                {
                    ModsRoot = query.ModsRoot,
                    SearchQuery = query.SearchQuery,
                    EntityKindFilter = query.EntityKindFilter,
                    SubTypeFilter = query.SubTypeFilter,
                    SortBy = query.SortBy,
                    PageIndex = query.PageIndex,
                    PageSize = query.PageSize
                });
            }

            return Task.FromResult(new ModItemCatalogPage
            {
                Items =
                [
                    new ModItemListRow
                    {
                        ItemKey = $"item-{query.PageIndex}",
                        DisplayName = $"CAS {query.PageIndex:00000000}",
                        EntityKind = "Cas",
                        EntitySubType = "CAS Part",
                        PackagePath = @"D:\Mods\demo.package",
                        PackageName = "demo",
                        ScopeText = "Cas",
                        ThumbnailStatus = "None",
                        TextureCount = 0,
                        EditableTextureCount = 0,
                        TextureSummaryText = "Textures 0 | Editable 0 | No texture",
                        UpdatedUtcTicks = DateTime.UtcNow.Ticks
                    }
                ],
                TotalItems = 120,
                PageIndex = query.PageIndex,
                PageSize = query.PageSize,
                TotalPages = 3
            });
        }
    }

    private static IModsWarmupService CreateModsWarmupService()
    {
        return new ModsWarmupService(new MainWindowCacheWarmupController(
            new FakeInventoryService(),
            new NoOpScheduler(),
            new FakePackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance));
    }

    private sealed class FakeInventoryService : IModPackageInventoryService
    {
        public Task<ModPackageInventoryRefreshResult> RefreshAsync(
            string modsRoot,
            IProgress<ModPackageInventoryRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var entry = new ModPackageInventoryEntry
            {
                PackagePath = Path.Combine(modsRoot, "demo.package"),
                FileLength = 1024,
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

    private sealed class NoOpScheduler : IModItemIndexScheduler
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
            FastBatchApplied?.Invoke(this, new ModFastBatchAppliedEventArgs
            {
                PackagePaths = request.ChangedPackages
            });
            EnrichmentApplied?.Invoke(this, new ModEnrichmentAppliedEventArgs
            {
                PackagePaths = request.ChangedPackages,
                AffectedItemKeys = ["item-1"]
            });
            AllWorkCompleted?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingScheduler : IModItemIndexScheduler
    {
        public TaskCompletionSource<bool> FirstCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            FirstCallStarted.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class FakePackageIndexCache : IPackageIndexCache
    {
        public Task<PackageIndexSnapshot?> TryLoadSnapshotAsync(
            string modsRootPath,
            long inventoryVersion,
            CancellationToken cancellationToken = default)
            => Task.FromResult<PackageIndexSnapshot?>(null);

        public Task<PackageIndexSnapshot> BuildSnapshotAsync(
            PackageIndexBuildRequest request,
            IProgress<TrayDependencyExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PackageIndexSnapshot
            {
                ModsRootPath = request.ModsRootPath,
                InventoryVersion = request.InventoryVersion,
                Packages = Array.Empty<IndexedPackageFile>()
            });

    }

    private sealed class FakeInspectService : IModItemInspectService
    {
        public Task<ModItemInspectDetail?> TryGetAsync(string itemKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ModItemInspectDetail?>(null);
        }
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public Task<IReadOnlyList<string>> PickFolderPathsAsync(string title, bool allowMultiple)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task<string?> PickFilePathAsync(string title, string fileTypeName, IReadOnlyList<string> patterns)
            => Task.FromResult<string?>(null);

        public Task<string?> PickCsvSavePathAsync(string title, string suggestedFileName)
            => Task.FromResult<string?>(null);
    }

    private sealed class FakeUiActivityMonitor : IUiActivityMonitor
    {
        public FakeUiActivityMonitor(DateTimeOffset lastInteractionUtc)
        {
            LastInteractionUtc = lastInteractionUtc;
        }

        public DateTimeOffset LastInteractionUtc { get; private set; }

        public void RecordInteraction()
        {
            LastInteractionUtc = DateTimeOffset.UtcNow;
        }
    }

    private sealed class SilentScheduler : IModItemIndexScheduler
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
            return Task.CompletedTask;
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

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mods-preview-{Guid.NewGuid():N}");
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
