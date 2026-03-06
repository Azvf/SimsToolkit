using Avalonia.Threading;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Preview;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Tests;

public sealed class TrayLikePreviewSurfaceViewModelTests
{
    [Fact]
    public async Task LoadPageAsync_UsesCurrentInputContext_PerSurface()
    {
        using var trayRoot = new TempDirectory();
        using var saveRoot = new TempDirectory();
        var trayService = new MultiContextPreviewQueryService();
        var thumbnails = new TrackingTrayThumbnailService(expectedStartCount: 0);
        var traySurface = new TrayLikePreviewSurfaceViewModel(trayService, thumbnails);
        var saveSurface = new TrayLikePreviewSurfaceViewModel(trayService, thumbnails);
        var trayFilter = new TrayLikePreviewFilterViewModel();
        var saveFilter = new TrayLikePreviewFilterViewModel();
        var savePath = Path.Combine(saveRoot.Path, "slot_00000001.save");
        File.WriteAllBytes(savePath, [1, 2, 3, 4]);

        traySurface.Configure(
            trayFilter,
            () => trayFilter.BuildInput(PreviewSourceRef.ForTrayRoot(trayRoot.Path)),
            PreviewSurfaceSelectionMode.Multiple,
            autoLoad: false);
        saveSurface.Configure(
            saveFilter,
            () => saveFilter.BuildInput(PreviewSourceRef.ForSaveDescriptor(savePath)),
            PreviewSurfaceSelectionMode.Single,
            autoLoad: false);

        await traySurface.EnsureLoadedAsync(forceReload: true);
        await saveSurface.EnsureLoadedAsync(forceReload: true);
        await traySurface.LoadPageAsync(2);

        Assert.Equal("tray-page-2", traySurface.PreviewItems.Single().Item.TrayItemKey);
    }

    [Fact]
    public void ClearCurrentSource_OnlyInvalidatesCurrentSource()
    {
        using var trayRoot = new TempDirectory();
        var service = new RecordingInvalidatePreviewQueryService();
        var surface = new TrayLikePreviewSurfaceViewModel(service, new TrackingTrayThumbnailService(expectedStartCount: 0));
        var filter = new TrayLikePreviewFilterViewModel();

        surface.Configure(
            filter,
            () => filter.BuildInput(PreviewSourceRef.ForTrayRoot(trayRoot.Path)),
            PreviewSurfaceSelectionMode.Multiple,
            autoLoad: false);

        surface.ClearCurrentSource("cleared", PreviewSourceRef.ForTrayRoot(trayRoot.Path));

        Assert.Equal(1, service.InvalidateCount);
        Assert.Equal(0, service.ResetCount);
    }

    [Fact]
    public async Task EnsureLoadedAsync_LoadsThumbnailsWithBoundedConcurrency()
    {
        using var trayRoot = new TempDirectory();
        var items = Enumerable.Range(1, 20)
            .Select(index => CreateItem(index))
            .ToArray();
        var runner = new SinglePagePreviewQueryService(items);
        var thumbnails = new TrackingTrayThumbnailService(items.Length, delayMs: 80);
        var surface = new TrayLikePreviewSurfaceViewModel(runner, thumbnails);
        var filter = new TrayLikePreviewFilterViewModel();

        surface.Configure(
            filter,
            () => filter.BuildInput(PreviewSourceRef.ForTrayRoot(trayRoot.Path)),
            PreviewSurfaceSelectionMode.Multiple,
            autoLoad: false);

        await surface.EnsureLoadedAsync(forceReload: true);
        await thumbnails.WaitForStartedCountAsync(items.Length);
        await WaitForAsync(() => surface.PreviewItems.All(item => !item.IsThumbnailLoading));

        Assert.Equal(items.Length, thumbnails.StartedCount);
        Assert.True(thumbnails.MaxConcurrentObserved <= 8);

        var firstWave = thumbnails.StartOrder.Take(8).ToArray();
        Assert.Equal(8, firstWave.Length);
        Assert.All(firstWave, key =>
            Assert.DoesNotContain(key, Enumerable.Range(13, 8).Select(index => $"item-{index}")));
    }

    [Fact]
    public async Task PauseBackgroundLoading_CancelsInFlightBatchBeforeTailRequestsStart()
    {
        using var trayRoot = new TempDirectory();
        var items = Enumerable.Range(1, 20)
            .Select(index => CreateItem(index))
            .ToArray();
        var runner = new SinglePagePreviewQueryService(items);
        var thumbnails = new TrackingTrayThumbnailService(items.Length, blockUntilCancellation: true);
        var surface = new TrayLikePreviewSurfaceViewModel(runner, thumbnails);
        var filter = new TrayLikePreviewFilterViewModel();

        surface.Configure(
            filter,
            () => filter.BuildInput(PreviewSourceRef.ForTrayRoot(trayRoot.Path)),
            PreviewSurfaceSelectionMode.Multiple,
            autoLoad: false);

        await surface.EnsureLoadedAsync(forceReload: true);
        await thumbnails.WaitForStartedCountAsync(8);

        surface.PauseBackgroundLoading();
        await Task.Delay(150);
        Dispatcher.UIThread.RunJobs(null);

        Assert.Equal(8, thumbnails.StartedCount);
        Assert.True(surface.PreviewItems.All(item => item.IsThumbnailLoading || item.IsThumbnailPlaceholderVisible));
    }

    private sealed class SinglePagePreviewQueryService : IPreviewQueryService
    {
        private readonly IReadOnlyList<SimsTrayPreviewItem> _items;

        public SinglePagePreviewQueryService(IReadOnlyList<SimsTrayPreviewItem> items)
        {
            _items = items;
        }

        public Task<TrayPreviewLoadResult> LoadAsync(
            TrayPreviewInput input,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayPreviewLoadResult
            {
                Summary = new SimsTrayPreviewSummary
                {
                    TotalItems = _items.Count,
                    TotalFiles = _items.Count,
                    TotalBytes = _items.Count * 1024L,
                    TotalMB = _items.Count / 1024d
                },
                Page = new SimsTrayPreviewPage
                {
                    PageIndex = 1,
                    PageSize = _items.Count,
                    TotalItems = _items.Count,
                    TotalPages = 1,
                    Items = _items
                },
                LoadedPageCount = 1
            });
        }

        public Task<TrayPreviewPageResult> LoadPageAsync(
            TrayPreviewInput input,
            int requestedPageIndex,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayPreviewPageResult
            {
                Page = new SimsTrayPreviewPage
                {
                    PageIndex = 1,
                    PageSize = _items.Count,
                    TotalItems = _items.Count,
                    TotalPages = 1,
                    Items = _items
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

        public void Invalidate(PreviewSourceRef? source = null)
        {
        }

        public void Reset()
        {
        }
    }

    private sealed class MultiContextPreviewQueryService : IPreviewQueryService
    {
        private readonly Dictionary<string, Dictionary<int, SimsTrayPreviewPage>> _pages = new(StringComparer.OrdinalIgnoreCase);

        public MultiContextPreviewQueryService()
        {
            _pages["tray"] = new Dictionary<int, SimsTrayPreviewPage>
            {
                [1] = BuildPage("tray-page-1"),
                [2] = BuildPage("tray-page-2")
            };
            _pages["save"] = new Dictionary<int, SimsTrayPreviewPage>
            {
                [1] = BuildPage("save-page-1"),
                [2] = BuildPage("save-page-2")
            };
        }

        public bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result)
        {
            result = null!;
            return false;
        }

        public Task<TrayPreviewLoadResult> LoadAsync(TrayPreviewInput input, CancellationToken cancellationToken = default)
        {
            var key = GetContextKey(input);
            var page = _pages[key][1];
            return Task.FromResult(new TrayPreviewLoadResult
            {
                Summary = new SimsTrayPreviewSummary
                {
                    TotalItems = 2,
                    TotalFiles = 2,
                    TotalBytes = 2,
                    TotalMB = 0.01
                },
                Page = page,
                LoadedPageCount = 1
            });
        }

        public Task<TrayPreviewPageResult> LoadPageAsync(
            TrayPreviewInput input,
            int requestedPageIndex,
            CancellationToken cancellationToken = default)
        {
            var key = GetContextKey(input);
            var page = _pages[key][requestedPageIndex];
            return Task.FromResult(new TrayPreviewPageResult
            {
                Page = page,
                LoadedPageCount = requestedPageIndex,
                FromCache = false
            });
        }

        public void Invalidate(PreviewSourceRef? source = null)
        {
        }

        public void Reset()
        {
        }

        private static string GetContextKey(TrayPreviewInput input)
        {
            return input.PreviewSource.Kind == PreviewSourceKind.TrayRoot ? "tray" : "save";
        }

        private static SimsTrayPreviewPage BuildPage(string key)
        {
            return new SimsTrayPreviewPage
            {
                PageIndex = key.EndsWith("2", StringComparison.Ordinal) ? 2 : 1,
                PageSize = 1,
                TotalItems = 2,
                TotalPages = 2,
                Items =
                [
                    new SimsTrayPreviewItem
                    {
                        TrayItemKey = key,
                        PresetType = "Lot",
                        DisplayTitle = key
                    }
                ]
            };
        }
    }

    private sealed class RecordingInvalidatePreviewQueryService : IPreviewQueryService
    {
        public int InvalidateCount { get; private set; }
        public int ResetCount { get; private set; }

        public bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result)
        {
            result = null!;
            return false;
        }

        public Task<TrayPreviewLoadResult> LoadAsync(TrayPreviewInput input, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayPreviewLoadResult
            {
                Summary = new SimsTrayPreviewSummary(),
                Page = new SimsTrayPreviewPage()
            });
        }

        public Task<TrayPreviewPageResult> LoadPageAsync(
            TrayPreviewInput input,
            int requestedPageIndex,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayPreviewPageResult { Page = new SimsTrayPreviewPage() });
        }

        public void Invalidate(PreviewSourceRef? source = null)
        {
            InvalidateCount++;
        }

        public void Reset()
        {
            ResetCount++;
        }
    }

    private sealed class TrackingTrayThumbnailService : ITrayThumbnailService
    {
        private readonly object _gate = new();
        private readonly int _delayMs;
        private readonly bool _blockUntilCancellation;
        private readonly TaskCompletionSource _allStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _startOrder = [];
        private int _expectedStartCount;
        private int _startedCount;
        private int _activeCount;
        private int _maxConcurrentObserved;

        public TrackingTrayThumbnailService(int expectedStartCount, int delayMs = 0, bool blockUntilCancellation = false)
        {
            _expectedStartCount = expectedStartCount;
            _delayMs = Math.Max(delayMs, 0);
            _blockUntilCancellation = blockUntilCancellation;
        }

        public int StartedCount => Volatile.Read(ref _startedCount);
        public int MaxConcurrentObserved => Volatile.Read(ref _maxConcurrentObserved);
        public List<string> StartOrder
        {
            get
            {
                lock (_gate)
                {
                    return _startOrder.ToList();
                }
            }
        }

        public async Task<TrayThumbnailResult> GetThumbnailAsync(
            SimsTrayPreviewItem item,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _startOrder.Add(item.TrayItemKey);
            }

            var started = Interlocked.Increment(ref _startedCount);
            if (started >= Volatile.Read(ref _expectedStartCount))
            {
                _allStarted.TrySetResult();
            }

            var active = Interlocked.Increment(ref _activeCount);
            UpdateMaxConcurrent(active);

            try
            {
                if (_blockUntilCancellation)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                else if (_delayMs > 0)
                {
                    await Task.Delay(_delayMs, cancellationToken);
                }

                return new TrayThumbnailResult
                {
                    SourceKind = TrayThumbnailSourceKind.Placeholder,
                    Success = false
                };
            }
            finally
            {
                Interlocked.Decrement(ref _activeCount);
            }
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

        public async Task WaitForStartedCountAsync(int expectedCount, int timeoutMs = 3000)
        {
            Interlocked.Exchange(ref _expectedStartCount, expectedCount);
            if (StartedCount >= expectedCount)
            {
                return;
            }

            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            await _allStarted.Task.WaitAsync(timeoutCts.Token);
        }

        private void UpdateMaxConcurrent(int currentActive)
        {
            while (true)
            {
                var snapshot = Volatile.Read(ref _maxConcurrentObserved);
                if (snapshot >= currentActive)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _maxConcurrentObserved, currentActive, snapshot) == snapshot)
                {
                    return;
                }
            }
        }
    }

    private static SimsTrayPreviewItem CreateItem(int index)
    {
        return new SimsTrayPreviewItem
        {
            TrayItemKey = $"item-{index}",
            PresetType = index % 2 == 0 ? "Lot" : "Household",
            DisplayTitle = $"Item {index}",
            TrayRootPath = "D:\\Tray",
            TrayInstanceId = $"0x{index:X16}",
            ContentFingerprint = $"fp-{index}"
        };
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 1500)
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tray-surface-{Guid.NewGuid():N}");
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
