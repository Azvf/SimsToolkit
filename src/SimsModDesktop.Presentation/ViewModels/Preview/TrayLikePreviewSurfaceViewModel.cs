using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Presentation.ViewModels.Infrastructure;
using SimsModDesktop.Presentation.ViewModels.Panels;

namespace SimsModDesktop.Presentation.ViewModels.Preview;

public enum PreviewSurfaceSelectionMode
{
    Single,
    Multiple
}

public sealed class TrayLikePreviewSurfaceViewModel : ObservableObject, IDisposable
{
    private const int InitialPriorityThumbnailCount = 12;
    private const int MaxConcurrentThumbnailLoads = 8;

    private readonly ITrayPreviewRunner _trayPreviewRunner;
    private readonly ITrayThumbnailService _trayThumbnailService;
    private readonly HashSet<string> _selectedKeys = new(StringComparer.OrdinalIgnoreCase);

    private TrayLikePreviewFilterViewModel? _filter;
    private Func<string>? _trayPathProvider;
    private CancellationTokenSource? _autoReloadCts;
    private CancellationTokenSource? _thumbnailCts;
    private bool _isBusy;
    private bool _hasLoadedOnce;
    private string _statusText = "Preview is ready.";
    private string _summaryText = "No preview data loaded.";
    private string _pageText = "Page 0/0";
    private string _lazyLoadText = "Lazy cache 0/0 pages";
    private string _jumpPageText = string.Empty;
    private bool _isDetailVisible;
    private string _footerTitle = string.Empty;
    private string _footerText = string.Empty;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private PreviewSurfaceSelectionMode _selectionMode = PreviewSurfaceSelectionMode.Single;
    private string? _selectionAnchorKey;
    private TrayPreviewListItemViewModel? _selectedItem;
    private int _thumbnailBatchId;
    private bool _backgroundLoadingPaused;
    private bool _refreshRequestedWhilePaused;

    public TrayLikePreviewSurfaceViewModel(
        ITrayPreviewRunner trayPreviewRunner,
        ITrayThumbnailService trayThumbnailService)
    {
        _trayPreviewRunner = trayPreviewRunner;
        _trayThumbnailService = trayThumbnailService;

        PreviewItems = new ObservableCollection<TrayPreviewListItemViewModel>();
        PreviewItems.CollectionChanged += OnPreviewItemsChanged;
        ActionButtons = new ObservableCollection<PreviewSurfaceActionButtonViewModel>();

        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => HasValidTrayPath);
        PrevPageCommand = new AsyncRelayCommand(LoadPreviousPageAsync, () => CanGoPrevPage);
        NextPageCommand = new AsyncRelayCommand(LoadNextPageAsync, () => CanGoNextPage);
        JumpPageCommand = new AsyncRelayCommand(JumpPageAsync, () => CanJumpPage);
        SelectAllPageCommand = new RelayCommand(SelectAllPage, () => IsMultipleSelection && HasItems);
        ClearSelectionCommand = new RelayCommand(ClearSelection, () => HasSelection);
        CloseDetailCommand = new RelayCommand(CloseDetail, () => IsDetailVisible);
    }

    public ObservableCollection<TrayPreviewListItemViewModel> PreviewItems { get; }
    public ObservableCollection<PreviewSurfaceActionButtonViewModel> ActionButtons { get; }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand PrevPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }
    public AsyncRelayCommand JumpPageCommand { get; }
    public RelayCommand SelectAllPageCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand CloseDetailCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            NotifyComputedStateChanged();
        }
    }

    public bool HasItems => PreviewItems.Count > 0;
    public bool HasSelection => _selectedKeys.Count > 0;
    public bool IsMultipleSelection => _selectionMode == PreviewSurfaceSelectionMode.Multiple;
    public bool ShowSelectionCheckboxes => IsMultipleSelection;
    public bool ShowSelectAllAction => IsMultipleSelection;
    public bool ShowPager => HasItems;
    public bool IsEmptyStateVisible => !IsBusy && !HasItems;
    public bool IsLoadingStateVisible => IsBusy && !HasItems;
    public bool IsFooterVisible => !string.IsNullOrWhiteSpace(FooterTitle) || !string.IsNullOrWhiteSpace(FooterText);
    public bool IsEntryMode => !string.Equals(_filter?.LayoutMode, "Grid", StringComparison.OrdinalIgnoreCase);
    public bool IsGridMode => string.Equals(_filter?.LayoutMode, "Grid", StringComparison.OrdinalIgnoreCase);

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public string SelectionSummaryText => HasItems
        ? $"{_selectedKeys.Count} selected / {PreviewItems.Count} on page"
        : "0 selected";

    public string PageText
    {
        get => _pageText;
        private set => SetProperty(ref _pageText, value);
    }

    public string LazyLoadText
    {
        get => _lazyLoadText;
        private set => SetProperty(ref _lazyLoadText, value);
    }

    public string JumpPageText
    {
        get => _jumpPageText;
        set
        {
            if (!SetProperty(ref _jumpPageText, value ?? string.Empty))
            {
                return;
            }

            JumpPageCommand.NotifyCanExecuteChanged();
        }
    }

    public string FooterTitle
    {
        get => _footerTitle;
        private set
        {
            if (!SetProperty(ref _footerTitle, value ?? string.Empty))
            {
                return;
            }

            OnPropertyChanged(nameof(HasFooterTitle));
            OnPropertyChanged(nameof(IsFooterVisible));
        }
    }

    public string FooterText
    {
        get => _footerText;
        private set
        {
            if (!SetProperty(ref _footerText, value ?? string.Empty))
            {
                return;
            }

            OnPropertyChanged(nameof(IsFooterVisible));
        }
    }

    public bool HasValidTrayPath
    {
        get
        {
            var trayPath = _trayPathProvider?.Invoke() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(trayPath) && Directory.Exists(trayPath);
        }
    }

    public TrayPreviewListItemViewModel? SelectedItem
    {
        get => _selectedItem;
        private set
        {
            if (!SetProperty(ref _selectedItem, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedPreviewItem));
            OnPropertyChanged(nameof(HasSelectedPreviewItem));
            OnPropertyChanged(nameof(IsDetailPlaceholderVisible));
        }
    }

    public SimsTrayPreviewItem? SelectedPreviewItem => SelectedItem?.Item;
    public bool HasSelectedPreviewItem => SelectedPreviewItem is not null;
    public bool IsDetailPlaceholderVisible => !HasSelectedPreviewItem;
    public bool HasFooterTitle => !string.IsNullOrWhiteSpace(FooterTitle);
    public bool IsDetailVisible
    {
        get => _isDetailVisible;
        private set
        {
            if (!SetProperty(ref _isDetailVisible, value))
            {
                return;
            }

            CloseDetailCommand.NotifyCanExecuteChanged();
        }
    }
    public bool CanGoPrevPage => !IsBusy && _currentPage > 1;
    public bool CanGoNextPage => !IsBusy && _currentPage < _totalPages;
    public bool CanJumpPage => !IsBusy && TryParseJumpPage(out _);

    public void Configure(
        TrayLikePreviewFilterViewModel filter,
        Func<string> trayPathProvider,
        PreviewSurfaceSelectionMode selectionMode,
        bool autoLoad = true)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(trayPathProvider);

        if (_filter is not null)
        {
            _filter.PropertyChanged -= OnFilterPropertyChanged;
        }

        _filter = filter;
        _trayPathProvider = trayPathProvider;
        _selectionMode = selectionMode;
        _filter.PropertyChanged += OnFilterPropertyChanged;

        OnPropertyChanged(nameof(IsMultipleSelection));
        OnPropertyChanged(nameof(ShowSelectionCheckboxes));
        OnPropertyChanged(nameof(ShowSelectAllAction));
        OnPropertyChanged(nameof(IsEntryMode));
        OnPropertyChanged(nameof(IsGridMode));
        OnPropertyChanged(nameof(HasValidTrayPath));
        RefreshCommand.NotifyCanExecuteChanged();

        _backgroundLoadingPaused = !autoLoad;
        _refreshRequestedWhilePaused = !autoLoad;

        if (autoLoad)
        {
            _ = RefreshAsync();
        }
    }

    public void SetActionButtons(IEnumerable<PreviewSurfaceActionButtonViewModel> buttons)
    {
        ActionButtons.Clear();
        foreach (var button in buttons)
        {
            ActionButtons.Add(button);
        }
    }

    public void SetFooter(string title, string text)
    {
        FooterTitle = title;
        FooterText = text;
    }

    public void NotifyTrayPathChanged(bool invalidateCaches = false)
    {
        OnPropertyChanged(nameof(HasValidTrayPath));
        RefreshCommand.NotifyCanExecuteChanged();
        if (invalidateCaches)
        {
            _trayPreviewRunner.Invalidate(_trayPathProvider?.Invoke());
        }

        if (_backgroundLoadingPaused)
        {
            _refreshRequestedWhilePaused = true;
            return;
        }

        _ = RefreshAsync();
    }

    public void PauseBackgroundLoading()
    {
        _backgroundLoadingPaused = true;

        _autoReloadCts?.Cancel();
        _autoReloadCts?.Dispose();
        _autoReloadCts = null;

        CancelThumbnailLoading();
    }

    public void ResetAfterCacheClear(string statusText = "Preview cache cleared. Return to Tray to reload.")
    {
        _autoReloadCts?.Cancel();
        _autoReloadCts?.Dispose();
        _autoReloadCts = null;

        _trayPreviewRunner.Invalidate();
        _trayThumbnailService.ResetMemoryCache();
        _hasLoadedOnce = false;
        ClearItems(statusText);
    }

    public Task EnsureLoadedAsync(bool forceReload = false)
    {
        if (!HasValidTrayPath || IsBusy)
        {
            return Task.CompletedTask;
        }

        var needsRefresh = forceReload || _refreshRequestedWhilePaused || !HasItems;
        _backgroundLoadingPaused = false;
        if (!needsRefresh)
        {
            return Task.CompletedTask;
        }

        _refreshRequestedWhilePaused = false;
        return RefreshAsync();
    }

    public IReadOnlyList<TrayPreviewListItemViewModel> GetSelectedItems()
    {
        return PreviewItems.Where(item => item.IsSelected).ToArray();
    }

    public void ApplySelection(TrayPreviewListItemViewModel selectedItem, bool controlPressed, bool shiftPressed)
    {
        ArgumentNullException.ThrowIfNull(selectedItem);

        if (!PreviewItems.Contains(selectedItem))
        {
            return;
        }

        if (!IsMultipleSelection)
        {
            ClearSelection();
            SetItemSelected(selectedItem, true);
            _selectionAnchorKey = BuildSelectionKey(selectedItem.Item);
            SelectedItem = selectedItem;
            return;
        }

        if (shiftPressed)
        {
            ApplyRangeSelection(selectedItem, preserveExisting: controlPressed);
            SelectedItem = selectedItem;
            return;
        }

        var targetSelected = controlPressed ? !selectedItem.IsSelected : true;
        if (!controlPressed)
        {
            ClearSelection();
        }

        SetItemSelected(selectedItem, targetSelected);
        _selectionAnchorKey = BuildSelectionKey(selectedItem.Item);
        SelectedItem = selectedItem;
    }

    public void ToggleChildren(TrayPreviewListItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanToggleChildren)
        {
            return;
        }

        item.ToggleChildrenCommand.Execute(null);
        if (item.IsExpanded)
        {
            LoadChildThumbnails(item);
        }
    }

    public async Task RefreshAsync()
    {
        if (_filter is null || _trayPathProvider is null)
        {
            return;
        }

        _backgroundLoadingPaused = false;
        _refreshRequestedWhilePaused = false;

        var trayPath = _trayPathProvider.Invoke();
        if (string.IsNullOrWhiteSpace(trayPath) || !Directory.Exists(trayPath))
        {
            await ExecuteOnUiAsync(() => ClearItems("Set a valid Tray path to load preview items.")).ConfigureAwait(false);
            return;
        }

        await ExecuteOnUiAsync(() =>
        {
            IsBusy = true;
            StatusText = "Loading preview...";
        }).ConfigureAwait(false);

        try
        {
            var input = _filter.BuildInput(trayPath);
            if (_trayPreviewRunner.TryGetCached(input, out var cached))
            {
                await ExecuteOnUiAsync(() =>
                {
                    ApplyLoadResult(cached);
                    StatusText = "Loaded cached preview results.";
                }).ConfigureAwait(false);
                return;
            }

            var result = await _trayPreviewRunner.LoadPreviewAsync(input).ConfigureAwait(false);
            if (result.Status == ExecutionRunStatus.Success && result.LoadResult is not null)
            {
                await ExecuteOnUiAsync(() =>
                {
                    ApplyLoadResult(result.LoadResult);
                    StatusText = "Preview loaded.";
                }).ConfigureAwait(false);
                return;
            }

            if (result.Status == ExecutionRunStatus.Cancelled)
            {
                return;
            }

            await ExecuteOnUiAsync(() =>
                ClearItems(string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Preview load failed." : result.ErrorMessage))
                .ConfigureAwait(false);
        }
        finally
        {
            await ExecuteOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    public async Task LoadPageAsync(int requestedPageIndex)
    {
        if (!HasValidTrayPath)
        {
            return;
        }

        await ExecuteOnUiAsync(() => IsBusy = true).ConfigureAwait(false);
        try
        {
            var result = await _trayPreviewRunner.LoadPageAsync(requestedPageIndex).ConfigureAwait(false);
            if (result.Status == ExecutionRunStatus.Success && result.PageResult is not null)
            {
                await ExecuteOnUiAsync(() =>
                {
                    ApplyPage(result.PageResult.Page, result.PageResult.LoadedPageCount);
                    StatusText = result.PageResult.FromCache
                        ? "Loaded cached page."
                        : "Preview page loaded.";
                }).ConfigureAwait(false);
                return;
            }

            if (result.Status == ExecutionRunStatus.Cancelled)
            {
                return;
            }

            await ExecuteOnUiAsync(() =>
                StatusText = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Preview page load failed." : result.ErrorMessage)
                .ConfigureAwait(false);
        }
        finally
        {
            await ExecuteOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        PreviewItems.CollectionChanged -= OnPreviewItemsChanged;
        if (_filter is not null)
        {
            _filter.PropertyChanged -= OnFilterPropertyChanged;
        }

        _autoReloadCts?.Cancel();
        _autoReloadCts?.Dispose();
        CancelThumbnailLoading();
        ClearPreviewItems();
    }

    public void OpenDetail(TrayPreviewListItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (!PreviewItems.Contains(item))
        {
            return;
        }

        if (!item.IsSelected)
        {
            ApplySelection(item, controlPressed: false, shiftPressed: false);
        }

        SelectedItem = item;
        IsDetailVisible = true;
    }

    private async Task LoadPreviousPageAsync()
    {
        await LoadPageAsync(_currentPage - 1);
    }

    private async Task LoadNextPageAsync()
    {
        await LoadPageAsync(_currentPage + 1);
    }

    private async Task JumpPageAsync()
    {
        if (!TryParseJumpPage(out var page))
        {
            return;
        }

        await LoadPageAsync(page);
    }

    private void OnFilterPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_filter is null)
        {
            return;
        }

        if (string.Equals(e.PropertyName, nameof(TrayLikePreviewFilterViewModel.LayoutMode), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(IsEntryMode));
            OnPropertyChanged(nameof(IsGridMode));
        }

        if (string.Equals(e.PropertyName, nameof(TrayLikePreviewFilterViewModel.EnableDebugPreview), StringComparison.Ordinal))
        {
            foreach (var item in PreviewItems)
            {
                item.SetDebugPreviewEnabled(_filter.EnableDebugPreview);
            }

            return;
        }

        if (!IsAutoReloadProperty(e.PropertyName))
        {
            return;
        }

        QueueAutoReload();
    }

    private void QueueAutoReload()
    {
        if (_backgroundLoadingPaused)
        {
            _refreshRequestedWhilePaused = true;
            return;
        }

        _autoReloadCts?.Cancel();
        _autoReloadCts?.Dispose();
        _autoReloadCts = new CancellationTokenSource();
        var cts = _autoReloadCts;

        _ = QueueAutoReloadCoreAsync(cts);
    }

    private async Task QueueAutoReloadCoreAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(300, cts.Token).ConfigureAwait(false);
            if (!cts.IsCancellationRequested)
            {
                await RefreshAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool IsAutoReloadProperty(string? propertyName)
    {
        return string.Equals(propertyName, nameof(TrayLikePreviewFilterViewModel.PresetTypeFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayLikePreviewFilterViewModel.BuildSizeFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayLikePreviewFilterViewModel.HouseholdSizeFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayLikePreviewFilterViewModel.AuthorFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayLikePreviewFilterViewModel.TimeFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayLikePreviewFilterViewModel.SearchQuery), StringComparison.Ordinal);
    }

    private void ApplyLoadResult(TrayPreviewLoadResult result)
    {
        ApplySummary(result.Summary);
        ApplyPage(result.Page, result.LoadedPageCount);
    }

    private void ApplySummary(SimsTrayPreviewSummary summary)
    {
        _totalPages = Math.Max(1, (int)Math.Ceiling(summary.TotalItems / (double)Math.Max(1, _filter?.PageSize ?? 50)));
        SummaryText = summary.TotalItems == 0
            ? "No tray items matched the current filters."
            : $"Tray items: {summary.TotalItems} | Files: {summary.TotalFiles} | {summary.TotalMB:0.##} MB";
        PageText = $"Page {_currentPage}/{_totalPages}";
        OnPropertyChanged(nameof(CanGoPrevPage));
        OnPropertyChanged(nameof(CanGoNextPage));
        JumpPageCommand.NotifyCanExecuteChanged();
    }

    private void ApplyPage(SimsTrayPreviewPage page, int loadedPageCount)
    {
        _currentPage = page.PageIndex;
        _totalPages = Math.Max(1, page.TotalPages);
        PageText = $"Page {_currentPage}/{_totalPages}";
        LazyLoadText = $"Lazy cache {loadedPageCount}/{_totalPages} pages";
        JumpPageText = string.Empty;

        var selectedKeys = _selectedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedPrimaryKey = SelectedPreviewItem?.TrayItemKey;
        ClearPreviewItems();

        foreach (var item in page.Items)
        {
            var vm = new TrayPreviewListItemViewModel(item, expandedCallback: OnItemExpanded);
            if (_filter?.EnableDebugPreview == true)
            {
                vm.SetDebugPreviewEnabled(true);
            }

            if (selectedKeys.Contains(BuildSelectionKey(item)))
            {
                vm.SetSelected(true);
            }

            PreviewItems.Add(vm);
        }

        _hasLoadedOnce = true;
        SelectedItem = PreviewItems.FirstOrDefault(item =>
            string.Equals(item.Item.TrayItemKey, selectedPrimaryKey, StringComparison.OrdinalIgnoreCase))
            ?? PreviewItems.FirstOrDefault(item => item.IsSelected)
            ?? PreviewItems.FirstOrDefault();

        if (!IsMultipleSelection && SelectedItem is not null)
        {
            SetItemSelected(SelectedItem, true);
        }

        StartThumbnailLoading();
        NotifyComputedStateChanged();
    }

    private void ClearItems(string statusText)
    {
        _trayPreviewRunner.Reset();
        _selectedKeys.Clear();
        _selectionAnchorKey = null;
        SelectedItem = null;
        IsDetailVisible = false;
        _currentPage = 1;
        _totalPages = 1;
        SummaryText = _hasLoadedOnce
            ? "No tray items matched the current filters."
            : "No preview data loaded.";
        LazyLoadText = "Lazy cache 0/0 pages";
        PageText = "Page 0/0";
        StatusText = statusText;
        ClearPreviewItems();
        NotifyComputedStateChanged();
    }

    private void ClearPreviewItems()
    {
        CancelThumbnailLoading();
        foreach (var item in PreviewItems)
        {
            item.Dispose();
        }

        PreviewItems.Clear();
    }

    private void OnItemExpanded(TrayPreviewListItemViewModel item)
    {
        LoadChildThumbnails(item);
    }

    private void LoadChildThumbnails(TrayPreviewListItemViewModel parentItem)
    {
        if (parentItem.ChildItems.Count == 0)
        {
            return;
        }

        _ = LoadThumbnailBatchAsync(parentItem.ChildItems.ToArray(), _thumbnailBatchId, _thumbnailCts, _thumbnailCts?.Token ?? CancellationToken.None);
    }

    private void StartThumbnailLoading()
    {
        CancelThumbnailLoading();
        if (PreviewItems.Count == 0)
        {
            return;
        }

        if (_backgroundLoadingPaused)
        {
            _refreshRequestedWhilePaused = true;
            return;
        }

        _thumbnailBatchId++;
        _thumbnailCts = new CancellationTokenSource();
        var batchId = _thumbnailBatchId;
        _ = LoadThumbnailBatchAsync(BuildPrioritizedItems(PreviewItems), batchId, _thumbnailCts, _thumbnailCts.Token);
    }

    private async Task LoadThumbnailBatchAsync(
        IReadOnlyList<TrayPreviewListItemViewModel> items,
        int batchId,
        CancellationTokenSource? batchCts,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0 || batchCts is null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (!IsActiveThumbnailBatch(batchId, batchCts))
            {
                return;
            }

            item.SetThumbnailLoading();
        }

        var workerCount = Math.Min(MaxConcurrentThumbnailLoads, items.Count);
        var nextItemIndex = new[] { -1 };
        var workers = new Task[workerCount];
        for (var workerIndex = 0; workerIndex < workerCount; workerIndex++)
        {
            workers[workerIndex] = Task.Run(
                () => RunThumbnailWorkerAsync(items, nextItemIndex, batchId, batchCts, cancellationToken));
        }

        await Task.WhenAll(workers).ConfigureAwait(false);
    }

    private async Task RunThumbnailWorkerAsync(
        IReadOnlyList<TrayPreviewListItemViewModel> items,
        int[] nextItemIndex,
        int batchId,
        CancellationTokenSource batchCts,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (!IsActiveThumbnailBatch(batchId, batchCts))
            {
                return;
            }

            var itemIndex = Interlocked.Increment(ref nextItemIndex[0]);
            if (itemIndex < 0 || itemIndex >= items.Count)
            {
                return;
            }

            var item = items[itemIndex];
            TrayThumbnailResult result;
            Bitmap? bitmap = null;
            var wasCancelled = false;
            try
            {
                (wasCancelled, result, bitmap) = ResolveThumbnailResult(item.Item, cancellationToken);
            }
            catch
            {
                result = new TrayThumbnailResult
                {
                    SourceKind = TrayThumbnailSourceKind.Placeholder,
                    Success = false
                };
            }

            if (wasCancelled)
            {
                bitmap?.Dispose();
                return;
            }

            if (!IsActiveThumbnailBatch(batchId, batchCts))
            {
                bitmap?.Dispose();
                return;
            }

            await ExecuteOnUiAsync(() =>
            {
                if (!IsActiveThumbnailBatch(batchId, batchCts))
                {
                    bitmap?.Dispose();
                    return;
                }

                if (result.Success && bitmap is not null)
                {
                    item.SetThumbnail(bitmap);
                    return;
                }

                item.SetThumbnailUnavailable(isError: false);
            }).ConfigureAwait(false);
        }
    }

    private (bool WasCancelled, TrayThumbnailResult Result, Bitmap? Bitmap) ResolveThumbnailResult(
        SimsTrayPreviewItem item,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = _trayThumbnailService.GetThumbnailAsync(item, cancellationToken).GetAwaiter().GetResult();
            if (result.Success && TryLoadBitmap(result.CacheFilePath, out var bitmap))
            {
                return (false, result, bitmap);
            }

            return (false, result, null);
        }
        catch (OperationCanceledException)
        {
            return (true, new TrayThumbnailResult
            {
                SourceKind = TrayThumbnailSourceKind.Placeholder,
                Success = false
            }, null);
        }
    }

    private bool IsActiveThumbnailBatch(int batchId, CancellationTokenSource cts)
    {
        return batchId == _thumbnailBatchId &&
               !cts.IsCancellationRequested &&
               ReferenceEquals(_thumbnailCts, cts);
    }

    private static IReadOnlyList<TrayPreviewListItemViewModel> BuildPrioritizedItems(
        IReadOnlyList<TrayPreviewListItemViewModel> items)
    {
        if (items.Count <= InitialPriorityThumbnailCount)
        {
            return items.ToArray();
        }

        var prioritized = new TrayPreviewListItemViewModel[items.Count];
        var outputIndex = 0;
        for (var i = 0; i < InitialPriorityThumbnailCount; i++)
        {
            prioritized[outputIndex++] = items[i];
        }

        for (var i = InitialPriorityThumbnailCount; i < items.Count; i++)
        {
            prioritized[outputIndex++] = items[i];
        }

        return prioritized;
    }

    private void CancelThumbnailLoading()
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = null;
    }

    private void SelectAllPage()
    {
        if (!IsMultipleSelection || PreviewItems.Count == 0)
        {
            return;
        }

        foreach (var item in PreviewItems)
        {
            SetItemSelected(item, true);
        }

        _selectionAnchorKey = BuildSelectionKey(PreviewItems[^1].Item);
        if (SelectedItem is null)
        {
            SelectedItem = PreviewItems[0];
        }
    }

    private void ClearSelection()
    {
        foreach (var item in PreviewItems.Where(item => item.IsSelected))
        {
            item.SetSelected(false);
        }

        _selectedKeys.Clear();
        _selectionAnchorKey = null;
        if (!IsMultipleSelection)
        {
            SelectedItem = null;
        }

        NotifyComputedStateChanged();
    }

    private void CloseDetail()
    {
        IsDetailVisible = false;
    }

    private void ApplyRangeSelection(TrayPreviewListItemViewModel selectedItem, bool preserveExisting)
    {
        var targetIndex = PreviewItems.IndexOf(selectedItem);
        if (targetIndex < 0)
        {
            return;
        }

        var anchorIndex = -1;
        if (!string.IsNullOrWhiteSpace(_selectionAnchorKey))
        {
            anchorIndex = PreviewItems
                .Select((item, index) => new { item, index })
                .Where(entry => string.Equals(BuildSelectionKey(entry.item.Item), _selectionAnchorKey, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.index)
                .DefaultIfEmpty(-1)
                .First();
        }

        if (anchorIndex < 0)
        {
            if (!preserveExisting)
            {
                ClearSelection();
            }

            SetItemSelected(selectedItem, true);
            return;
        }

        if (!preserveExisting)
        {
            ClearSelection();
        }

        var startIndex = Math.Min(anchorIndex, targetIndex);
        var endIndex = Math.Max(anchorIndex, targetIndex);
        for (var index = startIndex; index <= endIndex; index++)
        {
            SetItemSelected(PreviewItems[index], true);
        }
    }

    private void SetItemSelected(TrayPreviewListItemViewModel item, bool selected)
    {
        var key = BuildSelectionKey(item.Item);
        var changed = selected
            ? _selectedKeys.Add(key)
            : _selectedKeys.Remove(key);

        item.SetSelected(selected);

        if (changed)
        {
            NotifyComputedStateChanged();
        }
    }

    private static string BuildSelectionKey(SimsTrayPreviewItem item)
    {
        return string.IsNullOrWhiteSpace(item.TrayItemKey)
            ? item.ContentFingerprint
            : item.TrayItemKey;
    }

    private bool TryParseJumpPage(out int page)
    {
        return int.TryParse(JumpPageText, out page) && page >= 1 && page <= _totalPages;
    }

    private void NotifyComputedStateChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionSummaryText));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(IsLoadingStateVisible));
        OnPropertyChanged(nameof(ShowPager));
        OnPropertyChanged(nameof(CanGoPrevPage));
        OnPropertyChanged(nameof(CanGoNextPage));
        PrevPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        JumpPageCommand.NotifyCanExecuteChanged();
        SelectAllPageCommand.NotifyCanExecuteChanged();
        ClearSelectionCommand.NotifyCanExecuteChanged();
    }

    private void OnPreviewItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotifyComputedStateChanged();
    }

    private static bool TryLoadBitmap(string? filePath, out Bitmap bitmap)
    {
        bitmap = null!;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            bitmap = new Bitmap(stream);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task ExecuteOnUiAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action);
    }
}
