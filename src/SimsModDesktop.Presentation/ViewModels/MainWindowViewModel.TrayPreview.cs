using System.Diagnostics;
using System.Text.Json;
using Avalonia.Media.Imaging;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Results;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task RunTrayPreviewAsync(TrayPreviewInput? explicitInput = null)
    {
        return RunTrayPreviewCoreAsync(explicitInput, existingOperationId: null);
    }

    private async Task RunTrayPreviewCoreAsync(TrayPreviewInput? explicitInput = null, string? existingOperationId = null)
    {
        if (_executionCts is not null)
        {
            StatusMessage = L("status.executionAlreadyRunning");
            return;
        }

        TrayPreviewInput input;
        if (explicitInput is null)
        {
            if (!_toolkitActionPlanner.TryBuildTrayPreviewInput(CreatePlanBuilderState(), out var built, out var validationError))
            {
                StatusMessage = validationError;
                AppendLog("[validation] " + validationError);
                await ShowErrorPopupAsync(L("status.validationFailed"));
                return;
            }

            input = built;
        }
        else
        {
            input = explicitInput;
        }

        var operationId = existingOperationId ?? await RegisterRecoveryAsync(_recoveryController.BuildTrayPreviewRecoveryPayload(input));

        _executionCts = new CancellationTokenSource();
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        _trayPreviewCoordinator.Reset();
        ClearLog();
        ClearTrayPreview();
        IsBusy = true;
        SetTrayPreviewPageLoading(true);
        SetProgress(isIndeterminate: true, percent: 0, message: L("progress.loadingTray"));
        AppendLog("[start] " + startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        AppendLog("[action] traypreview");
        StatusMessage = L("status.trayPreviewLoading");

        try
        {
            await MarkRecoveryStartedAsync(operationId);
            var result = await _trayPreviewCoordinator.LoadAsync(input, _executionCts.Token);
            stopwatch.Stop();
            SetTrayPreviewSummary(result.Summary);
            SetTrayPreviewPage(result.Page, result.LoadedPageCount);

            AppendLog($"[preview] trayPath={input.TrayPath}");
            if (!string.IsNullOrWhiteSpace(input.AuthorFilter))
            {
                AppendLog($"[preview] authorFilter={input.AuthorFilter}");
            }

            if (!string.IsNullOrWhiteSpace(input.SearchQuery))
            {
                AppendLog($"[preview] search={input.SearchQuery}");
            }

            AppendLog($"[preview] presetType={input.PresetTypeFilter}");
            AppendLog($"[preview] timeFilter={input.TimeFilter}");
            AppendLog($"[preview] pageSize={input.PageSize}");
            AppendLog($"[preview] totalItems={result.Summary.TotalItems}");

            StatusMessage =
                LF("status.trayPreviewLoaded", result.Summary.TotalItems, result.Page.TotalPages, stopwatch.Elapsed.ToString("mm\\:ss"));
            SetProgress(isIndeterminate: false, percent: 100, message: L("progress.trayLoaded"));
            await MarkRecoveryCompletedAsync(
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
            await SaveResultHistoryAsync(SimsAction.TrayPreview, "TrayPreview", $"Loaded {result.Summary.TotalItems} items", operationId);
        }
        catch (OperationCanceledException)
        {
            AppendLog("[cancelled]");
            StatusMessage = L("status.trayPreviewCancelled");
            SetProgress(isIndeterminate: false, percent: 0, message: L("progress.cancelled"));
            await MarkRecoveryCompletedAsync(
                operationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Cancelled
                });
            await SaveResultHistoryAsync(SimsAction.TrayPreview, "TrayPreview", "Cancelled", operationId);
        }
        catch (Exception ex)
        {
            var errorMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? L("status.unknownTrayPreviewError")
                : ex.Message;
            AppendLog("[error] " + errorMessage);
            StatusMessage = L("status.trayPreviewFailed");
            SetProgress(isIndeterminate: false, percent: 0, message: L("progress.trayFailed"));
            await MarkRecoveryCompletedAsync(
                operationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Failed,
                    FailureMessage = errorMessage
                });
            await SaveResultHistoryAsync(SimsAction.TrayPreview, "TrayPreview", errorMessage, operationId);
            await ShowErrorPopupAsync(L("status.trayPreviewFailed"));
        }
        finally
        {
            SetTrayPreviewPageLoading(false);
            _executionCts.Dispose();
            _executionCts = null;
            IsBusy = false;
            RefreshValidationNow();
        }
    }

    private async Task LoadPreviousTrayPreviewPageAsync()
    {
        await LoadTrayPreviewPageAsync(_trayPreviewStateController.CurrentPage - 1);
    }

    private async Task LoadNextTrayPreviewPageAsync()
    {
        await LoadTrayPreviewPageAsync(_trayPreviewStateController.CurrentPage + 1);
    }

    private async Task JumpToTrayPreviewPageAsync()
    {
        if (!TryParsePreviewJumpPage(PreviewJumpPageText, out var requestedPageIndex))
        {
            return;
        }

        await LoadTrayPreviewPageAsync(requestedPageIndex);
    }

    private async Task LoadTrayPreviewPageAsync(int requestedPageIndex)
    {
        if (_isTrayPreviewPageLoading)
        {
            StatusMessage = L("status.trayPageLoadingAlready");
            return;
        }

        CancelTrayPreviewThumbnailLoading();
        SetTrayPreviewPageLoading(true);
        try
        {
            var result = await _trayPreviewCoordinator.LoadPageAsync(requestedPageIndex);
            SetTrayPreviewPage(result.Page, result.LoadedPageCount);
            StatusMessage = LF("status.trayPageLoaded", result.Page.PageIndex, result.Page.TotalPages);
            return;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = L("status.trayPageCancelled");
            return;
        }
        catch (Exception ex)
        {
            var errorMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? L("status.unknownTrayPreviewPageError")
                : ex.Message;
            AppendLog("[error] " + errorMessage);
            StatusMessage = L("status.trayPageFailed");
            await ShowErrorPopupAsync(L("status.trayPageFailed"));
        }
        finally
        {
            SetTrayPreviewPageLoading(false);
        }
    }

    private static bool TryParsePreviewJumpPage(string? rawValue, out int page)
    {
        page = 0;
        return int.TryParse(rawValue?.Trim(), out page);
    }

    private async Task TryAutoLoadTrayPreviewAsync()
    {
        if (!IsTrayPreviewWorkspace || IsBusy || _isTrayPreviewPageLoading)
        {
            return;
        }

        if (!_toolkitActionPlanner.TryBuildTrayPreviewInput(CreatePlanBuilderState(), out var input, out _))
        {
            return;
        }

        if (_trayPreviewCoordinator.TryGetCached(input, out var cached))
        {
            SetTrayPreviewSummary(cached.Summary);
            SetTrayPreviewPage(cached.Page, cached.LoadedPageCount);
            StatusMessage = LF("status.trayPageLoaded", cached.Page.PageIndex, cached.Page.TotalPages);
            return;
        }

        await RunTrayPreviewAsync(input);
    }

    private void ClearTrayPreview()
    {
        ExecuteOnUi(() =>
        {
            CloseTrayPreviewDetails();
            CancelTrayPreviewThumbnailLoading();
            ClearTrayPreviewSelection();
            ClearPreviewItems();
            _trayPreviewStateController.Reset(
                L("preview.noneLoaded"),
                LF("preview.page", 0, 0),
                LF("preview.lazyCache", 0, 0));
            NotifyTrayPreviewViewStateChanged();
            NotifyCommandStates();
        });
    }

    private void SetTrayPreviewSummary(SimsTrayPreviewSummary summary)
    {
        ExecuteOnUi(() =>
        {
            var breakdown = string.IsNullOrWhiteSpace(summary.PresetTypeBreakdown)
                ? L("preview.typeNa")
                : LF("preview.type", summary.PresetTypeBreakdown);
            _trayPreviewStateController.ApplySummary(
                summary.TotalItems.ToString("N0"),
                summary.TotalFiles.ToString("N0"),
                $"{summary.TotalMB:N2} MB",
                summary.LatestWriteTimeLocal == DateTime.MinValue
                    ? "-"
                    : summary.LatestWriteTimeLocal.ToString("yyyy-MM-dd HH:mm"),
                LF("preview.summaryReady", breakdown));
        });
    }

    private void SetTrayPreviewPage(SimsTrayPreviewPage page, int loadedPageCount)
    {
        ExecuteOnUi(() =>
        {
            CloseTrayPreviewDetails();
            CancelTrayPreviewThumbnailLoading();
            ClearPreviewItems();
            foreach (var item in page.Items)
            {
                var viewModel = new TrayPreviewListItemViewModel(item, OnTrayPreviewItemExpanded, OpenTrayPreviewDetails);
                viewModel.SetSelected(IsTrayPreviewItemSelected(item));
                PreviewItems.Add(viewModel);
            }

            ApplyTrayPreviewDebugVisibility();

            var firstItemIndex = page.Items.Count == 0 ? 0 : ((page.PageIndex - 1) * page.PageSize) + 1;
            var lastItemIndex = page.Items.Count == 0 ? 0 : firstItemIndex + page.Items.Count - 1;
            var safeTotalPages = Math.Max(page.TotalPages, 1);
            _trayPreviewStateController.ApplyPage(
                page.PageIndex,
                safeTotalPages,
                page.TotalItems.ToString("N0"),
                LF("preview.range", firstItemIndex, lastItemIndex, page.TotalItems),
                LF("preview.page", page.PageIndex, safeTotalPages),
                LF("preview.lazyCache", loadedPageCount, safeTotalPages),
                page.PageIndex.ToString());
            NotifyTrayPreviewViewStateChanged();
            NotifyCommandStates();
        });

        StartTrayPreviewThumbnailLoading(page.PageIndex);
    }

    private void ClearPreviewItems()
    {
        CloseTrayPreviewDetails();
        foreach (var item in PreviewItems)
        {
            item.Dispose();
        }

        PreviewItems.Clear();
    }

    private void CancelTrayPreviewThumbnailLoading()
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

    private void ApplyTrayPreviewDebugVisibility()
    {
        ExecuteOnUi(() =>
        {
            foreach (var item in PreviewItems)
            {
                item.SetDebugPreviewEnabled(TrayPreview.EnableDebugPreview);
            }
        });
    }

    private void StartTrayPreviewThumbnailLoading(int pageIndex)
    {
        if (PreviewItems.Count == 0)
        {
            return;
        }

        var batchId = Interlocked.Increment(ref _trayPreviewThumbnailBatchId);
        var cts = new CancellationTokenSource();
        _trayPreviewThumbnailCts = cts;
        var primaryItems = PreviewItems.ToList();
        foreach (var item in primaryItems)
        {
            item.SetThumbnailLoading();
        }

        _ = LoadTrayPreviewThumbnailsAsync(primaryItems, pageIndex, batchId, cts, stageLabel: "top-level");
    }

    private async Task LoadTrayPreviewThumbnailsAsync(
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
        using var gate = new SemaphoreSlim(maxParallelism, maxParallelism);

        try
        {
            await LoadTrayPreviewThumbnailBatchAsync(items, gate, batchId, cts).ConfigureAwait(false);

            if (!cts.IsCancellationRequested && IsActiveThumbnailBatch(batchId, cts))
            {
                AppendLog(
                    $"[preview-thumbs] page={pageIndex} stage={stageLabel} count={items.Count} cache={cacheHits} generated={generated} failed={failures}");
            }
        }
        catch (OperationCanceledException)
        {
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
            IReadOnlyList<TrayPreviewListItemViewModel> items,
            SemaphoreSlim localGate,
            int localBatchId,
            CancellationTokenSource localCts)
        {
            if (items.Count == 0)
            {
                return;
            }

            var tasks = items.Select(item =>
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

                            await ExecuteOnUiAsync(() =>
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
                        await ExecuteOnUiAsync(() =>
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
                        await ExecuteOnUiAsync(() =>
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

    private void OnTrayPreviewItemExpanded(TrayPreviewListItemViewModel expandedItem)
    {
        ArgumentNullException.ThrowIfNull(expandedItem);

        LoadTrayPreviewChildThumbnails(expandedItem);
    }

    public void ApplyTrayPreviewSelection(
        TrayPreviewListItemViewModel selectedItem,
        bool controlPressed,
        bool shiftPressed)
    {
        _trayPreviewSelectionController.ApplySelection(PreviewItems, selectedItem, controlPressed, shiftPressed);
    }

    private void OpenTrayPreviewDetails(TrayPreviewListItemViewModel selectedItem)
    {
        ArgumentNullException.ThrowIfNull(selectedItem);
        _trayPreviewSelectionController.OpenDetails(selectedItem);
        LoadTrayPreviewChildThumbnails(selectedItem);
    }

    private void GoBackTrayPreviewDetails()
    {
        var previousItem = _trayPreviewSelectionController.GoBackDetails();
        if (previousItem is not null)
        {
            LoadTrayPreviewChildThumbnails(previousItem);
        }
    }

    private void CloseTrayPreviewDetails()
    {
        _trayPreviewSelectionController.CloseDetails();
    }

    private void LoadTrayPreviewChildThumbnails(TrayPreviewListItemViewModel parentItem)
    {
        ArgumentNullException.ThrowIfNull(parentItem);

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
            itemsToLoad,
            _trayPreviewStateController.CurrentPage,
            batchId,
            cts,
            stageLabel: "expanded");
    }

    private bool IsActiveThumbnailBatch(int batchId, CancellationTokenSource cts)
    {
        return batchId == _trayPreviewThumbnailBatchId &&
               !cts.IsCancellationRequested &&
               (_trayPreviewThumbnailCts is null || ReferenceEquals(_trayPreviewThumbnailCts, cts));
    }

    private void ClearTrayPreviewSelection()
    {
        _trayPreviewSelectionController.ClearSelection(PreviewItems);
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

    private void SetTrayPreviewPageLoading(bool loading)
    {
        ExecuteOnUi(() =>
        {
            _isTrayPreviewPageLoading = loading;
            NotifyCommandStates();
        });
    }
}
