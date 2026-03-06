using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Presentation.Dialogs;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.Presentation.ViewModels;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Tests;

public sealed class TrayPreviewWorkspaceViewModelTests
{
    [Fact]
    public async Task SetIsActive_DefersInitialLoadUntilTraySectionIsActive()
    {
        using var trayRoot = new TempDirectory();
        using var modsRoot = new TempDirectory();
        var filter = new TrayPreviewPanelViewModel();
        var runner = new CountingTrayPreviewCoordinator();
        var trayDependencies = new TrayDependenciesPanelViewModel
        {
            ModsPath = modsRoot.Path
        };
        var cacheWarmupController = new MainWindowCacheWarmupController(
            new FakeInventoryService(),
            new NoOpModItemIndexScheduler(),
            new FakePackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance);
        var workspace = new TrayPreviewWorkspaceViewModel(
            filter,
            runner,
            new PassiveTrayThumbnailService(),
            new FakeFileDialogService(),
            new FakeTrayDependencyExportService(),
            cacheWarmupController,
            trayDependencies);

        filter.TrayRoot = trayRoot.Path;
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs(null);

        Assert.Equal(0, runner.LoadCount);

        workspace.SetIsActive(true);
        await WaitForAsync(() => runner.LoadCount == 1);

        workspace.SetIsActive(false);
        filter.SearchQuery = "reload-after-return";
        await Task.Delay(350);
        Dispatcher.UIThread.RunJobs(null);

        Assert.Equal(1, runner.LoadCount);

        workspace.SetIsActive(true);
        await WaitForAsync(() => runner.LoadCount == 2);

        workspace.SetIsActive(false);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs(null);
    }

    [Fact]
    public async Task SetIsActive_False_AllowsTrayWarmupToContinueInBackground()
    {
        using var trayRoot = new TempDirectory();
        using var modsRoot = new TempDirectory();
        var filter = new TrayPreviewPanelViewModel();
        var runner = new CountingTrayPreviewCoordinator();
        var trayDependencies = new TrayDependenciesPanelViewModel
        {
            ModsPath = modsRoot.Path
        };
        var packageCache = new ControlledPackageIndexCache();
        var cacheWarmupController = new MainWindowCacheWarmupController(
            new FakeInventoryService(),
            new NoOpModItemIndexScheduler(),
            packageCache,
            NullLogger<MainWindowCacheWarmupController>.Instance);
        var workspace = new TrayPreviewWorkspaceViewModel(
            filter,
            runner,
            new PassiveTrayThumbnailService(),
            new FakeFileDialogService(),
            new FakeTrayDependencyExportService(),
            cacheWarmupController,
            trayDependencies);

        filter.TrayRoot = trayRoot.Path;
        workspace.SetIsActive(true);
        await packageCache.FirstBuildStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        workspace.SetIsActive(false);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs(null);
        Assert.Equal("Background", workspace.TrayDependencyCacheWarmupStageText);
        Assert.InRange(workspace.TrayDependencyCacheWarmupPercent, 0, 99);

        packageCache.ReleaseBuild();
        await WaitForAsync(
            () => cacheWarmupController.TryGetReadyTraySnapshot(modsRoot.Path, out _),
            timeoutMs: 3000);

        workspace.SetIsActive(true);
        await WaitForAsync(() => workspace.IsTrayDependencyCacheReady, timeoutMs: 3000);
        Assert.True(workspace.IsTrayDependencyCacheReady);
        Assert.Equal(1, packageCache.BuildCallCount);

        workspace.SetIsActive(false);
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs(null);
    }

    private sealed class CountingTrayPreviewCoordinator : ITrayPreviewCoordinator
    {
        public int LoadCount { get; private set; }

        public Task<TrayPreviewLoadResult> LoadAsync(
            TrayPreviewInput input,
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(new TrayPreviewLoadResult
            {
                Summary = new SimsTrayPreviewSummary
                {
                    TotalItems = 1,
                    TotalFiles = 1,
                    TotalBytes = 1024,
                    TotalMB = 0.001
                },
                Page = new SimsTrayPreviewPage
                {
                    PageIndex = 1,
                    PageSize = 50,
                    TotalItems = 1,
                    TotalPages = 1,
                    Items = [CreateItem("item-1")]
                },
                LoadedPageCount = 1
            });
        }

        public Task<TrayPreviewPageResult> LoadPageAsync(
            int requestedPageIndex,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayPreviewPageResult
            {
                Page = new SimsTrayPreviewPage
                {
                    PageIndex = 1,
                    PageSize = 50,
                    TotalItems = 1,
                    TotalPages = 1,
                    Items = [CreateItem("item-1")]
                },
                LoadedPageCount = 1,
                FromCache = false
            });
        }

        public bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result)
        {
            result = null!;
            return false;
        }

        public void Invalidate(string? trayRootPath = null)
        {
        }

        public void Reset()
        {
        }
    }

    private sealed class PassiveTrayThumbnailService : ITrayThumbnailService
    {
        public Task<TrayThumbnailResult> GetThumbnailAsync(
            SimsTrayPreviewItem item,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayThumbnailResult
            {
                SourceKind = TrayThumbnailSourceKind.Placeholder,
                Success = false
            });
        }

        public Task CleanupStaleEntriesAsync(
            string trayRootPath,
            IReadOnlyCollection<string> liveItemKeys,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void ResetMemoryCache(string? trayRootPath = null)
        {
        }
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public Task<IReadOnlyList<string>> PickFolderPathsAsync(string title, bool allowMultiple)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        public Task<string?> PickFilePathAsync(string title, string fileTypeName, IReadOnlyList<string> patterns)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickCsvSavePathAsync(string title, string suggestedFileName)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class FakeTrayDependencyExportService : ITrayDependencyExportService
    {
        public Task<TrayDependencyExportResult> ExportAsync(
            TrayDependencyExportRequest request,
            IProgress<TrayDependencyExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayDependencyExportResult
            {
                Success = true
            });
        }
    }

    private sealed class FakeInventoryService : IModPackageInventoryService
    {
        public Task<ModPackageInventoryRefreshResult> RefreshAsync(
            string modsRoot,
            IProgress<ModPackageInventoryRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ModPackageInventoryRefreshResult
            {
                Snapshot = new ModPackageInventorySnapshot
                {
                    ModsRootPath = modsRoot,
                    InventoryVersion = 1,
                    Entries = Array.Empty<ModPackageInventoryEntry>(),
                    LastValidatedUtcTicks = DateTime.UtcNow.Ticks
                }
            });
        }
    }

    private sealed class NoOpModItemIndexScheduler : IModItemIndexScheduler
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
            => Task.CompletedTask;
    }

    private sealed class FakePackageIndexCache : IPackageIndexCache
    {
        public Task<PackageIndexSnapshot?> TryLoadSnapshotAsync(
            string modsRootPath,
            long inventoryVersion,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PackageIndexSnapshot?>(new PackageIndexSnapshot
            {
                ModsRootPath = modsRootPath,
                InventoryVersion = inventoryVersion <= 0 ? 1 : inventoryVersion,
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

    private sealed class ControlledPackageIndexCache : IPackageIndexCache
    {
        public TaskCompletionSource<bool> FirstBuildStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _buildCallCount;
        public int BuildCallCount => Volatile.Read(ref _buildCallCount);

        public void ReleaseBuild()
        {
            _release.TrySetResult(true);
        }

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
            var call = Interlocked.Increment(ref _buildCallCount);
            if (call == 1)
            {
                FirstBuildStarted.TrySetResult(true);
                await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
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

    private static SimsTrayPreviewItem CreateItem(string key)
    {
        return new SimsTrayPreviewItem
        {
            TrayItemKey = key,
            PresetType = "Household",
            DisplayTitle = key,
            TrayRootPath = "D:\\Tray",
            TrayInstanceId = "0x0000000000000001",
            ContentFingerprint = $"fp-{key}"
        };
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 1000)
    {
        var startedAt = DateTime.UtcNow;
        while (!condition())
        {
            Dispatcher.UIThread.RunJobs(null);
            if ((DateTime.UtcNow - startedAt).TotalMilliseconds > timeoutMs)
            {
                break;
            }

            await Task.Delay(10);
        }

        Dispatcher.UIThread.RunJobs(null);
        Assert.True(condition());
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tray-workspace-{Guid.NewGuid():N}");
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
