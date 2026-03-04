using System.Threading;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.TextureCompression;

namespace SimsModDesktop.Tests;

public sealed class ModItemIndexSchedulerTests
{
    [Fact]
    public async Task QueueRefreshAsync_FastPass_UsesParallelParsingAndBatchedWrites()
    {
        using var temp = new TempDirectory("scheduler-fast");
        var packagePaths = Enumerable.Range(0, 10)
            .Select(index => CreatePackage(temp.Path, $"pkg-{index:D2}.package"))
            .ToArray();
        var store = new RecordingStore();
        var fastService = new RecordingFastService();
        var scheduler = new ModItemIndexScheduler(store, fastService, new RecordingDeepService());
        var fastEvents = 0;
        scheduler.FastBatchApplied += (_, _) => fastEvents++;

        await scheduler.QueueRefreshAsync(new ModIndexRefreshRequest
        {
            ModsRootPath = temp.Path,
            ChangedPackages = packagePaths,
            AllowDeepEnrichment = false
        });

        Assert.True(fastService.MaxConcurrency > 1);
        Assert.Equal(2, store.FastBatchWrites.Count);
        Assert.Equal(8, store.FastBatchWrites[0].Count);
        Assert.Equal(2, store.FastBatchWrites[1].Count);
        Assert.Equal(2, fastEvents);
    }

    [Fact]
    public async Task QueueRefreshAsync_DeepPass_KeepsPriorityPackagesFirst()
    {
        using var temp = new TempDirectory("scheduler-deep");
        var priorityPath = CreatePackage(temp.Path, "priority.package");
        var changedPath = CreatePackage(temp.Path, "changed.package");
        var store = new RecordingStore();
        store.PackageStates[priorityPath] = CreateFastReadyState(priorityPath);
        store.PackageStates[changedPath] = CreateFastReadyState(changedPath);
        var scheduler = new ModItemIndexScheduler(store, new RecordingFastService(), new RecordingDeepService());
        IReadOnlyList<string>? eventPackages = null;
        scheduler.EnrichmentApplied += (_, args) => eventPackages = args.PackagePaths;

        await scheduler.QueueRefreshAsync(new ModIndexRefreshRequest
        {
            ModsRootPath = temp.Path,
            ChangedPackages = [changedPath],
            PriorityPackages = [priorityPath],
            AllowDeepEnrichment = true
        });

        Assert.Single(store.DeepBatchWrites);
        Assert.Equal([priorityPath, changedPath], store.DeepBatchWrites[0].Select(item => item.PackageState.PackagePath).ToArray());
        Assert.NotNull(eventPackages);
        Assert.Equal([priorityPath, changedPath], eventPackages);
    }

    [Fact]
    public async Task QueueRefreshAsync_ReusesParseContextAcrossFastAndDeepForChangedPackage()
    {
        using var temp = new TempDirectory("scheduler-context");
        var packagePath = CreatePackage(temp.Path, "shared.package");
        var store = new RecordingStore();
        var fastService = new RecordingFastService();
        var deepService = new RecordingDeepService();
        var scheduler = new ModItemIndexScheduler(store, fastService, deepService);

        await scheduler.QueueRefreshAsync(new ModIndexRefreshRequest
        {
            ModsRootPath = temp.Path,
            ChangedPackages = [packagePath],
            AllowDeepEnrichment = true
        });

        Assert.NotNull(fastService.LastParseContext);
        Assert.NotNull(deepService.LastParseContext);
        Assert.Same(fastService.LastParseContext, deepService.LastParseContext);
    }

    [Fact]
    public async Task QueueRefreshAsync_WhenCancelledDuringWrite_AllowsSubsequentRun()
    {
        using var temp = new TempDirectory("scheduler-cancel");
        var packagePaths = Enumerable.Range(0, 10)
            .Select(index => CreatePackage(temp.Path, $"pkg-{index:D2}.package"))
            .ToArray();
        using var cancellationSource = new CancellationTokenSource();
        var store = new CancellingStore(cancellationSource);
        var scheduler = new ModItemIndexScheduler(store, new RecordingFastService(), new RecordingDeepService());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scheduler.QueueRefreshAsync(
            new ModIndexRefreshRequest
            {
                ModsRootPath = temp.Path,
                ChangedPackages = packagePaths,
                AllowDeepEnrichment = false
            },
            cancellationToken: cancellationSource.Token));

        Assert.Single(store.FastBatchWrites);
        Assert.False(scheduler.IsFastPassRunning);
        Assert.False(scheduler.IsDeepPassRunning);

        await scheduler.QueueRefreshAsync(new ModIndexRefreshRequest
        {
            ModsRootPath = temp.Path,
            ChangedPackages = packagePaths.Skip(8).Take(2).ToArray(),
            AllowDeepEnrichment = false
        });

        Assert.Equal(2, store.FastBatchWrites.Count);
    }

    private static string CreatePackage(string rootPath, string fileName)
    {
        var path = Path.Combine(rootPath, fileName);
        var bytes = new byte[96];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0, 4), 1179664964u);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static ModPackageIndexState CreateFastReadyState(string packagePath)
    {
        var fileInfo = new FileInfo(packagePath);
        return new ModPackageIndexState
        {
            PackagePath = fileInfo.FullName,
            FileLength = fileInfo.Length,
            LastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
            PackageType = ".package",
            ScopeHint = "CAS",
            IndexedUtcTicks = DateTime.UtcNow.Ticks,
            ItemCount = 1,
            CasItemCount = 1,
            BuildBuyItemCount = 0,
            UnclassifiedEntityCount = 0,
            TextureResourceCount = 0,
            EditableTextureCount = 0,
            Status = "FastReady"
        };
    }

    private class RecordingStore : IModItemIndexStore
    {
        public Dictionary<string, ModPackageIndexState> PackageStates { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<IReadOnlyList<ModItemFastIndexBuildResult>> FastBatchWrites { get; } = [];
        public List<IReadOnlyList<ModItemEnrichmentBatch>> DeepBatchWrites { get; } = [];

        public virtual Task<ModPackageIndexState?> TryGetPackageStateAsync(string packagePath, CancellationToken cancellationToken = default)
        {
            PackageStates.TryGetValue(Path.GetFullPath(packagePath), out var state);
            return Task.FromResult(state);
        }

        public virtual Task ReplacePackagesFastAsync(IReadOnlyList<ModItemFastIndexBuildResult> buildResults, CancellationToken cancellationToken = default)
        {
            FastBatchWrites.Add(buildResults.ToArray());
            foreach (var result in buildResults)
            {
                PackageStates[result.PackageState.PackagePath] = result.PackageState;
            }

            return Task.CompletedTask;
        }

        public virtual Task ReplacePackageFastAsync(ModItemFastIndexBuildResult buildResult, CancellationToken cancellationToken = default)
        {
            return ReplacePackagesFastAsync([buildResult], cancellationToken);
        }

        public virtual Task ApplyItemEnrichmentBatchesAsync(IReadOnlyList<ModItemEnrichmentBatch> batches, CancellationToken cancellationToken = default)
        {
            DeepBatchWrites.Add(batches.ToArray());
            foreach (var batch in batches)
            {
                PackageStates[batch.PackageState.PackagePath] = batch.PackageState;
            }

            return Task.CompletedTask;
        }

        public virtual Task ApplyItemEnrichmentBatchAsync(ModItemEnrichmentBatch batch, CancellationToken cancellationToken = default)
        {
            return ApplyItemEnrichmentBatchesAsync([batch], cancellationToken);
        }

        public virtual Task ReplacePackageAsync(ModItemIndexBuildResult buildResult, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public virtual Task DeletePackageAsync(string packagePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public virtual Task<ModItemCatalogPage> QueryPageAsync(ModItemCatalogQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ModItemCatalogPage
            {
                Items = Array.Empty<ModItemListRow>(),
                TotalItems = 0,
                PageIndex = 1,
                PageSize = 50,
                TotalPages = 1
            });
        }
        public virtual Task<ModItemInspectDetail?> TryGetInspectAsync(string itemKey, CancellationToken cancellationToken = default) => Task.FromResult<ModItemInspectDetail?>(null);
        public virtual Task<int> CountIndexedPackagesAsync(CancellationToken cancellationToken = default) => Task.FromResult(PackageStates.Count);
    }

    private sealed class CancellingStore : RecordingStore
    {
        private readonly CancellationTokenSource _cancellationSource;
        private int _writeCount;

        public CancellingStore(CancellationTokenSource cancellationSource)
        {
            _cancellationSource = cancellationSource;
        }

        public override Task ReplacePackagesFastAsync(IReadOnlyList<ModItemFastIndexBuildResult> buildResults, CancellationToken cancellationToken = default)
        {
            var task = base.ReplacePackagesFastAsync(buildResults, cancellationToken);
            if (Interlocked.Increment(ref _writeCount) == 1)
            {
                _cancellationSource.Cancel();
            }

            return task;
        }
    }

    private sealed class RecordingFastService : IFastModItemIndexService, IContextAwareFastModItemIndexService
    {
        private int _currentConcurrency;
        private int _maxConcurrency;

        public int MaxConcurrency => _maxConcurrency;
        public ModPackageParseContext? LastParseContext { get; private set; }

        public async Task<ModItemFastIndexBuildResult> BuildFastPackageAsync(string packagePath, CancellationToken cancellationToken = default)
        {
            var parseContext = ModPackageParseContext.Create(packagePath);
            return await BuildFastPackageAsync(parseContext, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ModItemFastIndexBuildResult> BuildFastPackageAsync(ModPackageParseContext parseContext, CancellationToken cancellationToken = default)
        {
            LastParseContext = parseContext;
            var concurrency = Interlocked.Increment(ref _currentConcurrency);
            UpdateMaxConcurrency(concurrency);

            try
            {
                await Task.Delay(40, cancellationToken).ConfigureAwait(false);
                var now = DateTime.UtcNow.Ticks;
                return new ModItemFastIndexBuildResult
                {
                    PackageState = new ModPackageIndexState
                    {
                        PackagePath = parseContext.PackagePath,
                        FileLength = parseContext.FileLength,
                        LastWriteUtcTicks = parseContext.LastWriteUtcTicks,
                        PackageType = ".package",
                        ScopeHint = "CAS",
                        IndexedUtcTicks = now,
                        ItemCount = 1,
                        CasItemCount = 1,
                        BuildBuyItemCount = 0,
                        UnclassifiedEntityCount = 0,
                        TextureResourceCount = 0,
                        EditableTextureCount = 0,
                        Status = "FastReady"
                    },
                    Items = []
                };
            }
            finally
            {
                Interlocked.Decrement(ref _currentConcurrency);
            }
        }

        private void UpdateMaxConcurrency(int concurrency)
        {
            while (true)
            {
                var observed = _maxConcurrency;
                if (concurrency <= observed)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxConcurrency, concurrency, observed) == observed)
                {
                    return;
                }
            }
        }
    }

    private sealed class RecordingDeepService : IDeepModItemEnrichmentService, IContextAwareDeepModItemEnrichmentService
    {
        public ModPackageParseContext? LastParseContext { get; private set; }

        public Task<ModItemEnrichmentBatch> EnrichPackageAsync(string packagePath, CancellationToken cancellationToken = default)
        {
            var parseContext = ModPackageParseContext.Create(packagePath);
            return EnrichPackageAsync(parseContext, cancellationToken);
        }

        public async Task<ModItemEnrichmentBatch> EnrichPackageAsync(ModPackageParseContext parseContext, CancellationToken cancellationToken = default)
        {
            LastParseContext = parseContext;
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            var now = DateTime.UtcNow.Ticks;
            return new ModItemEnrichmentBatch
            {
                PackageState = new ModPackageIndexState
                {
                    PackagePath = parseContext.PackagePath,
                    FileLength = parseContext.FileLength,
                    LastWriteUtcTicks = parseContext.LastWriteUtcTicks,
                    PackageType = ".package",
                    ScopeHint = "CAS",
                    IndexedUtcTicks = now,
                    ItemCount = 1,
                    CasItemCount = 1,
                    BuildBuyItemCount = 0,
                    UnclassifiedEntityCount = 0,
                    TextureResourceCount = 0,
                    EditableTextureCount = 0,
                    Status = "Ready"
                },
                Items = [],
                AffectedItemKeys = [$"{parseContext.PackagePath}|item"]
            };
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
