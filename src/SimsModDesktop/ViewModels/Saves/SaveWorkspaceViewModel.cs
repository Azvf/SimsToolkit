using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.Models;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.Services;
using SimsModDesktop.ViewModels.Infrastructure;
using SimsModDesktop.ViewModels.Preview;

namespace SimsModDesktop.ViewModels.Saves;

public sealed class SaveWorkspaceViewModel : ObservableObject
{
    private readonly ISaveHouseholdCoordinator _coordinator;
    private readonly IFileDialogService _fileDialogService;
    private readonly ITrayDependenciesLauncher _trayDependenciesLauncher;

    private CancellationTokenSource? _selectionLoadCts;
    private CancellationTokenSource? _cacheBuildCts;
    private string _pendingSelectedSavePath = string.Empty;
    private string _savesPath = string.Empty;
    private string _exportRootPath = string.Empty;
    private bool _generateThumbnails = true;
    private bool _isActive;
    private bool _hasPendingRefresh;
    private bool _isBusy;
    private bool _isBuildingCache;
    private string _statusText = "Set a valid Saves path to scan save slots.";
    private string _cacheStatusText = "No save selected.";
    private string _exportLogText = "Save preview ready.";
    private IReadOnlyList<SaveFileEntry> _saveFiles = Array.Empty<SaveFileEntry>();
    private SaveFileEntry? _selectedSave;
    private SaveHouseholdSnapshot? _currentSnapshot;
    private SavePreviewCacheManifest? _currentManifest;
    private SaveHouseholdItem? _selectedPreviewHousehold;

    public SaveWorkspaceViewModel(
        ISaveHouseholdCoordinator coordinator,
        IFileDialogService fileDialogService,
        ITrayDependenciesLauncher trayDependenciesLauncher,
        ITrayPreviewRunner trayPreviewRunner,
        ITrayThumbnailService trayThumbnailService)
    {
        _coordinator = coordinator;
        _fileDialogService = fileDialogService;
        _trayDependenciesLauncher = trayDependenciesLauncher;

        Filter = new SavePreviewFilterViewModel();
        Surface = new TrayLikePreviewSurfaceViewModel(trayPreviewRunner, trayThumbnailService);
        Surface.Configure(Filter, ResolveCurrentCacheRoot, PreviewSurfaceSelectionMode.Single);
        Surface.SetFooter("Save Preview Log", ExportLogText);
        Surface.PropertyChanged += OnSurfacePropertyChanged;

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
                SaveFiles = Array.Empty<SaveFileEntry>();
                SelectedSave = null;
                _currentSnapshot = null;
                _currentManifest = null;
                _selectedPreviewHousehold = null;
                CacheStatusText = "Saves path is missing or does not exist.";
                StatusText = "Set a valid Saves path to scan save slots.";
                Surface.NotifyTrayPathChanged();
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

    public string ExportLogText
    {
        get => _exportLogText;
        private set
        {
            if (!SetProperty(ref _exportLogText, value))
            {
                return;
            }

            Surface.SetFooter("Save Preview Log", value);
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

            return $"{SelectedSave.FileName} | Ready {_currentManifest.ReadyHouseholdCount}/{_currentManifest.TotalHouseholdCount} | Failed {_currentManifest.FailedHouseholdCount} | Blocked {_currentManifest.BlockedHouseholdCount}";
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
            SelectedSavePath = SelectedSave?.FilePath ?? string.Empty,
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
            return;
        }

        if (HasValidSavesPath && (_hasPendingRefresh || SaveFiles.Count == 0))
        {
            _ = RefreshSavesAsync();
            return;
        }

        if (SelectedSave is not null)
        {
            Surface.NotifyTrayPathChanged(invalidateCaches: true);
            _ = EnsureCurrentSnapshotAsync();
        }
    }

    private async Task RefreshSavesAsync()
    {
        if (!HasValidSavesPath)
        {
            _hasPendingRefresh = false;
            SaveFiles = Array.Empty<SaveFileEntry>();
            SelectedSave = null;
            StatusText = "Saves path is missing or does not exist.";
            return;
        }

        IsBusy = true;
        try
        {
            var saveFiles = await Task.Run(() => _coordinator.GetSaveFiles(SavesPath));
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

        await BuildPreviewCacheAsync(SelectedSave, forceRefreshPreview: true);
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

        IsBusy = true;
        try
        {
            await Task.Run(() => _coordinator.ClearPreviewCache(selectedSavePath));

            if (SelectedSave is null ||
                !string.Equals(NormalizePath(SelectedSave.FilePath), selectedSavePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentManifest = null;
            _selectedPreviewHousehold = null;
            _currentSnapshot = null;
            CacheStatusText = "Cache cleared.";
            StatusText = "Selected save preview cache was cleared.";
            OnPropertyChanged(nameof(SelectedSaveSummary));
            Surface.NotifyTrayPathChanged(invalidateCaches: true);
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
            _currentSnapshot = null;
            _currentManifest = null;
            _selectedPreviewHousehold = null;
            CacheStatusText = "No save selected.";
            Surface.NotifyTrayPathChanged();
            return;
        }

        _pendingSelectedSavePath = NormalizePath(SelectedSave.FilePath);
        var selectedSave = SelectedSave;
        if (_coordinator.TryGetPreviewCacheManifest(selectedSave.FilePath, out var manifest) &&
            _coordinator.IsPreviewCacheCurrent(selectedSave.FilePath, manifest))
        {
            _currentManifest = manifest;
            _currentSnapshot = null;
            _selectedPreviewHousehold = null;
            CacheStatusText = $"Cached: Ready {manifest.ReadyHouseholdCount}, Failed {manifest.FailedHouseholdCount}, Blocked {manifest.BlockedHouseholdCount}";
            StatusText = $"Loaded cached preview for {selectedSave.FileName}.";
            OnPropertyChanged(nameof(SelectedSaveSummary));
            Surface.NotifyTrayPathChanged(invalidateCaches: true);
            _ = EnsureCurrentSnapshotAsync();
            return;
        }

        _currentManifest = null;
        _currentSnapshot = null;
        _selectedPreviewHousehold = null;
        CacheStatusText = "No preview cache yet. Building...";
        StatusText = $"Building preview cache for {selectedSave.FileName}...";
        OnPropertyChanged(nameof(SelectedSaveSummary));

        await BuildPreviewCacheAsync(selectedSave, forceRefreshPreview: true);
    }

    private async Task BuildPreviewCacheAsync(SaveFileEntry selectedSave, bool forceRefreshPreview)
    {
        CancelSelectionLoad();
        CancelCacheBuild();
        var cts = new CancellationTokenSource();
        _cacheBuildCts = cts;

        IsBuildingCache = true;
        try
        {
            var progress = new Progress<SavePreviewCacheBuildProgress>(update =>
            {
                CacheStatusText = string.IsNullOrWhiteSpace(update.Detail)
                    ? $"Building cache... {update.Percent}%"
                    : $"{update.Detail} ({update.Percent}%)";
            });

            var result = await _coordinator.BuildPreviewCacheAsync(selectedSave.FilePath, progress, cts.Token);
            if (cts.IsCancellationRequested ||
                SelectedSave is null ||
                !string.Equals(NormalizePath(SelectedSave.FilePath), NormalizePath(selectedSave.FilePath), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!result.Succeeded)
            {
                CacheStatusText = string.IsNullOrWhiteSpace(result.Error)
                    ? "Failed to build preview cache."
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
                    Surface.NotifyTrayPathChanged(invalidateCaches: true);
                }

                return;
            }

            _currentSnapshot = result.Snapshot ?? _currentSnapshot;
            _currentManifest = result.Manifest;
            CacheStatusText = _currentManifest is null
                ? "Cache ready."
                : $"Cache ready: {_currentManifest.ReadyHouseholdCount}/{_currentManifest.TotalHouseholdCount} households";
            StatusText = $"Loaded save {selectedSave.FileName} and refreshed its preview cache.";
            OnPropertyChanged(nameof(SelectedSaveSummary));

            if (forceRefreshPreview)
            {
                Surface.NotifyTrayPathChanged(invalidateCaches: true);
            }
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

        ResolveSelectedPreviewHousehold();
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
            var loadResult = await Task.Run(() =>
            {
                cts.Token.ThrowIfCancellationRequested();
                var success = _coordinator.TryLoadHouseholds(selectedSave.FilePath, out var snapshot, out var error);
                cts.Token.ThrowIfCancellationRequested();
                return (Success: success, Snapshot: snapshot, Error: error);
            });

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

    private async Task AnalyzeDependenciesAsync()
    {
        if (SelectedSave is null || Surface.SelectedPreviewItem is null)
        {
            return;
        }

        var trayPath = ResolveCurrentCacheRoot();
        if (string.IsNullOrWhiteSpace(trayPath) || !Directory.Exists(trayPath))
        {
            StatusText = "The selected save does not have a preview cache yet.";
            return;
        }

        IsBusy = true;
        try
        {
            await _trayDependenciesLauncher.RunForTrayItemAsync(trayPath, Surface.SelectedPreviewItem.TrayItemKey);
            StatusText = "Started tray dependency analysis for the selected save household.";
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

            var result = await Task.Run(() => _coordinator.Export(request));
            if (!result.Succeeded)
            {
                StatusText = string.IsNullOrWhiteSpace(result.Error)
                    ? "Export failed."
                    : result.Error;
                ExportLogText = StatusText;
                return;
            }

            StatusText = $"Exported to {result.ExportDirectory}";
            var lines = new List<string>
            {
                $"Instance: {result.InstanceIdHex}",
                $"Directory: {result.ExportDirectory}",
                "Files:"
            };
            lines.AddRange(result.WrittenFiles
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>());
            if (result.Warnings.Count > 0)
            {
                lines.Add("Warnings:");
                lines.AddRange(result.Warnings);
            }

            ExportLogText = string.Join(Environment.NewLine, lines);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string ResolveCurrentCacheRoot()
    {
        return SelectedSave is null
            ? string.Empty
            : _coordinator.GetPreviewCacheRoot(SelectedSave.FilePath);
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

    private static string NormalizePath(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Trim('"');
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
