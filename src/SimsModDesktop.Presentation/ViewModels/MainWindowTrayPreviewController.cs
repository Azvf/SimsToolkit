using System.Text.Json;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Results;
using SimsModDesktop.Presentation.Diagnostics;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowTrayPreviewController
{
    private readonly ITrayPreviewCoordinator _trayPreviewCoordinator;
    private readonly ITrayThumbnailService _trayThumbnailService;
    private readonly IToolkitActionPlanner _toolkitActionPlanner;
    private readonly MainWindowRecoveryController _recoveryController;
    private readonly MainWindowTrayPreviewStateController _trayPreviewStateController;
    private readonly MainWindowTrayPreviewSelectionController _trayPreviewSelectionController;
    private readonly ILogger<MainWindowTrayPreviewController> _logger;
    private CancellationTokenSource? _trayPreviewThumbnailCts;
    private int _trayPreviewThumbnailBatchId;

    public MainWindowTrayPreviewController(
        ITrayPreviewCoordinator trayPreviewCoordinator,
        ITrayThumbnailService trayThumbnailService,
        IToolkitActionPlanner toolkitActionPlanner,
        MainWindowRecoveryController recoveryController,
        MainWindowTrayPreviewStateController trayPreviewStateController,
        MainWindowTrayPreviewSelectionController trayPreviewSelectionController,
        ILogger<MainWindowTrayPreviewController> logger)
    {
        _trayPreviewCoordinator = trayPreviewCoordinator;
        _trayThumbnailService = trayThumbnailService;
        _toolkitActionPlanner = toolkitActionPlanner;
        _recoveryController = recoveryController;
        _trayPreviewStateController = trayPreviewStateController;
        _trayPreviewSelectionController = trayPreviewSelectionController;
        _logger = logger;
    }

    internal Task RunTrayPreviewAsync(MainWindowTrayPreviewHost host, TrayPreviewInput? explicitInput = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        return RunTrayPreviewCoreAsync(host, explicitInput, existingOperationId: null);
    }

    internal Task RunTrayPreviewCoreAsync(
        MainWindowTrayPreviewHost host,
        TrayPreviewInput? explicitInput = null,
        string? existingOperationId = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        return RunTrayPreviewCoreInternalAsync(host, explicitInput, existingOperationId);
    }

    internal Task LoadPreviousTrayPreviewPageAsync(MainWindowTrayPreviewHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return LoadTrayPreviewPageAsync(host, _trayPreviewStateController.CurrentPage - 1);
    }

    internal Task LoadNextTrayPreviewPageAsync(MainWindowTrayPreviewHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return LoadTrayPreviewPageAsync(host, _trayPreviewStateController.CurrentPage + 1);
    }

    internal Task JumpToTrayPreviewPageAsync(MainWindowTrayPreviewHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (!TryParsePreviewJumpPage(_trayPreviewStateController.JumpPageText, out var requestedPageIndex))
        {
            return Task.CompletedTask;
        }

        return LoadTrayPreviewPageAsync(host, requestedPageIndex);
    }

    internal async Task TryAutoLoadTrayPreviewAsync(MainWindowTrayPreviewHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (!host.IsTrayPreviewWorkspace || host.GetIsBusy() || host.GetIsTrayPreviewPageLoading())
        {
            return;
        }

        if (!_toolkitActionPlanner.TryBuildTrayPreviewInput(host.CreatePlanBuilderState(), out var input, out _))
        {
            return;
        }

        if (_trayPreviewCoordinator.TryGetCached(input, out var cached))
        {
            SetTrayPreviewSummary(host, cached.Summary);
            SetTrayPreviewPage(host, cached.Page, cached.LoadedPageCount);
            host.SetStatus(host.LocalizeFormat("status.trayPageLoaded", [cached.Page.PageIndex, cached.Page.TotalPages]));
            return;
        }

        await RunTrayPreviewAsync(host, input);
    }

    internal void ClearTrayPreview(MainWindowTrayPreviewHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.ExecuteOnUi(() =>
        {
            CloseTrayPreviewDetails();
            CancelTrayPreviewThumbnailLoading();
            ClearTrayPreviewSelection(host);
            ClearPreviewItems(host);
            _trayPreviewStateController.Reset(
                host.Localize("preview.noneLoaded"),
                host.LocalizeFormat("preview.page", [0, 0]),
                host.LocalizeFormat("preview.lazyCache", [0, 0]));
            host.NotifyTrayPreviewViewStateChanged();
            host.NotifyCommandStates();
        });
    }

    internal void SetTrayPreviewSummary(MainWindowTrayPreviewHost host, SimsTrayPreviewSummary summary)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(summary);

        host.ExecuteOnUi(() =>
        {
            var breakdown = string.IsNullOrWhiteSpace(summary.PresetTypeBreakdown)
                ? host.Localize("preview.typeNa")
                : host.LocalizeFormat("preview.type", [summary.PresetTypeBreakdown]);
            _trayPreviewStateController.ApplySummary(
                summary.TotalItems.ToString("N0"),
                summary.TotalFiles.ToString("N0"),
                $"{summary.TotalMB:N2} MB",
                summary.LatestWriteTimeLocal == DateTime.MinValue
                    ? "-"
                    : summary.LatestWriteTimeLocal.ToString("yyyy-MM-dd HH:mm"),
                host.LocalizeFormat("preview.summaryReady", [breakdown]));
        });
    }

    internal void SetTrayPreviewPage(MainWindowTrayPreviewHost host, SimsTrayPreviewPage page, int loadedPageCount)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(page);

        host.ExecuteOnUi(() =>
        {
            CloseTrayPreviewDetails();
            CancelTrayPreviewThumbnailLoading();
            ClearPreviewItems(host);
            foreach (var item in page.Items)
            {
                var viewModel = new TrayPreviewListItemViewModel(
                    item,
                    expandedItem => OnTrayPreviewItemExpanded(host, expandedItem),
                    selectedItem => OpenTrayPreviewDetails(host, selectedItem));
                viewModel.SetSelected(IsTrayPreviewItemSelected(item));
                host.PreviewItems.Add(viewModel);
            }

            ApplyTrayPreviewDebugVisibility(host);

            var firstItemIndex = page.Items.Count == 0 ? 0 : ((page.PageIndex - 1) * page.PageSize) + 1;
            var lastItemIndex = page.Items.Count == 0 ? 0 : firstItemIndex + page.Items.Count - 1;
            var safeTotalPages = Math.Max(page.TotalPages, 1);
            _trayPreviewStateController.ApplyPage(
                page.PageIndex,
                safeTotalPages,
                page.TotalItems.ToString("N0"),
                host.LocalizeFormat("preview.range", [firstItemIndex, lastItemIndex, page.TotalItems]),
                host.LocalizeFormat("preview.page", [page.PageIndex, safeTotalPages]),
                host.LocalizeFormat("preview.lazyCache", [loadedPageCount, safeTotalPages]),
                page.PageIndex.ToString());
            host.NotifyTrayPreviewViewStateChanged();
            host.NotifyCommandStates();
        });

        StartTrayPreviewThumbnailLoading(host, page.PageIndex);
    }

    internal void ApplyTrayPreviewDebugVisibility(MainWindowTrayPreviewHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.ExecuteOnUi(() =>
        {
            foreach (var item in host.PreviewItems)
            {
                item.SetDebugPreviewEnabled(host.TrayPreview.EnableDebugPreview);
            }
        });
    }

    internal void OnTrayPreviewItemExpanded(MainWindowTrayPreviewHost host, TrayPreviewListItemViewModel expandedItem)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(expandedItem);

        LoadTrayPreviewChildThumbnails(host, expandedItem);
    }

    internal void ApplyTrayPreviewSelection(
        MainWindowTrayPreviewHost host,
        TrayPreviewListItemViewModel selectedItem,
        bool controlPressed,
        bool shiftPressed)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(selectedItem);

        _trayPreviewSelectionController.ApplySelection(host.PreviewItems, selectedItem, controlPressed, shiftPressed);
    }

    internal void OpenTrayPreviewDetails(MainWindowTrayPreviewHost host, TrayPreviewListItemViewModel selectedItem)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(selectedItem);

        _trayPreviewSelectionController.OpenDetails(selectedItem);
        LoadTrayPreviewChildThumbnails(host, selectedItem);
    }

    internal void GoBackTrayPreviewDetails(MainWindowTrayPreviewHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var previousItem = _trayPreviewSelectionController.GoBackDetails();
        if (previousItem is not null)
        {
            LoadTrayPreviewChildThumbnails(host, previousItem);
        }
    }

    internal void CloseTrayPreviewDetails()
    {
        _trayPreviewSelectionController.CloseDetails();
    }

    internal void ClearTrayPreviewSelection(MainWindowTrayPreviewHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _trayPreviewSelectionController.ClearSelection(host.PreviewItems);
    }

    private async Task RunTrayPreviewCoreInternalAsync(
        MainWindowTrayPreviewHost host,
        TrayPreviewInput? explicitInput,
        string? existingOperationId)
    {
        if (host.GetExecutionCts() is not null)
        {
            host.SetStatus(host.Localize("status.executionAlreadyRunning"));
            return;
        }

        TrayPreviewInput input;
        if (explicitInput is null)
        {
            if (!_toolkitActionPlanner.TryBuildTrayPreviewInput(host.CreatePlanBuilderState(), out var built, out var validationError))
            {
                host.SetStatus(validationError);
                host.AppendLog("[validation] " + validationError);
                await host.ShowErrorPopupAsync(host.Localize("status.validationFailed"));
                return;
            }

            input = built;
        }
        else
        {
            input = explicitInput;
        }

        var operationId = existingOperationId ?? await _recoveryController.RegisterRecoveryAsync(_recoveryController.BuildTrayPreviewRecoveryPayload(input));

        using var executionCts = new CancellationTokenSource();
        host.SetExecutionCts(executionCts);
        var startedAt = DateTimeOffset.Now;
        _trayPreviewCoordinator.Reset();
        host.ClearLog();
        var timing = PerformanceLogScope.Begin(_logger, "traypreview.load");
        ClearTrayPreview(host);
        host.SetBusy(true);
        host.SetTrayPreviewPageLoading(true);
        host.SetProgress(true, 0, host.Localize("progress.loadingTray"));
        host.AppendLog("[start] " + startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        host.AppendLog("[action] traypreview");
        host.SetStatus(host.Localize("status.trayPreviewLoading"));

        try
        {
            await _recoveryController.MarkRecoveryStartedAsync(operationId);
            var result = await _trayPreviewCoordinator.LoadAsync(input, executionCts.Token);
            SetTrayPreviewSummary(host, result.Summary);
            SetTrayPreviewPage(host, result.Page, result.LoadedPageCount);

            host.AppendLog($"[preview] previewSource={input.PreviewSource.Kind}:{input.PreviewSource.SourceKey}");
            if (!string.IsNullOrWhiteSpace(input.AuthorFilter))
            {
                host.AppendLog($"[preview] authorFilter={input.AuthorFilter}");
            }

            if (!string.IsNullOrWhiteSpace(input.SearchQuery))
            {
                host.AppendLog($"[preview] search={input.SearchQuery}");
            }

            host.AppendLog($"[preview] presetType={input.PresetTypeFilter}");
            host.AppendLog($"[preview] timeFilter={input.TimeFilter}");
            host.AppendLog($"[preview] pageSize={input.PageSize}");
            host.AppendLog($"[preview] totalItems={result.Summary.TotalItems}");

            host.SetStatus(host.LocalizeFormat(
                "status.trayPreviewLoaded",
                [result.Summary.TotalItems, result.Page.TotalPages, timing.Elapsed.ToString("mm\\:ss")]));
            host.SetProgress(false, 100, host.Localize("progress.trayLoaded"));
            await _recoveryController.MarkRecoveryCompletedAsync(
                operationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Succeeded,
                    ResultSummaryJson = JsonSerializer.Serialize(new
                    {
                        result.Summary.TotalItems,
                        result.Page.TotalPages
                    })
                });
            await _recoveryController.SaveResultHistoryAsync(
                SimsAction.TrayPreview,
                "TrayPreview",
                $"Loaded {result.Summary.TotalItems} items",
                operationId);
            timing.Success(null, ("totalItems", result.Summary.TotalItems), ("totalPages", result.Page.TotalPages));
        }
        catch (OperationCanceledException)
        {
            host.AppendLog("[cancelled]");
            host.SetStatus(host.Localize("status.trayPreviewCancelled"));
            host.SetProgress(false, 0, host.Localize("progress.cancelled"));
            await _recoveryController.MarkRecoveryCompletedAsync(
                operationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Cancelled
                });
            await _recoveryController.SaveResultHistoryAsync(SimsAction.TrayPreview, "TrayPreview", "Cancelled", operationId);
            timing.Cancel();
        }
        catch (Exception ex)
        {
            var errorMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? host.Localize("status.unknownTrayPreviewError")
                : ex.Message;
            host.AppendLog("[error] " + errorMessage);
            host.SetStatus(host.Localize("status.trayPreviewFailed"));
            host.SetProgress(false, 0, host.Localize("progress.trayFailed"));
            await _recoveryController.MarkRecoveryCompletedAsync(
                operationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Failed,
                    FailureMessage = errorMessage
                });
            await _recoveryController.SaveResultHistoryAsync(SimsAction.TrayPreview, "TrayPreview", errorMessage, operationId);
            timing.Fail(ex);
            await host.ShowErrorPopupAsync(host.Localize("status.trayPreviewFailed"));
        }
        finally
        {
            host.SetTrayPreviewPageLoading(false);
            host.SetExecutionCts(null);
            host.SetBusy(false);
            host.RefreshValidation();
        }
    }

    private async Task LoadTrayPreviewPageAsync(MainWindowTrayPreviewHost host, int requestedPageIndex)
    {
        if (host.GetIsTrayPreviewPageLoading())
        {
            host.SetStatus(host.Localize("status.trayPageLoadingAlready"));
            return;
        }

        CancelTrayPreviewThumbnailLoading();
        host.SetTrayPreviewPageLoading(true);
        var timing = PerformanceLogScope.Begin(_logger, "traypreview.page.load", ("pageIndex", requestedPageIndex));
        try
        {
            var result = await _trayPreviewCoordinator.LoadPageAsync(requestedPageIndex);
            SetTrayPreviewPage(host, result.Page, result.LoadedPageCount);
            host.SetStatus(host.LocalizeFormat("status.trayPageLoaded", [result.Page.PageIndex, result.Page.TotalPages]));
            timing.Success(null, ("pageIndex", result.Page.PageIndex), ("fromCache", result.FromCache));
        }
        catch (OperationCanceledException)
        {
            host.SetStatus(host.Localize("status.trayPageCancelled"));
            timing.Cancel();
        }
        catch (Exception ex)
        {
            var errorMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? host.Localize("status.unknownTrayPreviewPageError")
                : ex.Message;
            host.AppendLog("[error] " + errorMessage);
            host.SetStatus(host.Localize("status.trayPageFailed"));
            timing.Fail(ex);
            await host.ShowErrorPopupAsync(host.Localize("status.trayPageFailed"));
        }
        finally
        {
            host.SetTrayPreviewPageLoading(false);
        }
    }

    private void ClearPreviewItems(MainWindowTrayPreviewHost host)
    {
        CloseTrayPreviewDetails();
        foreach (var item in host.PreviewItems)
        {
            item.Dispose();
        }

        host.PreviewItems.Clear();
    }

    internal void CancelTrayPreviewThumbnailLoading()
    {
        Interlocked.Increment(ref _trayPreviewThumbnailBatchId);
        var cts = _trayPreviewThumbnailCts;
        _trayPreviewThumbnailCts = null;

        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void StartTrayPreviewThumbnailLoading(MainWindowTrayPreviewHost host, int pageIndex)
    {
        if (host.PreviewItems.Count == 0)
        {
            return;
        }

        var batchId = Interlocked.Increment(ref _trayPreviewThumbnailBatchId);
        var cts = new CancellationTokenSource();
        _trayPreviewThumbnailCts = cts;
        var primaryItems = host.PreviewItems.ToList();
        foreach (var item in primaryItems)
        {
            item.SetThumbnailLoading();
        }

        _ = LoadTrayPreviewThumbnailsAsync(host, primaryItems, pageIndex, batchId, cts, "top-level");
    }

    private async Task LoadTrayPreviewThumbnailsAsync(
        MainWindowTrayPreviewHost host,
        IReadOnlyList<TrayPreviewListItemViewModel> items,
        int pageIndex,
        int batchId,
        CancellationTokenSource cts,
        string stageLabel)
    {
        var cacheHits = 0;
        var generated = 0;
        var failures = 0;
        var maxParallelism = Math.Min(4, Math.Max(2, Environment.ProcessorCount / 2));
        var timing = PerformanceLogScope.Begin(
            _logger,
            "traypreview.thumbnails.batch",
            ("stage", stageLabel),
            ("pageIndex", pageIndex),
            ("count", items.Count));
        using var gate = new SemaphoreSlim(maxParallelism, maxParallelism);

        try
        {
            await LoadTrayPreviewThumbnailBatchAsync(items, gate, batchId, cts).ConfigureAwait(false);

            if (!cts.IsCancellationRequested && IsActiveThumbnailBatch(batchId, cts))
            {
                host.AppendLog(
                    $"[preview-thumbs] page={pageIndex} stage={stageLabel} count={items.Count} cache={cacheHits} generated={generated} failed={failures}");
                timing.Success(
                    null,
                    ("stage", stageLabel),
                    ("pageIndex", pageIndex),
                    ("count", items.Count),
                    ("cacheHits", cacheHits),
                    ("generated", generated),
                    ("failures", failures));
            }
            else
            {
                timing.Cancel(
                    null,
                    ("stage", stageLabel),
                    ("pageIndex", pageIndex),
                    ("count", items.Count),
                    ("cacheHits", cacheHits),
                    ("generated", generated),
                    ("failures", failures));
            }
        }
        catch (OperationCanceledException)
        {
            timing.Cancel(
                null,
                ("stage", stageLabel),
                ("pageIndex", pageIndex),
                ("count", items.Count),
                ("cacheHits", cacheHits),
                ("generated", generated),
                ("failures", failures));
        }
        catch (Exception ex)
        {
            timing.Fail(
                ex,
                null,
                ("stage", stageLabel),
                ("pageIndex", pageIndex),
                ("count", items.Count),
                ("cacheHits", cacheHits),
                ("generated", generated),
                ("failures", failures));
            throw;
        }
        finally
        {
            if (ReferenceEquals(_trayPreviewThumbnailCts, cts))
            {
                _trayPreviewThumbnailCts = null;
            }

            cts.Dispose();
        }

        async Task LoadTrayPreviewThumbnailBatchAsync(
            IReadOnlyList<TrayPreviewListItemViewModel> batchItems,
            SemaphoreSlim localGate,
            int localBatchId,
            CancellationTokenSource localCts)
        {
            if (batchItems.Count == 0)
            {
                return;
            }

            var tasks = batchItems.Select(item =>
                Task.Run(async () =>
                {
                    await localGate.WaitAsync(localCts.Token).ConfigureAwait(false);
                    try
                    {
                        var result = await _trayThumbnailService.GetThumbnailAsync(item.Item, localCts.Token).ConfigureAwait(false);
                        if (result.Success && TryLoadBitmap(result.CacheFilePath, out var bitmap))
                        {
                            if (result.FromCache)
                            {
                                Interlocked.Increment(ref cacheHits);
                            }
                            else
                            {
                                Interlocked.Increment(ref generated);
                            }

                            await host.ExecuteOnUiAsync(() =>
                            {
                                if (IsActiveThumbnailBatch(localBatchId, localCts))
                                {
                                    item.SetThumbnail(bitmap);
                                }
                                else
                                {
                                    bitmap.Dispose();
                                }
                            }).ConfigureAwait(false);
                            return;
                        }

                        Interlocked.Increment(ref failures);
                        await host.ExecuteOnUiAsync(() =>
                        {
                            if (IsActiveThumbnailBatch(localBatchId, localCts))
                            {
                                item.SetThumbnailUnavailable(isError: true);
                            }
                        }).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch
                    {
                        Interlocked.Increment(ref failures);
                        await host.ExecuteOnUiAsync(() =>
                        {
                            if (IsActiveThumbnailBatch(localBatchId, localCts))
                            {
                                item.SetThumbnailUnavailable(isError: true);
                            }
                        }).ConfigureAwait(false);
                    }
                    finally
                    {
                        localGate.Release();
                    }
                }, localCts.Token));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private void LoadTrayPreviewChildThumbnails(MainWindowTrayPreviewHost host, TrayPreviewListItemViewModel parentItem)
    {
        var batchId = _trayPreviewThumbnailBatchId;
        var itemsToLoad = parentItem.ChildItems
            .SelectMany(item => item.EnumerateSelfAndDescendants())
            .Where(item => !item.HasThumbnail && !item.HasThumbnailError && !item.IsThumbnailLoading)
            .ToList();
        if (itemsToLoad.Count == 0)
        {
            return;
        }

        foreach (var item in itemsToLoad)
        {
            item.SetThumbnailLoading();
        }

        var cts = new CancellationTokenSource();
        _ = LoadTrayPreviewThumbnailsAsync(
            host,
            itemsToLoad,
            _trayPreviewStateController.CurrentPage,
            batchId,
            cts,
            "expanded");
    }

    private bool IsActiveThumbnailBatch(int batchId, CancellationTokenSource cts)
    {
        return batchId == _trayPreviewThumbnailBatchId &&
               !cts.IsCancellationRequested &&
               (_trayPreviewThumbnailCts is null || ReferenceEquals(_trayPreviewThumbnailCts, cts));
    }

    private bool IsTrayPreviewItemSelected(SimsTrayPreviewItem item)
    {
        return _trayPreviewSelectionController.IsItemSelected(item);
    }

    private static bool TryLoadBitmap(string cacheFilePath, out Bitmap bitmap)
    {
        bitmap = null!;

        if (string.IsNullOrWhiteSpace(cacheFilePath) || !File.Exists(cacheFilePath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(cacheFilePath);
            bitmap = new Bitmap(stream);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParsePreviewJumpPage(string? rawValue, out int page)
    {
        page = 0;
        return int.TryParse(rawValue?.Trim(), out page);
    }
}
