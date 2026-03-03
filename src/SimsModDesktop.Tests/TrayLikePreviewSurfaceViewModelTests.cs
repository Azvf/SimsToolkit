using Avalonia.Threading;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Tests;

public sealed class TrayLikePreviewSurfaceViewModelTests
{
    [Fact]
    public async Task EnsureLoadedAsync_LoadsThumbnailsWithBoundedConcurrency()
    {
        using var trayRoot = new TempDirectory();
        var items = Enumerable.Range(1, 20)
            .Select(index => CreateItem(index))
            .ToArray();
        var runner = new SinglePageTrayPreviewCoordinator(items);
        var thumbnails = new TrackingTrayThumbnailService(items.Length, delayMs: 80);
        var surface = new TrayLikePreviewSurfaceViewModel(runner, thumbnails);
        var filter = new TrayLikePreviewFilterViewModel();

        surface.Configure(filter, () => trayRoot.Path, PreviewSurfaceSelectionMode.Multiple, autoLoad: false);

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
        var runner = new SinglePageTrayPreviewCoordinator(items);
        var thumbnails = new TrackingTrayThumbnailService(items.Length, blockUntilCancellation: true);
        var surface = new TrayLikePreviewSurfaceViewModel(runner, thumbnails);
        var filter = new TrayLikePreviewFilterViewModel();

        surface.Configure(filter, () => trayRoot.Path, PreviewSurfaceSelectionMode.Multiple, autoLoad: false);

        await surface.EnsureLoadedAsync(forceReload: true);
        await thumbnails.WaitForStartedCountAsync(8);

        surface.PauseBackgroundLoading();
        await Task.Delay(150);
        Dispatcher.UIThread.RunJobs(null);

        Assert.Equal(8, thumbnails.StartedCount);
        Assert.True(surface.PreviewItems.All(item => item.IsThumbnailLoading || item.IsThumbnailPlaceholderVisible));
    }

    private sealed class SinglePageTrayPreviewCoordinator : ITrayPreviewCoordinator
    {
        private readonly IReadOnlyList<SimsTrayPreviewItem> _items;

        public SinglePageTrayPreviewCoordinator(IReadOnlyList<SimsTrayPreviewItem> items)
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

        public void Invalidate(string? trayRootPath = null)
        {
        }

        public void Reset()
        {
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
