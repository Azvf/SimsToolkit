using Avalonia.Threading;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Presentation.Dialogs;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Tests;

public sealed class TrayPreviewWorkspaceViewModelTests
{
    [Fact]
    public async Task SetIsActive_DefersInitialLoadUntilTraySectionIsActive()
    {
        using var trayRoot = new TempDirectory();
        var filter = new TrayPreviewPanelViewModel();
        var runner = new CountingTrayPreviewRunner();
        var workspace = new TrayPreviewWorkspaceViewModel(
            filter,
            runner,
            new PassiveTrayThumbnailService(),
            new FakeFileDialogService(),
            new FakeTrayDependencyExportService(),
            new TrayDependenciesPanelViewModel());

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
    }

    private sealed class CountingTrayPreviewRunner : ITrayPreviewRunner
    {
        public int LoadCount { get; private set; }

        public Task<TrayPreviewLoadRunResult> LoadPreviewAsync(
            TrayPreviewInput input,
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(new TrayPreviewLoadRunResult
            {
                Status = ExecutionRunStatus.Success,
                LoadResult = new TrayPreviewLoadResult
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
                }
            });
        }

        public Task<TrayPreviewPageRunResult> LoadPageAsync(
            int requestedPageIndex,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayPreviewPageRunResult
            {
                Status = ExecutionRunStatus.Success,
                PageResult = new TrayPreviewPageResult
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
                }
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
