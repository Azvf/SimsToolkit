using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Presentation.Dialogs;
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
            cacheWarmupController: CreateCacheWarmupController(),
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

    private static MainWindowCacheWarmupController CreateCacheWarmupController()
    {
        return new MainWindowCacheWarmupController(
            new FakeInventoryService(),
            new NoOpScheduler(),
            new FakePackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance);
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
