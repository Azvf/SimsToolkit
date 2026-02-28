using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.Models;
using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class TrayPreviewCoordinatorTests
{
    [Fact]
    public async Task LoadPageAsync_UsesPageCacheAfterFirstLoad()
    {
        var trayDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        try
        {
            var coordinator = new TrayPreviewCoordinator(
                new FakeTrayPreviewService(),
                new TrayPreviewInputValidator());

            var input = new TrayPreviewInput
            {
                TrayPath = trayDir.FullName,
                PageSize = 50
            };

            var firstLoad = await coordinator.LoadAsync(input);
            Assert.Equal(1, firstLoad.Page.PageIndex);
            Assert.Equal(1, firstLoad.LoadedPageCount);

            var secondPage = await coordinator.LoadPageAsync(2);
            Assert.False(secondPage.FromCache);
            Assert.Equal(2, secondPage.Page.PageIndex);
            Assert.Equal(2, secondPage.LoadedPageCount);

            var secondPageCached = await coordinator.LoadPageAsync(2);
            Assert.True(secondPageCached.FromCache);
            Assert.Equal(2, secondPageCached.Page.PageIndex);
            Assert.Equal(2, secondPageCached.LoadedPageCount);
        }
        finally
        {
            trayDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task TryGetCached_RespectsTrayPreviewFilters()
    {
        var trayDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        try
        {
            var coordinator = new TrayPreviewCoordinator(
                new FakeTrayPreviewService(),
                new TrayPreviewInputValidator());

            var inputA = new TrayPreviewInput
            {
                TrayPath = trayDir.FullName,
                PageSize = 50,
                PresetTypeFilter = "Lot",
                AuthorFilter = "author-1",
                TimeFilter = "Last7d",
                SearchQuery = "villa"
            };
            var inputB = inputA with
            {
                SearchQuery = "other"
            };

            await coordinator.LoadAsync(inputA);
            Assert.True(coordinator.TryGetCached(inputA, out _));
            Assert.False(coordinator.TryGetCached(inputB, out _));
        }
        finally
        {
            trayDir.Delete(recursive: true);
        }
    }

    private sealed class FakeTrayPreviewService : ISimsTrayPreviewService
    {
        public Task<SimsTrayPreviewSummary> BuildSummaryAsync(
            SimsTrayPreviewRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SimsTrayPreviewSummary
            {
                TotalItems = 120,
                TotalFiles = 600,
                TotalBytes = 1024,
                TotalMB = 1,
                LatestWriteTimeLocal = DateTime.Now,
                PresetTypeBreakdown = "Lot:120"
            });
        }

        public Task<SimsTrayPreviewPage> BuildPageAsync(
            SimsTrayPreviewRequest request,
            int pageIndex,
            CancellationToken cancellationToken = default)
        {
            var items = Enumerable.Range(1, 5)
                .Select(i => new SimsTrayPreviewItem
                {
                    TrayItemKey = $"Item-{pageIndex}-{i}",
                    PresetType = "Lot"
                })
                .ToList();

            return Task.FromResult(new SimsTrayPreviewPage
            {
                PageIndex = pageIndex,
                PageSize = request.PageSize,
                TotalItems = 120,
                TotalPages = 3,
                Items = items
            });
        }
    }
}

