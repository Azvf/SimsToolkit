using Avalonia.Threading;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.ViewModels.Panels;
using SimsModDesktop.ViewModels.Preview;

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
            legacyCatalogService: null,
            catalogService: new FakeCatalogService(),
            indexScheduler: new NoOpScheduler(),
            scanService: new FakeScanService(),
            inspectService: new FakeInspectService());

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

    private sealed class FakeScanService : IModPackageScanService
    {
        public Task<IReadOnlyList<ModPackageScanResult>> ScanAsync(string modsRoot, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ModPackageScanResult>>(
            [
                new ModPackageScanResult
                {
                    PackagePath = Path.Combine(modsRoot, "demo.package"),
                    FileLength = 1024,
                    LastWriteUtcTicks = DateTime.UtcNow.Ticks,
                    PackageType = ".package",
                    ScopeHint = "CAS"
                }
            ]);
        }
    }

    private sealed class NoOpScheduler : IModItemIndexScheduler
    {
        public event EventHandler<ModFastBatchAppliedEventArgs>? FastBatchApplied;
        public event EventHandler<ModEnrichmentAppliedEventArgs>? EnrichmentApplied;
        public event EventHandler? AllWorkCompleted;
        public bool IsFastPassRunning => false;
        public bool IsDeepPassRunning => false;

        public Task QueueRefreshAsync(IReadOnlyList<string> packagePaths, CancellationToken cancellationToken = default)
        {
            FastBatchApplied?.Invoke(this, new ModFastBatchAppliedEventArgs
            {
                PackagePaths = packagePaths
            });
            EnrichmentApplied?.Invoke(this, new ModEnrichmentAppliedEventArgs
            {
                PackagePaths = packagePaths,
                AffectedItemKeys = ["item-1"]
            });
            AllWorkCompleted?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInspectService : IModItemInspectService
    {
        public Task<ModItemInspectDetail?> TryGetAsync(string itemKey, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ModItemInspectDetail?>(null);
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
