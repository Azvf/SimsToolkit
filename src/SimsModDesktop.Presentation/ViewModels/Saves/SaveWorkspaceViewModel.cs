using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Preview;
using SimsModDesktop.PackageCore;
using Avalonia.Threading;
using SimsModDesktop.Presentation.Dialogs;
using SimsModDesktop.Presentation.Services;
using SimsModDesktop.Presentation.Save;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.Presentation.ViewModels.Infrastructure;
using SimsModDesktop.Presentation.ViewModels.Preview;
using SimsModDesktop.Application.Warmup;

namespace SimsModDesktop.Presentation.ViewModels.Saves;

public sealed class SaveWorkspaceViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;
    private readonly IPathIdentityResolver _pathIdentityResolver;
    private readonly SavePreviewLifecycleController _lifecycleController;
    private readonly SaveDependencyAnalysisController _dependencyAnalysisController;
    private readonly SaveExportController _exportController;

    private CancellationTokenSource? _selectionLoadCts;
    private CancellationTokenSource? _cacheBuildCts;
    private CancellationTokenSource? _artifactPrimeCts;
    private string _pendingSelectedSavePath = string.Empty;
    private string _pendingSelectedPreviewHouseholdKey = string.Empty;
    private string _savesPath = string.Empty;
    private string _exportRootPath = string.Empty;
    private bool _generateThumbnails = true;
    private bool _isActive;
    private bool _hasPendingRefresh;
    private bool _isBusy;
    private bool _isBuildingCache;
    private string _statusText = "Set a valid Saves path to scan save slots.";
    private string _cacheStatusText = "No save selected.";
    private string _exportSummaryText = "Save preview ready.";
    private IReadOnlyList<SaveFileEntry> _saveFiles = Array.Empty<SaveFileEntry>();
    private SaveFileEntry? _selectedSave;
    private SaveHouseholdSnapshot? _currentSnapshot;
    private SavePreviewDescriptorManifest? _currentManifest;
    private SaveHouseholdItem? _selectedPreviewHousehold;

    public SaveWorkspaceViewModel(
        ISaveHouseholdCoordinator coordinator,
        IFileDialogService fileDialogService,
        ITrayDependenciesLauncher trayDependenciesLauncher,
        IPreviewQueryService previewQueryService,
        ITrayThumbnailService trayThumbnailService,
        ISaveWarmupService? saveWarmupService = null,
        IPathIdentityResolver? pathIdentityResolver = null,
        IUiActivityMonitor? uiActivityMonitor = null,
        IConfigurationProvider? configurationProvider = null)
    {
        _fileDialogService = fileDialogService;
        _pathIdentityResolver = pathIdentityResolver ?? new SystemPathIdentityResolver();
        _lifecycleController = new SavePreviewLifecycleController(
            coordinator,
            uiActivityMonitor ?? new UiActivityMonitor(),
            configurationProvider,
            saveWarmupService);
        _dependencyAnalysisController = new SaveDependencyAnalysisController(
            coordinator,
            trayDependenciesLauncher,
            saveWarmupService);
        _exportController = new SaveExportController(coordinator);

        Filter = new SavePreviewFilterViewModel();
        Surface = new TrayLikePreviewSurfaceViewModel(previewQueryService, trayThumbnailService);
        Surface.Configure(Filter, BuildCurrentPreviewInput, PreviewSurfaceSelectionMode.Single);
        Surface.SetFooter("Save Preview", ExportSummaryText);
        Surface.PropertyChanged += OnSurfacePropertyChanged;
        Surface.PreviewItems.CollectionChanged += (_, _) => TryRestoreSelectedPreviewItem();

        RefreshSavesCommand = new AsyncRelayCommand(RefreshSavesAsync, () => !IsBusy);
        BrowseExportRootCommand = new AsyncRelayCommand(BrowseExportRootAsync, () => !IsBusy);
        RebuildCacheCommand = new AsyncRelayCommand(RebuildCacheAsync, () => !IsBusy && SelectedSave is not null);
        ClearSelectedSaveCacheCommand = new AsyncRelayCommand(ClearSelectedSaveCacheAsync, () => !IsBusy && SelectedSave is not null);
        AnalyzeDependenciesCommand = new AsyncRelayCommand(AnalyzeDependenciesAsync, CanAnalyzeDependencies);
        ExportCommand = new AsyncRelayCommand(ExportAsync, CanExport);

        Surface.SetActionButtons(
        [
            new PreviewSurfaceActionButtonViewModel { Label = "Analyze Dependencies", Command = AnalyzeDependenciesCommand },
            new PreviewSurfaceActionButtonViewModel { Label = "Export Selected Household", Command = ExportCommand, IsPrimary = true },
            new PreviewSurfaceActionButtonViewModel { Label = "Clear Selection", Command = Surface.ClearSelectionCommand }
        ]);
    }

    public SavePreviewFilterViewModel Filter { get; }
    public TrayLikePreviewSurfaceViewModel Surface { get; }

    public AsyncRelayCommand RefreshSavesCommand { get; }
    public AsyncRelayCommand BrowseExportRootCommand { get; }
    public AsyncRelayCommand RebuildCacheCommand { get; }
    public AsyncRelayCommand ClearSelectedSaveCacheCommand { get; }
    public AsyncRelayCommand AnalyzeDependenciesCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }

    public string SavesPath
    {
        get => _savesPath;
        set
        {
            var normalized = NormalizePath(value);
            if (!SetProperty(ref _savesPath, normalized))
            {
                return;
            }

            if (!HasValidSavesPath)
            {
                _hasPendingRefresh = false;
                CancelSelectionLoad();
                CancelCacheBuild();
                CancelArtifactPrime();
                SaveFiles = Array.Empty<SaveFileEntry>();
                SelectedSave = null;
                _currentSnapshot = null;
                _currentManifest = null;
                _selectedPreviewHousehold = null;
                CacheStatusText = "Saves path is missing or does not exist.";
                StatusText = "Set a valid Saves path to scan save slots.";
                Surface.NotifyPreviewSourceChanged();
            }
            else
            {
                _hasPendingRefresh = true;
                StatusText = _isActive
                    ? "Loading save catalog..."
                    : "Open the Saves section to load save previews.";

                if (_isActive)
                {
                    _ = RefreshSavesAsync();
                }
            }

            NotifyStateChanged();
        }
    }

    public string ExportRootPath
    {
        get => _exportRootPath;
        set
        {
            var normalized = NormalizePath(value);
            if (!SetProperty(ref _exportRootPath, normalized))
            {
                return;
            }

            NotifyStateChanged();
        }
    }

    public bool GenerateThumbnails
    {
        get => _generateThumbnails;
        set
        {
            if (!SetProperty(ref _generateThumbnails, value))
            {
                return;
            }

            NotifyStateChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            NotifyStateChanged();
        }
    }

    public bool IsBuildingCache
    {
        get => _isBuildingCache;
        private set
        {
            if (!SetProperty(ref _isBuildingCache, value))
            {
                return;
            }

            NotifyStateChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string CacheStatusText
    {
        get => _cacheStatusText;
        private set => SetProperty(ref _cacheStatusText, value);
    }

    public string ExportSummaryText
    {
        get => _exportSummaryText;
        private set
        {
            if (!SetProperty(ref _exportSummaryText, value))
            {
                return;
            }

            Surface.SetFooter("Save Preview", value);
        }
    }

    public IReadOnlyList<SaveFileEntry> SaveFiles
    {
        get => _saveFiles;
        private set => SetProperty(ref _saveFiles, value);
    }

    public SaveFileEntry? SelectedSave
    {
        get => _selectedSave;
        set
        {
            if (!SetProperty(ref _selectedSave, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedSaveSummary));
            NotifyStateChanged();
            if (_isActive)
            {
                _ = HandleSelectedSaveChangedAsync();
            }
        }
    }

    public string SelectedSaveSummary
    {
        get
        {
            if (SelectedSave is null)
            {
                return "No save selected.";
            }

            if (_currentManifest is null)
            {
                return SelectedSave.DisplayLabel;
            }

            return $"{SelectedSave.FileName} | Ready {_currentManifest.ReadyHouseholdCount}/{_currentManifest.TotalHouseholdCount} | Blocked {_currentManifest.BlockedHouseholdCount}";
        }
    }

    public bool HasValidSavesPath =>
        !string.IsNullOrWhiteSpace(SavesPath) && Directory.Exists(SavesPath);

    public bool HasExportRootPath =>
        !string.IsNullOrWhiteSpace(ExportRootPath) && Directory.Exists(ExportRootPath);

    public void LoadFromSettings(AppSettings.SavesSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ExportRootPath = settings.LastExportRoot;
        _pendingSelectedSavePath = NormalizePath(settings.SelectedSavePath);
        _pendingSelectedPreviewHouseholdKey = settings.SelectedPreviewHouseholdKey?.Trim() ?? string.Empty;
        Filter.SearchQuery = settings.SearchQuery;
        Filter.HouseholdSizeFilter = settings.HouseholdSizeFilter;
        Filter.LayoutMode = settings.LayoutMode;
        GenerateThumbnails = settings.GenerateThumbnails;
    }

    public AppSettings.SavesSettings ToSettings()
    {
        return new AppSettings.SavesSettings
        {
            LastExportRoot = ExportRootPath,
            SelectedSavePath = SelectedSave?.FilePath ?? _pendingSelectedSavePath,
            SelectedPreviewHouseholdKey = Surface.SelectedPreviewItem?.TrayItemKey ?? _pendingSelectedPreviewHouseholdKey,
            SearchQuery = Filter.SearchQuery,
            HouseholdSizeFilter = Filter.HouseholdSizeFilter,
            LayoutMode = Filter.LayoutMode,
            GenerateThumbnails = GenerateThumbnails
        };
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
            CancelSelectionLoad();
            CancelCacheBuild();
            CancelArtifactPrime();
            return;
        }

        if (HasValidSavesPath && (_hasPendingRefresh || SaveFiles.Count == 0))
        {
            _ = RefreshSavesAsync();
            return;
        }

        if (SelectedSave is not null)
        {
            _ = HandleSelectedSaveChangedAsync();
        }
    }

    public void ResetAfterCacheClear()
    {
        CancelSelectionLoad();
        CancelCacheBuild();
        CancelArtifactPrime();
        _currentSnapshot = null;
        _currentManifest = null;
        _selectedPreviewHousehold = null;
        Surface.ResetAfterCacheClear();
    }

    private async Task RefreshSavesAsync()
    {
        if (!HasValidSavesPath)
        {
            _hasPendingRefresh = false;
            CancelArtifactPrime();
            SaveFiles = Array.Empty<SaveFileEntry>();
            SelectedSave = null;
            StatusText = "Saves path is missing or does not exist.";
            return;
        }

        IsBusy = true;
        try
        {
            var saveFiles = await _lifecycleController.GetSaveFilesAsync(SavesPath);
            _hasPendingRefresh = false;
            SaveFiles = saveFiles;

            var selected = saveFiles.FirstOrDefault(item =>
                string.Equals(NormalizePath(item.FilePath), _pendingSelectedSavePath, StringComparison.OrdinalIgnoreCase))
                ?? saveFiles.FirstOrDefault();

            SelectedSave = selected;
            StatusText = saveFiles.Count == 0
                ? "No primary .save files were found."
                : $"Found {saveFiles.Count} save file(s).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BrowseExportRootAsync()
    {
        var selectedPaths = await _fileDialogService.PickFolderPathsAsync("Select Export Root", allowMultiple: false);
        var selectedPath = selectedPaths.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        ExportRootPath = selectedPath;
    }

    private async Task RebuildCacheAsync()
    {
        if (SelectedSave is null)
        {
            return;
        }

        await BuildPreviewDescriptorAsync(SelectedSave, forceRefreshPreview: true);
    }

    private async Task ClearSelectedSaveCacheAsync()
    {
        if (SelectedSave is null)
        {
            return;
        }

        var selectedSavePath = NormalizePath(SelectedSave.FilePath);
        CancelSelectionLoad();
        CancelCacheBuild();
        CancelArtifactPrime();

        IsBusy = true;
        try
        {
            await _lifecycleController.ClearPreviewDataAsync(selectedSavePath);

            if (SelectedSave is null ||
                !string.Equals(NormalizePath(SelectedSave.FilePath), selectedSavePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentManifest = null;
            _selectedPreviewHousehold = null;
            _currentSnapshot = null;
            _pendingSelectedPreviewHouseholdKey = string.Empty;
            CacheStatusText = "Preview cache cleared. Rebuild descriptor to reload.";
            StatusText = "Selected save preview cache was cleared.";
            OnPropertyChanged(nameof(SelectedSaveSummary));
            Surface.ClearCurrentSource(
                "Preview cache cleared. Rebuild descriptor to reload.",
                PreviewSourceRef.ForSaveDescriptor(selectedSavePath));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExport()
    {
        return !IsBusy &&
               SelectedSave is not null &&
               Surface.SelectedPreviewItem is not null &&
               _selectedPreviewHousehold is not null &&
               HasExportRootPath;
    }

    private bool CanAnalyzeDependencies()
    {
        return !IsBusy &&
               SelectedSave is not null &&
               Surface.SelectedPreviewItem is not null;
    }

    private async Task HandleSelectedSaveChangedAsync()
    {
        if (!_isActive)
        {
            return;
        }

        if (SelectedSave is null)
        {
            CancelSelectionLoad();
            CancelCacheBuild();
            CancelArtifactPrime();
            _currentSnapshot = null;
            _currentManifest = null;
            _selectedPreviewHousehold = null;
            CacheStatusText = "No save selected.";
            Surface.NotifyPreviewSourceChanged();
            return;
        }

        _pendingSelectedSavePath = NormalizePath(SelectedSave.FilePath);
        CancelArtifactPrime();
        var selectedSave = SelectedSave;
        if (_lifecycleController.TryGetPreviewDescriptor(selectedSave.FilePath, out var manifest) &&
            _lifecycleController.IsPreviewDescriptorCurrent(selectedSave.FilePath, manifest))
        {
            _currentManifest = manifest;
            _currentSnapshot = null;
            _selectedPreviewHousehold = null;
            CacheStatusText = $"Descriptor ready: Ready {manifest.ReadyHouseholdCount}, Blocked {manifest.BlockedHouseholdCount}";
            StatusText = $"Loaded preview descriptor for {selectedSave.FileName}.";
            OnPropertyChanged(nameof(SelectedSaveSummary));
            Surface.NotifyPreviewSourceChanged(invalidateCaches: true);
            _ = EnsureCurrentSnapshotAsync();
            return;
        }

        _currentManifest = null;
        _currentSnapshot = null;
        _selectedPreviewHousehold = null;
        CacheStatusText = "No preview descriptor yet. Building...";
        StatusText = $"Building preview descriptor for {selectedSave.FileName}...";
        OnPropertyChanged(nameof(SelectedSaveSummary));

        await BuildPreviewDescriptorAsync(selectedSave, forceRefreshPreview: true);
    }

    private async Task BuildPreviewDescriptorAsync(SaveFileEntry selectedSave, bool forceRefreshPreview)
    {
        CancelSelectionLoad();
        CancelCacheBuild();
        CancelArtifactPrime();
        var cts = new CancellationTokenSource();
        _cacheBuildCts = cts;

        IsBuildingCache = true;
        try
        {
            SavePreviewDescriptorBuildResult result;
            var observer = new CacheWarmupObserver
            {
                ReportProgress = progress =>
                {
                    CacheStatusText = string.IsNullOrWhiteSpace(progress.Detail)
                        ? $"Building descriptor... {progress.Percent}%"
                        : $"{progress.Detail} ({progress.Percent}%)";
                }
            };
            var progress = new Progress<SavePreviewDescriptorBuildProgress>(update =>
            {
                CacheStatusText = string.IsNullOrWhiteSpace(update.Detail)
                    ? $"Building descriptor... {update.Percent}%"
                    : $"{update.Detail} ({update.Percent}%)";
            });
            result = await _lifecycleController.EnsureDescriptorAsync(selectedSave.FilePath, observer, progress, cts.Token);

            if (cts.IsCancellationRequested ||
                SelectedSave is null ||
                !string.Equals(NormalizePath(SelectedSave.FilePath), NormalizePath(selectedSave.FilePath), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!result.Succeeded)
            {
                CacheStatusText = string.IsNullOrWhiteSpace(result.Error)
                    ? "Failed to build preview descriptor."
                    : result.Error;
                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    StatusText = result.Error;
                }

                if (result.Manifest is not null)
                {
                    _currentManifest = result.Manifest;
                    OnPropertyChanged(nameof(SelectedSaveSummary));
                }

                if (forceRefreshPreview)
                {
                    Surface.NotifyPreviewSourceChanged(invalidateCaches: true);
                }

                return;
            }

            _currentSnapshot = result.Snapshot ?? _currentSnapshot;
            _currentManifest = result.Manifest;
            CacheStatusText = _currentManifest is null
                ? "Descriptor ready."
                : $"Descriptor ready: {_currentManifest.ReadyHouseholdCount}/{_currentManifest.TotalHouseholdCount} households";
            StatusText = $"Loaded save {selectedSave.FileName} and refreshed its preview descriptor.";
            OnPropertyChanged(nameof(SelectedSaveSummary));

            if (forceRefreshPreview)
            {
                Surface.NotifyPreviewSourceChanged(invalidateCaches: true);
            }

            TryRestoreSelectedPreviewItem();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_cacheBuildCts, cts))
            {
                _cacheBuildCts = null;
            }

            IsBuildingCache = false;
        }
    }

    private void OnSurfacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(TrayLikePreviewSurfaceViewModel.SelectedPreviewItem), StringComparison.Ordinal) &&
            !string.Equals(e.PropertyName, nameof(TrayLikePreviewSurfaceViewModel.HasSelectedPreviewItem), StringComparison.Ordinal))
        {
            return;
        }

        CancelArtifactPrime();
        _pendingSelectedPreviewHouseholdKey = Surface.SelectedPreviewItem?.TrayItemKey ?? string.Empty;
        ResolveSelectedPreviewHousehold();
        ScheduleIdleArtifactPrime();
        AnalyzeDependenciesCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    private async Task<bool> EnsureCurrentSnapshotAsync()
    {
        if (_currentSnapshot is not null)
        {
            return true;
        }

        if (SelectedSave is null)
        {
            return false;
        }

        CancelSelectionLoad();
        var cts = new CancellationTokenSource();
        _selectionLoadCts = cts;
        var selectedSave = SelectedSave;

        try
        {
            var loadResult = await _lifecycleController.LoadHouseholdsAsync(selectedSave.FilePath, cts.Token);

            if (cts.IsCancellationRequested ||
                SelectedSave is null ||
                !string.Equals(NormalizePath(SelectedSave.FilePath), NormalizePath(selectedSave.FilePath), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!loadResult.Success || loadResult.Snapshot is null)
            {
                if (!string.IsNullOrWhiteSpace(loadResult.Error))
                {
                    StatusText = loadResult.Error;
                }

                return false;
            }

            _currentSnapshot = loadResult.Snapshot;
            ResolveSelectedPreviewHousehold();
            ScheduleIdleArtifactPrime();
            OnPropertyChanged(nameof(SelectedSaveSummary));
            ExportCommand.NotifyCanExecuteChanged();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            if (ReferenceEquals(_selectionLoadCts, cts))
            {
                _selectionLoadCts = null;
            }

            cts.Dispose();
        }
    }

    private void ResolveSelectedPreviewHousehold()
    {
        _selectedPreviewHousehold = null;
        if (Surface.SelectedPreviewItem is null || _currentManifest is null || _currentSnapshot is null)
        {
            return;
        }

        var matchedEntry = _currentManifest.Entries.FirstOrDefault(entry =>
            string.Equals(entry.TrayItemKey, Surface.SelectedPreviewItem.TrayItemKey, StringComparison.OrdinalIgnoreCase));
        if (matchedEntry is null)
        {
            return;
        }

        _selectedPreviewHousehold = _currentSnapshot.Households.FirstOrDefault(item => item.HouseholdId == matchedEntry.HouseholdId);
    }

    private void TryRestoreSelectedPreviewItem()
    {
        if (Surface.PreviewItems.Count == 0)
        {
            return;
        }

        var targetKey = _pendingSelectedPreviewHouseholdKey;
        var currentSelection = Surface.SelectedPreviewItem;
        if (!string.IsNullOrWhiteSpace(targetKey) &&
            currentSelection is not null &&
            string.Equals(currentSelection.TrayItemKey, targetKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(targetKey) &&
            currentSelection is not null &&
            IsReadyPreviewItem(currentSelection.TrayItemKey))
        {
            return;
        }

        var targetItem = !string.IsNullOrWhiteSpace(targetKey)
            ? Surface.PreviewItems.FirstOrDefault(item =>
                string.Equals(item.Item.TrayItemKey, targetKey, StringComparison.OrdinalIgnoreCase))
            : null;
        if (targetItem is null && _currentManifest is not null)
        {
            var readyKeys = _currentManifest.Entries
                .Where(entry => string.Equals(entry.BuildState, "Ready", StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.TrayItemKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            targetItem = Surface.PreviewItems.FirstOrDefault(item => readyKeys.Contains(item.Item.TrayItemKey));
        }

        targetItem ??= Surface.PreviewItems.FirstOrDefault();
        if (targetItem is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (Surface.SelectedPreviewItem is null ||
                !string.Equals(Surface.SelectedPreviewItem.TrayItemKey, targetItem.Item.TrayItemKey, StringComparison.OrdinalIgnoreCase))
            {
                Surface.ApplySelection(targetItem, controlPressed: false, shiftPressed: false);
            }
        });
    }

    private bool IsReadyPreviewItem(string trayItemKey)
    {
        if (string.IsNullOrWhiteSpace(trayItemKey) || _currentManifest is null)
        {
            return false;
        }

        return _currentManifest.Entries.Any(entry =>
            string.Equals(entry.TrayItemKey, trayItemKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.BuildState, "Ready", StringComparison.OrdinalIgnoreCase));
    }

    private async Task AnalyzeDependenciesAsync()
    {
        if (SelectedSave is null || Surface.SelectedPreviewItem is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            StatusText = await _dependencyAnalysisController.AnalyzeAsync(
                SelectedSave.FilePath,
                Surface.SelectedPreviewItem.TrayItemKey);
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportAsync()
    {
        if (SelectedSave is null)
        {
            StatusText = "Select a cached household before exporting.";
            return;
        }

        if (_selectedPreviewHousehold is null)
        {
            var snapshotReady = await EnsureCurrentSnapshotAsync();
            if (!snapshotReady || _selectedPreviewHousehold is null)
            {
                StatusText = "Select a cached household before exporting.";
                return;
            }
        }

        if (!HasExportRootPath)
        {
            StatusText = "Export root path is missing or does not exist.";
            return;
        }

        IsBusy = true;
        try
        {
            var request = new SaveHouseholdExportRequest
            {
                SourceSavePath = SelectedSave.FilePath,
                HouseholdId = _selectedPreviewHousehold.HouseholdId,
                ExportRootPath = ExportRootPath,
                CreatorName = Environment.UserName,
                CreatorId = ComputeCreatorId(Environment.UserName),
                GenerateThumbnails = GenerateThumbnails
            };

            var result = await _exportController.ExportAsync(request);
            if (!result.Succeeded)
            {
                StatusText = string.IsNullOrWhiteSpace(result.Error)
                    ? "Export failed."
                    : result.Error;
                ExportSummaryText = StatusText;
                return;
            }

            StatusText = $"Exported to {result.ExportDirectory}";
            ExportSummaryText = _exportController.BuildSummary(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private TrayPreviewInput? BuildCurrentPreviewInput()
    {
        if (SelectedSave is null)
        {
            return null;
        }

        if (_currentManifest is null)
        {
            return null;
        }

        var normalizedSavePath = NormalizePath(SelectedSave.FilePath);
        if (string.IsNullOrWhiteSpace(normalizedSavePath) || !File.Exists(normalizedSavePath))
        {
            return null;
        }

        return Filter.BuildInput(PreviewSourceRef.ForSaveDescriptor(normalizedSavePath));
    }

    private void CancelCacheBuild()
    {
        try
        {
            _cacheBuildCts?.Cancel();
        }
        catch
        {
        }
        finally
        {
            _cacheBuildCts?.Dispose();
            _cacheBuildCts = null;
        }
    }

    private void CancelSelectionLoad()
    {
        try
        {
            _selectionLoadCts?.Cancel();
        }
        catch
        {
        }
        finally
        {
            _selectionLoadCts?.Dispose();
            _selectionLoadCts = null;
        }
    }

    private void ScheduleIdleArtifactPrime()
    {
        var selectedSave = SelectedSave;
        var selectedPreviewItem = Surface.SelectedPreviewItem;
        if (!_lifecycleController.ShouldQueueIdleArtifactPrime(
                _isActive,
                IsBusy,
                selectedSave,
                selectedPreviewItem?.TrayItemKey,
                _selectedPreviewHousehold))
        {
            return;
        }

        CancelArtifactPrime();
        _artifactPrimeCts = new CancellationTokenSource();
        var token = _artifactPrimeCts.Token;
        if (selectedSave is null || selectedPreviewItem is null)
        {
            return;
        }

        var saveFilePath = selectedSave.FilePath;
        var trayItemKey = selectedPreviewItem.TrayItemKey;
        _ = Task.Run(async () =>
        {
            try
            {
                await _lifecycleController.QueueOrEnsureIdleArtifactPrimeAsync(
                    saveFilePath,
                    trayItemKey,
                    () => _isActive &&
                          !IsBusy &&
                          SelectedSave is not null &&
                          Surface.SelectedPreviewItem is not null &&
                          string.Equals(SelectedSave.FilePath, saveFilePath, StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(Surface.SelectedPreviewItem.TrayItemKey, trayItemKey, StringComparison.OrdinalIgnoreCase),
                    token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }, token);
    }

    private void CancelArtifactPrime()
    {
        try
        {
            _artifactPrimeCts?.Cancel();
        }
        catch
        {
        }
        finally
        {
            _artifactPrimeCts?.Dispose();
            _artifactPrimeCts = null;
        }
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasValidSavesPath));
        OnPropertyChanged(nameof(HasExportRootPath));
        OnPropertyChanged(nameof(SelectedSaveSummary));
        RefreshSavesCommand.NotifyCanExecuteChanged();
        BrowseExportRootCommand.NotifyCanExecuteChanged();
        RebuildCacheCommand.NotifyCanExecuteChanged();
        ClearSelectedSaveCacheCommand.NotifyCanExecuteChanged();
        AnalyzeDependenciesCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    private string NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim().Trim('"');
        var fileResolved = _pathIdentityResolver.ResolveFile(trimmed);
        if (fileResolved.Exists || Path.HasExtension(trimmed))
        {
            if (!string.IsNullOrWhiteSpace(fileResolved.CanonicalPath))
            {
                return fileResolved.CanonicalPath;
            }

            if (!string.IsNullOrWhiteSpace(fileResolved.FullPath))
            {
                return fileResolved.FullPath;
            }
        }

        var directoryResolved = _pathIdentityResolver.ResolveDirectory(trimmed);
        if (!string.IsNullOrWhiteSpace(directoryResolved.CanonicalPath))
        {
            return directoryResolved.CanonicalPath;
        }

        if (!string.IsNullOrWhiteSpace(directoryResolved.FullPath))
        {
            return directoryResolved.FullPath;
        }

        return trimmed;
    }

    private static ulong ComputeCreatorId(string value)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offsetBasis;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash == 0 ? 1UL : hash;
    }
}
