using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using SimsModDesktop.Models;
using SimsModDesktop.Services;
using SimsModDesktop.ViewModels.Infrastructure;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.ViewModels.Preview;

public sealed class ModPreviewWorkspaceViewModel : ObservableObject
{
    private readonly ModPreviewPanelViewModel _filter;
    private readonly IModPreviewCatalogService _catalogService;

    private CancellationTokenSource? _refreshDebounceCts;
    private CancellationTokenSource? _refreshWorkCts;
    private bool _isBusy;
    private bool _isActive;
    private bool _hasPendingRefresh = true;
    private string _statusText = "Set a valid Mods path to scan packages.";
    private string _summaryText = "No preview data loaded.";
    private ModPreviewListItemViewModel? _selectedItem;
    private ModPreviewDetailModel? _selectedDetail;

    public ModPreviewWorkspaceViewModel(
        ModPreviewPanelViewModel filter,
        IModPreviewCatalogService catalogService)
    {
        _filter = filter;
        _catalogService = catalogService;
        CatalogItems = new BulkObservableCollection<ModPreviewListItemViewModel>();

        RefreshCommand = new AsyncRelayCommand(() => RefreshAsync(bypassCache: true), () => !IsBusy);
        SelectItemCommand = new RelayCommand<ModPreviewListItemViewModel>(SelectItem);
        OpenSelectedFolderCommand = new RelayCommand(OpenSelectedFolder, () => SelectedItem is not null);
        OpenSelectedFileCommand = new RelayCommand(OpenSelectedFile, () => SelectedItem is not null);

        _filter.PropertyChanged += OnFilterPropertyChanged;
    }

    public BulkObservableCollection<ModPreviewListItemViewModel> CatalogItems { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand<ModPreviewListItemViewModel> SelectItemCommand { get; }
    public RelayCommand OpenSelectedFolderCommand { get; }
    public RelayCommand OpenSelectedFileCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsLoadingStateVisible));
            OnPropertyChanged(nameof(IsEmptyStateVisible));
            RefreshCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasValidModsPath =>
        !string.IsNullOrWhiteSpace(_filter.ModsRoot) && Directory.Exists(_filter.ModsRoot);

    public bool HasItems => CatalogItems.Count > 0;
    public bool IsLoadingStateVisible => IsBusy && !HasItems;
    public bool IsEmptyStateVisible => !IsBusy && !HasItems;
    public bool HasSelectedItem => SelectedItem is not null;
    public bool IsDetailPlaceholderVisible => !HasSelectedItem;

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

    public ModPreviewListItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (value is not null && !CatalogItems.Contains(value))
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

            if (value is not null && !value.IsSelected)
            {
                value.IsSelected = true;
            }

            SelectedDetail = value is null ? null : BuildDetail(value.Item);
            OnPropertyChanged(nameof(HasSelectedItem));
            OnPropertyChanged(nameof(IsDetailPlaceholderVisible));
            OpenSelectedFolderCommand.NotifyCanExecuteChanged();
            OpenSelectedFileCommand.NotifyCanExecuteChanged();
        }
    }

    public ModPreviewDetailModel? SelectedDetail
    {
        get => _selectedDetail;
        private set => SetProperty(ref _selectedDetail, value);
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
            CancelScheduledRefresh();
            return;
        }

        if (_hasPendingRefresh || (HasValidModsPath && CatalogItems.Count == 0))
        {
            ScheduleRefresh(immediate: true);
        }
    }

    public Task RefreshAsync()
    {
        return RefreshAsync(bypassCache: true);
    }

    private async Task RefreshAsync(bool bypassCache)
    {
        _hasPendingRefresh = false;

        if (!HasValidModsPath)
        {
            await ExecuteOnUiAsync(() =>
            {
                CatalogItems.ReplaceAll(Array.Empty<ModPreviewListItemViewModel>());
                SelectedItem = null;
                SummaryText = "No preview data loaded.";
                StatusText = "Set a valid Mods path to scan packages.";
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(IsLoadingStateVisible));
                OnPropertyChanged(nameof(IsEmptyStateVisible));
                OnPropertyChanged(nameof(HasValidModsPath));
            }).ConfigureAwait(false);
            return;
        }

        var previousSelectionKey = SelectedItem?.Item.Key;
        await ExecuteOnUiAsync(() => IsBusy = true).ConfigureAwait(false);
        var cancellationToken = BeginRefreshWork();

        try
        {
            var query = new ModPreviewCatalogQuery
            {
                ModsRoot = _filter.ModsRoot,
                PackageTypeFilter = _filter.PackageTypeFilter,
                ScopeFilter = _filter.ScopeFilter,
                SortBy = _filter.SortBy,
                SearchQuery = _filter.SearchQuery,
                ShowOverridesOnly = _filter.ShowOverridesOnly,
                BypassCache = bypassCache
            };

            await ExecuteOnUiAsync(() =>
            {
                CatalogItems.ReplaceAll(Array.Empty<ModPreviewListItemViewModel>());
                SelectedItem = null;
                SummaryText = "Scanning Mods folder...";
                StatusText = "Loading initial results...";
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(IsLoadingStateVisible));
                OnPropertyChanged(nameof(IsEmptyStateVisible));
            }).ConfigureAwait(false);

            await foreach (var page in _catalogService.StreamCatalogAsync(query, cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var viewModels = page.Items
                    .Select(item => new ModPreviewListItemViewModel(item))
                    .ToArray();

                await ExecuteOnUiAsync(() =>
                {
                    if (page.ReplaceExisting)
                    {
                        CatalogItems.ReplaceAll(viewModels);
                    }
                    else
                    {
                        CatalogItems.AddRange(viewModels);
                    }

                    ApplySelection(previousSelectionKey);

                    if (page.ReplaceExisting)
                    {
                        SummaryText = CatalogItems.Count == 0
                            ? "No matching mods."
                            : $"Mods: {page.MatchedCount} | Packages: {page.PackageCount} | Scripts: {page.ScriptCount}";
                        StatusText = CatalogItems.Count == 0
                            ? "No mods matched the current filters."
                            : "Catalog refreshed from the current Mods path.";
                    }
                    else
                    {
                        SummaryText = $"Loading mods: {page.MatchedCount} matched";
                        StatusText = $"Scanned {page.ScannedCount} files | Packages {page.PackageCount} | Scripts {page.ScriptCount}";
                    }

                    OnPropertyChanged(nameof(HasItems));
                    OnPropertyChanged(nameof(IsLoadingStateVisible));
                    OnPropertyChanged(nameof(IsEmptyStateVisible));
                }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await ExecuteOnUiAsync(() =>
            {
                CatalogItems.ReplaceAll(Array.Empty<ModPreviewListItemViewModel>());
                SelectedItem = null;
                SummaryText = "Preview load failed.";
                StatusText = ex.Message;
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(IsLoadingStateVisible));
                OnPropertyChanged(nameof(IsEmptyStateVisible));
            }).ConfigureAwait(false);
        }
        finally
        {
            if (IsCurrentRefresh(cancellationToken))
            {
                CompleteRefreshWork(cancellationToken);
                await ExecuteOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
            }
        }
    }

    private void OnFilterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasValidModsPath));

        _hasPendingRefresh = true;
        if (_isActive)
        {
            ScheduleRefresh(immediate: false);
        }
    }

    private void ScheduleRefresh(bool immediate)
    {
        CancelScheduledRefresh();
        if (!_isActive)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _refreshDebounceCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                if (!immediate)
                {
                    await Task.Delay(180, cts.Token).ConfigureAwait(false);
                }

                if (!cts.IsCancellationRequested)
                {
                    await RefreshAsync(bypassCache: false).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, cts.Token);
    }

    private void CancelScheduledRefresh()
    {
        _refreshDebounceCts?.Cancel();
        _refreshDebounceCts?.Dispose();
        _refreshDebounceCts = null;
    }

    private void SelectItem(ModPreviewListItemViewModel? item)
    {
        if (item is null || !CatalogItems.Contains(item))
        {
            return;
        }

        SelectedItem = item;
    }

    private CancellationToken BeginRefreshWork()
    {
        _refreshWorkCts?.Cancel();
        _refreshWorkCts?.Dispose();
        _refreshWorkCts = new CancellationTokenSource();
        return _refreshWorkCts.Token;
    }

    private void CompleteRefreshWork(CancellationToken token)
    {
        if (_refreshWorkCts is null || _refreshWorkCts.Token != token)
        {
            return;
        }

        _refreshWorkCts.Dispose();
        _refreshWorkCts = null;
    }

    private bool IsCurrentRefresh(CancellationToken token)
    {
        return _refreshWorkCts is not null && _refreshWorkCts.Token == token;
    }

    private void ApplySelection(string? preferredKey)
    {
        var selected = CatalogItems.FirstOrDefault(item =>
                           string.Equals(item.Item.Key, preferredKey, StringComparison.OrdinalIgnoreCase))
                       ?? CatalogItems.FirstOrDefault();
        SelectedItem = selected;
    }

    private static ModPreviewDetailModel BuildDetail(ModPreviewCatalogItem item)
    {
        var overviewRows = new[]
        {
            new ModPreviewDetailRow { Label = "Classification", Value = item.Classification },
            new ModPreviewDetailRow { Label = "File Size", Value = item.FileSizeText },
            new ModPreviewDetailRow { Label = "Last Updated", Value = item.LastUpdatedText },
            new ModPreviewDetailRow { Label = "Extension", Value = item.FileExtension },
            new ModPreviewDetailRow { Label = "Type", Value = item.PackageType }
        };

        var resourceRows = new[]
        {
            new ModPreviewDetailRow
            {
                Label = "Preview",
                Value = string.Equals(item.PackageType, "Script Mod", StringComparison.OrdinalIgnoreCase)
                    ? "No embedded preview. Script packages usually surface metadata only."
                    : "Thumbnail slot reserved. Package parser can bind swatches or texture previews here."
            },
            new ModPreviewDetailRow { Label = "Scope", Value = item.Scope },
            new ModPreviewDetailRow { Label = "Path Group", Value = item.DisplaySubtitle }
        };

        var conflictRows = new[]
        {
            new ModPreviewDetailRow { Label = "Status", Value = item.ConflictHintText },
            new ModPreviewDetailRow
            {
                Label = "Override",
                Value = item.IsOverride ? "This file is likely replacing existing resources." : "No obvious override signal detected."
            }
        };

        var fileRows = new[]
        {
            new ModPreviewDetailRow { Label = "Relative", Value = item.RelativePath },
            new ModPreviewDetailRow { Label = "Absolute", Value = item.FullPath }
        };

        return new ModPreviewDetailModel
        {
            DisplayTitle = item.DisplayTitle,
            DisplaySubtitle = item.DisplaySubtitle,
            PackageType = item.PackageType,
            Scope = item.Scope,
            RelativePath = item.RelativePath,
            FullPath = item.FullPath,
            PreviewStatusText = "Parser-ready surface. Real package metadata can replace these placeholders later.",
            OverviewRows = overviewRows,
            ResourceRows = resourceRows,
            ConflictRows = conflictRows,
            FileRows = fileRows
        };
    }

    private void OpenSelectedFolder()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(SelectedItem.Item.FullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        OpenExplorer(directory, selectFile: false);
    }

    private void OpenSelectedFile()
    {
        if (SelectedItem is null || !File.Exists(SelectedItem.Item.FullPath))
        {
            return;
        }

        OpenExplorer(SelectedItem.Item.FullPath, selectFile: true);
    }

    private static void OpenExplorer(string path, bool selectFile)
    {
        var arguments = selectFile ? $"/select,\"{path}\"" : $"\"{path}\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    private static Task ExecuteOnUiAsync(Action action)
    {
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}
