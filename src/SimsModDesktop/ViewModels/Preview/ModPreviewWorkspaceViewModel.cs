using System.ComponentModel;
using Avalonia.Threading;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.Services;
using SimsModDesktop.ViewModels.Infrastructure;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.ViewModels.Preview;

public sealed class ModPreviewWorkspaceViewModel : ObservableObject
{
    private readonly ModPreviewPanelViewModel _filter;
    private readonly IModPackageScanService _scanService;
    private readonly IModItemCatalogService _catalogService;
    private readonly IModItemIndexScheduler _indexScheduler;
    private readonly object _eventLock = new();

    private CancellationTokenSource? _refreshCts;
    private CancellationTokenSource? _fastEventCts;
    private CancellationTokenSource? _deepEventCts;
    private HashSet<string> _pendingDeepKeys = new(StringComparer.OrdinalIgnoreCase);
    private bool _isBusy;
    private bool _isActive;
    private bool _hasPendingRefresh = true;
    private bool _hasLoadedStablePage;
    private bool _inspectHasNewerSnapshot;
    private string _statusText = "Set a valid Mods path to build the item catalog.";
    private string _summaryText = "No indexed items loaded.";
    private string _pageText = "Page 0/0";
    private string _jumpPageText = string.Empty;
    private string _indexingStatusText = "No package indexing has started yet.";
    private int _currentPage = 1;
    private int _totalPages = 1;
    private ModItemListItemViewModel? _selectedItem;

    public ModPreviewWorkspaceViewModel(
        ModPreviewPanelViewModel filter,
        IModPreviewCatalogService? legacyCatalogService = null,
        IModItemCatalogService? catalogService = null,
        IModItemIndexScheduler? indexScheduler = null,
        IModPackageScanService? scanService = null,
        IModItemInspectService? inspectService = null,
        IModPackageTextureEditService? textureEditService = null,
        IFileDialogService? fileDialogService = null)
    {
        _filter = filter;
        _scanService = scanService ?? new ModPackageScanService();

        var store = new SqliteModItemIndexStore();
        _catalogService = catalogService ?? new SqliteModItemCatalogService(store);
        _indexScheduler = indexScheduler ?? new ModItemIndexScheduler(
            store,
            new FastModItemIndexService(),
            new DeepModItemEnrichmentService(new ModPackageTextureAnalysisService(new SqliteModPackageTextureAnalysisStore())));

        var resolvedInspectService = inspectService ?? new SqliteModItemInspectService(store);
        var resolvedTextureEditService = textureEditService ?? NullModPackageTextureEditService.Instance;
        var resolvedFileDialog = fileDialogService ?? new NullFileDialogService();
        Inspect = new ModItemInspectViewModel(resolvedInspectService, resolvedTextureEditService, resolvedFileDialog);

        CatalogItems = new PatchableObservableCollection<ModItemListItemViewModel>();
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => HasValidModsPath && !IsBusy);
        PrevPageCommand = new AsyncRelayCommand(LoadPreviousPageAsync, () => CanGoPrevPage);
        NextPageCommand = new AsyncRelayCommand(LoadNextPageAsync, () => CanGoNextPage);
        JumpPageCommand = new AsyncRelayCommand(JumpPageAsync, () => CanJumpPage);
        SelectItemCommand = new RelayCommand<ModItemListItemViewModel>(SelectItem);

        _filter.PropertyChanged += OnFilterPropertyChanged;
        _indexScheduler.FastBatchApplied += OnFastBatchApplied;
        _indexScheduler.EnrichmentApplied += OnEnrichmentApplied;
        _indexScheduler.AllWorkCompleted += OnAllWorkCompleted;
    }

    public PatchableObservableCollection<ModItemListItemViewModel> CatalogItems { get; }
    public ModItemInspectViewModel Inspect { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand PrevPageCommand { get; }
    public AsyncRelayCommand NextPageCommand { get; }
    public AsyncRelayCommand JumpPageCommand { get; }
    public RelayCommand<ModItemListItemViewModel> SelectItemCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsEmptyStateVisible));
            OnPropertyChanged(nameof(IsLoadingStateVisible));
            OnPropertyChanged(nameof(CanGoPrevPage));
            OnPropertyChanged(nameof(CanGoNextPage));
            OnPropertyChanged(nameof(CanJumpPage));
            RefreshCommand.NotifyCanExecuteChanged();
            PrevPageCommand.NotifyCanExecuteChanged();
            NextPageCommand.NotifyCanExecuteChanged();
            JumpPageCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasValidModsPath =>
        !string.IsNullOrWhiteSpace(_filter.ModsRoot) && Directory.Exists(_filter.ModsRoot);

    public bool HasItems => CatalogItems.Count > 0;
    public bool IsEmptyStateVisible => !IsBusy && !HasItems;
    public bool IsLoadingStateVisible => IsBusy && !HasItems;
    public bool CanGoPrevPage => !IsBusy && _currentPage > 1;
    public bool CanGoNextPage => !IsBusy && _currentPage < _totalPages;
    public bool CanJumpPage => !IsBusy && int.TryParse(JumpPageText, out var page) && page >= 1 && page <= _totalPages;

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

    public string PageText
    {
        get => _pageText;
        private set => SetProperty(ref _pageText, value);
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

            OnPropertyChanged(nameof(CanJumpPage));
            JumpPageCommand.NotifyCanExecuteChanged();
        }
    }

    public string IndexingStatusText
    {
        get => _indexingStatusText;
        private set => SetProperty(ref _indexingStatusText, value);
    }

    public ModItemListItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (value is not null && (!CatalogItems.Contains(value) || value.IsPlaceholder))
            {
                return;
            }

            var previous = _selectedItem;
            if (!SetProperty(ref _selectedItem, value))
            {
                return;
            }

            if (previous is not null && !ReferenceEquals(previous, value))
            {
                previous.IsSelected = false;
            }

            if (value is not null)
            {
                value.IsSelected = true;
                _inspectHasNewerSnapshot = false;
                _ = Inspect.LoadAsync(value.ItemKey);
            }
        }
    }

    public void SetIsActive(bool isActive)
    {
        if (_isActive == isActive)
        {
            return;
        }

        _isActive = isActive;
        if (!_isActive)
        {
            _refreshCts?.Cancel();
            return;
        }

        if (_hasPendingRefresh || (HasValidModsPath && CatalogItems.Count == 0))
        {
            _ = RefreshAsync();
        }
    }

    public Task RefreshAsync()
    {
        return RefreshAsync(forcePageReset: true);
    }

    private async Task RefreshAsync(bool forcePageReset)
    {
        _hasPendingRefresh = false;
        CancelEventThrottles();
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        var cancellationToken = _refreshCts.Token;

        if (!HasValidModsPath)
        {
            await ExecuteOnUiAsync(() =>
            {
                CatalogItems.ReplaceAllStable(Array.Empty<ModItemListItemViewModel>());
                SelectedItem = null;
                SummaryText = "No indexed items loaded.";
                StatusText = "Set a valid Mods path to build the item catalog.";
                IndexingStatusText = "No package indexing has started yet.";
                PageText = "Page 0/0";
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(IsEmptyStateVisible));
                OnPropertyChanged(nameof(IsLoadingStateVisible));
            }).ConfigureAwait(false);
            return;
        }

        if (forcePageReset)
        {
            _currentPage = 1;
        }

        await ExecuteOnUiAsync(() =>
        {
            IsBusy = true;
            StatusText = "Loading cached item catalog rows...";
        }).ConfigureAwait(false);

        try
        {
            var packageScan = await _scanService.ScanAsync(_filter.ModsRoot, cancellationToken).ConfigureAwait(false);
            await LoadPageShellAsync(cancellationToken, allowSkeletonWhenEmpty: packageScan.Count > 0).ConfigureAwait(false);

            if (packageScan.Count == 0)
            {
                await ExecuteOnUiAsync(() =>
                {
                    IndexingStatusText = "No packages were found under the configured Mods path.";
                    Inspect.SetBackgroundSyncActive(false);
                }).ConfigureAwait(false);
                return;
            }

            await ExecuteOnUiAsync(() =>
            {
                IndexingStatusText = "Fast parsing package metadata in the background...";
                Inspect.SetBackgroundSyncActive(true);
            }).ConfigureAwait(false);

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await _indexScheduler.QueueRefreshAsync(packageScan.Select(result => result.PackagePath).ToArray(), cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await ExecuteOnUiAsync(() =>
            {
                CatalogItems.ReplaceAllStable(Array.Empty<ModItemListItemViewModel>());
                SelectedItem = null;
                SummaryText = "Item catalog load failed.";
                StatusText = ex.Message;
                IndexingStatusText = "Background indexing did not start.";
                PageText = "Page 0/0";
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(IsEmptyStateVisible));
                OnPropertyChanged(nameof(IsLoadingStateVisible));
            }).ConfigureAwait(false);
        }
        finally
        {
            await ExecuteOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    private async Task LoadPageShellAsync(CancellationToken cancellationToken, bool allowSkeletonWhenEmpty)
    {
        var page = await QueryCurrentPageAsync(cancellationToken).ConfigureAwait(false);
        var rows = page.Items.Select(item => new ModItemListItemViewModel(item)).ToArray();

        await ExecuteOnUiAsync(() =>
        {
            _currentPage = page.PageIndex;
            _totalPages = page.TotalPages;
            PageText = $"Page {page.PageIndex}/{page.TotalPages}";

            if (rows.Length > 0)
            {
                ApplyStableRows(rows, page.TotalItems, page.PageSize, "Item catalog loaded from the local database.");
                _hasLoadedStablePage = true;
                IndexingStatusText = "Deep enrichment will continue in the background if packages changed.";
                return;
            }

            if (allowSkeletonWhenEmpty)
            {
                CatalogItems.ReplaceAllStable(BuildSkeletonRows(page.PageSize).Select(row => new ModItemListItemViewModel(row)));
                SelectedItem = null;
                _hasLoadedStablePage = false;
                SummaryText = $"Items: {page.TotalItems} | Page Size: {page.PageSize}";
                StatusText = "Loading basic catalog rows...";
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(IsEmptyStateVisible));
                OnPropertyChanged(nameof(IsLoadingStateVisible));
                return;
            }

            CatalogItems.ReplaceAllStable(Array.Empty<ModItemListItemViewModel>());
            SelectedItem = null;
            _hasLoadedStablePage = false;
            SummaryText = "No indexed items loaded.";
            StatusText = "No indexed items matched the current filters.";
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(IsEmptyStateVisible));
            OnPropertyChanged(nameof(IsLoadingStateVisible));
        }).ConfigureAwait(false);
    }

    private async Task ApplyFastRowsToCurrentPageAsync()
    {
        var page = await QueryCurrentPageAsync(CancellationToken.None).ConfigureAwait(false);
        var rows = page.Items.Select(item => new ModItemListItemViewModel(item)).ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        await ExecuteOnUiAsync(() =>
        {
            _currentPage = page.PageIndex;
            _totalPages = page.TotalPages;
            PageText = $"Page {page.PageIndex}/{page.TotalPages}";
            ApplyStableRows(rows, page.TotalItems, page.PageSize, "Basic metadata loaded. Deep enrichment continues in the background.");
            _hasLoadedStablePage = true;
            IndexingStatusText = "Deep enriching names, textures, and previews in the background...";
        }).ConfigureAwait(false);
    }

    private async Task PatchCurrentPageRowsAsync()
    {
        var page = await QueryCurrentPageAsync(CancellationToken.None).ConfigureAwait(false);
        if (page.Items.Count == 0)
        {
            return;
        }

        await ExecuteOnUiAsync(() =>
        {
            if (!_hasLoadedStablePage || CatalogItems.Any(item => item.IsPlaceholder))
            {
                var replacementRows = page.Items.Select(item => new ModItemListItemViewModel(item)).ToArray();
                ApplyStableRows(replacementRows, page.TotalItems, page.PageSize, "Basic metadata loaded. Deep enrichment continues in the background.");
                _hasLoadedStablePage = true;
                return;
            }

            CatalogItems.PatchByKey(
                page.Items,
                existing => existing.ItemKey,
                update => update.ItemKey,
                (existing, update) => existing.UpdateFrom(update));
            SummaryText = $"Items: {page.TotalItems} | Page Size: {page.PageSize}";
            OnPropertyChanged(nameof(HasItems));
        }).ConfigureAwait(false);
    }

    private Task<ModItemCatalogPage> QueryCurrentPageAsync(CancellationToken cancellationToken)
    {
        return _catalogService.QueryPageAsync(
            new ModItemCatalogQuery
            {
                ModsRoot = _filter.ModsRoot,
                SearchQuery = _filter.SearchQuery,
                EntityKindFilter = _filter.PackageTypeFilter,
                SubTypeFilter = _filter.ScopeFilter,
                SortBy = _filter.SortBy,
                PageIndex = _currentPage,
                PageSize = 50
            },
            cancellationToken);
    }

    private void ApplyStableRows(
        IReadOnlyList<ModItemListItemViewModel> rows,
        int totalItems,
        int pageSize,
        string statusText)
    {
        var selectedKey = SelectedItem?.ItemKey;
        CatalogItems.ReplaceAllStable(rows);
        SummaryText = $"Items: {totalItems} | Page Size: {pageSize}";
        StatusText = statusText;
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(IsLoadingStateVisible));

        var resolvedSelection = rows.FirstOrDefault(item =>
            string.Equals(item.ItemKey, selectedKey, StringComparison.OrdinalIgnoreCase))
            ?? rows.FirstOrDefault(item => !item.IsPlaceholder);

        if (_selectedItem is not null &&
            resolvedSelection is not null &&
            string.Equals(_selectedItem.ItemKey, resolvedSelection.ItemKey, StringComparison.OrdinalIgnoreCase))
        {
            _selectedItem = resolvedSelection;
            resolvedSelection.IsSelected = true;
            OnPropertyChanged(nameof(SelectedItem));
            return;
        }

        SelectedItem = resolvedSelection;
    }

    private static IReadOnlyList<ModItemListRow> BuildSkeletonRows(int count)
    {
        var safeCount = Math.Max(1, count);
        var rows = new List<ModItemListRow>(safeCount);
        for (var index = 0; index < safeCount; index++)
        {
            rows.Add(new ModItemListRow
            {
                ItemKey = $"__skeleton__:{index}",
                DisplayName = "Loading item metadata...",
                EntityKind = "Pending",
                EntitySubType = "Pending",
                PackagePath = string.Empty,
                PackageName = string.Empty,
                ScopeText = "Pending",
                ThumbnailStatus = "Placeholder",
                TextureCount = 0,
                EditableTextureCount = 0,
                TextureSummaryText = "Basic metadata loaded",
                IsPlaceholder = true,
                DisplayStage = ModItemDisplayStage.Fast,
                ThumbnailStage = ModItemThumbnailStage.None,
                TextureStage = ModItemTextureStage.Pending,
                ShowThumbnailPlaceholder = true,
                ShowMetadataPlaceholder = true,
                StableSubtitleText = "Basic metadata loaded",
                SortKeyStable = $"__skeleton__:{index}",
                UpdatedUtcTicks = 0
            });
        }

        return rows;
    }

    private async Task LoadPreviousPageAsync()
    {
        if (!CanGoPrevPage)
        {
            return;
        }

        _currentPage--;
        await RefreshAsync(forcePageReset: false).ConfigureAwait(false);
    }

    private async Task LoadNextPageAsync()
    {
        if (!CanGoNextPage)
        {
            return;
        }

        _currentPage++;
        await RefreshAsync(forcePageReset: false).ConfigureAwait(false);
    }

    private async Task JumpPageAsync()
    {
        if (!int.TryParse(JumpPageText, out var page) || page < 1 || page > _totalPages)
        {
            return;
        }

        _currentPage = page;
        await RefreshAsync(forcePageReset: false).ConfigureAwait(false);
    }

    private void SelectItem(ModItemListItemViewModel? item)
    {
        if (item is null || item.IsPlaceholder || !CatalogItems.Contains(item))
        {
            return;
        }

        SelectedItem = item;
    }

    private void OnFilterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasValidModsPath));
        _hasPendingRefresh = true;
        if (_isActive)
        {
            _ = RefreshAsync();
        }
    }

    private void OnFastBatchApplied(object? sender, ModFastBatchAppliedEventArgs e)
    {
        if (!_isActive || !HasValidModsPath)
        {
            return;
        }

        ScheduleFastUpdate();
    }

    private void OnEnrichmentApplied(object? sender, ModEnrichmentAppliedEventArgs e)
    {
        if (!_isActive || !HasValidModsPath)
        {
            return;
        }

        lock (_eventLock)
        {
            foreach (var itemKey in e.AffectedItemKeys)
            {
                _pendingDeepKeys.Add(itemKey);
            }
        }

        if (_selectedItem is not null &&
            e.AffectedItemKeys.Any(itemKey => string.Equals(itemKey, _selectedItem.ItemKey, StringComparison.OrdinalIgnoreCase)))
        {
            _inspectHasNewerSnapshot = true;
            _ = ExecuteOnUiAsync(() => Inspect.MarkPendingRefresh());
        }

        ScheduleDeepUpdate();
    }

    private void OnAllWorkCompleted(object? sender, EventArgs e)
    {
        if (!_isActive)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await ExecuteOnUiAsync(() =>
            {
                IndexingStatusText = "Indexed item catalog is up to date.";
                Inspect.SetBackgroundSyncActive(false);
            }).ConfigureAwait(false);

            if (_inspectHasNewerSnapshot && SelectedItem is not null)
            {
                _inspectHasNewerSnapshot = false;
                await Inspect.RefreshCurrentAsync().ConfigureAwait(false);
            }
        });
    }

    private void ScheduleFastUpdate()
    {
        _fastEventCts?.Cancel();
        _fastEventCts?.Dispose();
        _fastEventCts = new CancellationTokenSource();
        var token = _fastEventCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), token).ConfigureAwait(false);
                await ApplyFastRowsToCurrentPageAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void ScheduleDeepUpdate()
    {
        _deepEventCts?.Cancel();
        _deepEventCts?.Dispose();
        _deepEventCts = new CancellationTokenSource();
        var token = _deepEventCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400), token).ConfigureAwait(false);
                lock (_eventLock)
                {
                    _pendingDeepKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                await PatchCurrentPageRowsAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void CancelEventThrottles()
    {
        _fastEventCts?.Cancel();
        _fastEventCts?.Dispose();
        _fastEventCts = null;
        _deepEventCts?.Cancel();
        _deepEventCts?.Dispose();
        _deepEventCts = null;
        lock (_eventLock)
        {
            _pendingDeepKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Task ExecuteOnUiAsync(Action action)
    {
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    private sealed class NullFileDialogService : IFileDialogService
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
}
