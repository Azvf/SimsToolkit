
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Presets;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.Models;
using SimsModDesktop.ViewModels.Infrastructure;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IExecutionCoordinator _executionCoordinator;
    private readonly ITrayPreviewCoordinator _trayPreviewCoordinator;
    private readonly IFileDialogService _fileDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ISettingsStore _settingsStore;
    private readonly IActionModuleRegistry _moduleRegistry;
    private readonly IQuickPresetCatalog _quickPresetCatalog;
    private readonly IQuickPresetApplier _quickPresetApplier;

    private readonly StringWriter _logWriter = new();
    private readonly List<QuickPresetDefinition> _allQuickPresets = new();
    private CancellationTokenSource? _executionCts;
    private CancellationTokenSource? _validationDebounceCts;
    private bool _isTrayPreviewPageLoading;
    private bool _isBusy;
    private AppWorkspace _workspace = AppWorkspace.Toolkit;
    private SimsAction _selectedAction = SimsAction.Organize;
    private string _scriptPath = string.Empty;
    private bool _whatIf;
    private string _statusMessage = "Ready.";
    private bool _isProgressIndeterminate;
    private int _progressValue;
    private string _progressMessage = "Idle";
    private string _logText = string.Empty;
    private int _trayPreviewCurrentPage = 1;
    private int _trayPreviewTotalPages = 1;
    private string _previewSummaryText = "No preview data loaded.";
    private string _previewDashboardTotalItems = "0";
    private string _previewDashboardTotalFiles = "0";
    private string _previewDashboardTotalSize = "0 MB";
    private string _previewDashboardLatestWrite = "-";
    private string _previewPageText = "Page 0/0";
    private string _previewLazyLoadText = "Lazy cache 0/0 pages";
    private string _quickPresetStatusText = "Preset templates not loaded.";
    private string _quickPresetSearchText = string.Empty;
    private string _validationSummaryText = "预检尚未开始。";
    private bool _hasValidationErrors;
    private bool _isToolkitLogDrawerOpen;
    private bool _isTrayPreviewLogDrawerOpen;
    private bool _isToolkitAdvancedOpen;
    private bool _isTrayPreviewAdvancedOpen;
    private AppSettings.QuickPresetSettings _quickPresetSettings = new();
    private bool _isInitialized;

    public MainWindowViewModel(
        IExecutionCoordinator executionCoordinator,
        ITrayPreviewCoordinator trayPreviewCoordinator,
        IFileDialogService fileDialogService,
        IConfirmationDialogService confirmationDialogService,
        ISettingsStore settingsStore,
        IQuickPresetCatalog quickPresetCatalog,
        IActionModuleRegistry moduleRegistry,
        IQuickPresetApplier quickPresetApplier,
        OrganizePanelViewModel organize,
        FlattenPanelViewModel flatten,
        NormalizePanelViewModel normalize,
        MergePanelViewModel merge,
        FindDupPanelViewModel findDup,
        TrayDependenciesPanelViewModel trayDependencies,
        TrayPreviewPanelViewModel trayPreview,
        SharedFileOpsPanelViewModel sharedFileOps)
    {
        _executionCoordinator = executionCoordinator;
        _trayPreviewCoordinator = trayPreviewCoordinator;
        _fileDialogService = fileDialogService;
        _confirmationDialogService = confirmationDialogService;
        _settingsStore = settingsStore;
        _quickPresetCatalog = quickPresetCatalog;
        _moduleRegistry = moduleRegistry;
        _quickPresetApplier = quickPresetApplier;

        Organize = organize;
        Flatten = flatten;
        Normalize = normalize;
        Merge = merge;
        Merge.SourcePaths.CollectionChanged += OnMergeSourcePathsChanged;
        FindDup = findDup;
        TrayDependencies = trayDependencies;
        TrayPreview = trayPreview;
        SharedFileOps = sharedFileOps;
        PreviewItems = new ObservableCollection<SimsTrayPreviewItem>();
        QuickPresets = new ObservableCollection<QuickPresetListItem>();

        var registeredToolkitActions = _moduleRegistry.All
            .Select(module => module.Action)
            .Where(action => action != SimsAction.TrayPreview)
            .ToHashSet();

        AvailableToolkitActions = Enum.GetValues<SimsAction>()
            .Where(action => action != SimsAction.TrayPreview && registeredToolkitActions.Contains(action))
            .ToArray();

        BrowseFolderCommand = new AsyncRelayCommand<string>(BrowseFolderAsync, _ => !IsBusy, disableWhileRunning: false);
        BrowseCsvPathCommand = new AsyncRelayCommand<string>(BrowseCsvPathAsync, _ => !IsBusy, disableWhileRunning: false);
        SwitchToToolkitWorkspaceCommand = new RelayCommand(
            () => Workspace = AppWorkspace.Toolkit,
            () => !IsBusy);
        SwitchToTrayPreviewWorkspaceCommand = new RelayCommand(
            () => Workspace = AppWorkspace.TrayPreview,
            () => !IsBusy);
        AddMergeSourcePathCommand = new RelayCommand<MergeSourcePathEntryViewModel>(AddMergeSourcePath, _ => !IsBusy);
        BrowseMergeSourcePathCommand = new AsyncRelayCommand<MergeSourcePathEntryViewModel>(BrowseMergeSourcePathAsync, _ => !IsBusy, disableWhileRunning: false);
        RemoveMergeSourcePathCommand = new RelayCommand<MergeSourcePathEntryViewModel>(
            RemoveMergeSourcePath,
            _ => !IsBusy && Merge.SourcePaths.Count > 1);
        RunCommand = new AsyncRelayCommand(RunToolkitAsync, () => !IsBusy && IsToolkitWorkspace && !HasValidationErrors);
        RunTrayPreviewCommand = new AsyncRelayCommand(() => RunTrayPreviewAsync(), () => !IsBusy && IsTrayPreviewWorkspace && !HasValidationErrors);
        RunActiveWorkspaceCommand = new AsyncRelayCommand(RunActiveWorkspaceAsync, () => !IsBusy && !HasValidationErrors);
        RunQuickPresetCommand = new AsyncRelayCommand<QuickPresetDefinition>(RunQuickPresetAsync, preset => !IsBusy && preset is not null);
        ReloadQuickPresetsCommand = new AsyncRelayCommand(ReloadQuickPresetsAsync, () => !IsBusy);
        OpenQuickPresetFolderCommand = new RelayCommand(OpenQuickPresetFolder, () => !IsBusy);
        CancelCommand = new RelayCommand(CancelExecution, () => IsBusy);
        PreviewPrevPageCommand = new AsyncRelayCommand(LoadPreviousTrayPreviewPageAsync, () => CanGoPrevPage);
        PreviewNextPageCommand = new AsyncRelayCommand(LoadNextTrayPreviewPageAsync, () => CanGoNextPage);
        ToggleToolkitAdvancedCommand = new RelayCommand(() => IsToolkitAdvancedOpen = !IsToolkitAdvancedOpen, () => IsToolkitWorkspace);
        ToggleTrayPreviewAdvancedCommand = new RelayCommand(() => IsTrayPreviewAdvancedOpen = !IsTrayPreviewAdvancedOpen, () => IsTrayPreviewWorkspace);
        ClearLogCommand = new RelayCommand(ClearLog, () => !string.IsNullOrWhiteSpace(LogText));

        HookValidationTracking();
    }

    public OrganizePanelViewModel Organize { get; }
    public FlattenPanelViewModel Flatten { get; }
    public NormalizePanelViewModel Normalize { get; }
    public MergePanelViewModel Merge { get; }
    public FindDupPanelViewModel FindDup { get; }
    public TrayDependenciesPanelViewModel TrayDependencies { get; }
    public TrayPreviewPanelViewModel TrayPreview { get; }
    public SharedFileOpsPanelViewModel SharedFileOps { get; }
    public ObservableCollection<SimsTrayPreviewItem> PreviewItems { get; }
    public ObservableCollection<QuickPresetListItem> QuickPresets { get; }

    public IReadOnlyList<SimsAction> AvailableToolkitActions { get; }

    public AsyncRelayCommand<string> BrowseFolderCommand { get; }
    public AsyncRelayCommand<string> BrowseCsvPathCommand { get; }
    public RelayCommand SwitchToToolkitWorkspaceCommand { get; }
    public RelayCommand SwitchToTrayPreviewWorkspaceCommand { get; }
    public RelayCommand<MergeSourcePathEntryViewModel> AddMergeSourcePathCommand { get; }
    public AsyncRelayCommand<MergeSourcePathEntryViewModel> BrowseMergeSourcePathCommand { get; }
    public RelayCommand<MergeSourcePathEntryViewModel> RemoveMergeSourcePathCommand { get; }
    public AsyncRelayCommand RunCommand { get; }
    public AsyncRelayCommand RunTrayPreviewCommand { get; }
    public AsyncRelayCommand RunActiveWorkspaceCommand { get; }
    public AsyncRelayCommand<QuickPresetDefinition> RunQuickPresetCommand { get; }
    public AsyncRelayCommand ReloadQuickPresetsCommand { get; }
    public RelayCommand OpenQuickPresetFolderCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand PreviewPrevPageCommand { get; }
    public AsyncRelayCommand PreviewNextPageCommand { get; }
    public RelayCommand ToggleToolkitAdvancedCommand { get; }
    public RelayCommand ToggleTrayPreviewAdvancedCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    public AppWorkspace Workspace
    {
        get => _workspace;
        set
        {
            if (!SetProperty(ref _workspace, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsToolkitWorkspace));
            OnPropertyChanged(nameof(IsTrayPreviewWorkspace));
            StatusMessage = "Ready.";
            NotifyCommandStates();
            QueueValidationRefresh();

            if (_isInitialized && value == AppWorkspace.TrayPreview)
            {
                _ = TryAutoLoadTrayPreviewAsync();
            }
        }
    }

    public SimsAction SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (!SetProperty(ref _selectedAction, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsOrganizeVisible));
            OnPropertyChanged(nameof(IsFlattenVisible));
            OnPropertyChanged(nameof(IsNormalizeVisible));
            OnPropertyChanged(nameof(IsMergeVisible));
            OnPropertyChanged(nameof(IsFindDupVisible));
            OnPropertyChanged(nameof(IsTrayDependenciesVisible));
            StatusMessage = "Ready.";
            QueueValidationRefresh();
        }
    }

    public string ScriptPath
    {
        get => _scriptPath;
        set
        {
            if (!SetProperty(ref _scriptPath, value))
            {
                return;
            }

            QueueValidationRefresh();
        }
    }

    public bool WhatIf
    {
        get => _whatIf;
        set
        {
            if (!SetProperty(ref _whatIf, value))
            {
                return;
            }

            QueueValidationRefresh();
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

            NotifyCommandStates();
        }
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public int ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        private set => SetProperty(ref _progressMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string LogText
    {
        get => _logText;
        private set
        {
            if (!SetProperty(ref _logText, value))
            {
                return;
            }

            ClearLogCommand.NotifyCanExecuteChanged();
        }
    }

    public string PreviewSummaryText
    {
        get => _previewSummaryText;
        private set => SetProperty(ref _previewSummaryText, value);
    }

    public string PreviewDashboardTotalItems
    {
        get => _previewDashboardTotalItems;
        private set => SetProperty(ref _previewDashboardTotalItems, value);
    }

    public string PreviewDashboardTotalFiles
    {
        get => _previewDashboardTotalFiles;
        private set => SetProperty(ref _previewDashboardTotalFiles, value);
    }

    public string PreviewDashboardTotalSize
    {
        get => _previewDashboardTotalSize;
        private set => SetProperty(ref _previewDashboardTotalSize, value);
    }

    public string PreviewDashboardLatestWrite
    {
        get => _previewDashboardLatestWrite;
        private set => SetProperty(ref _previewDashboardLatestWrite, value);
    }

    public string PreviewPageText
    {
        get => _previewPageText;
        private set => SetProperty(ref _previewPageText, value);
    }

    public string PreviewLazyLoadText
    {
        get => _previewLazyLoadText;
        private set => SetProperty(ref _previewLazyLoadText, value);
    }

    public string QuickPresetStatusText
    {
        get => _quickPresetStatusText;
        private set => SetProperty(ref _quickPresetStatusText, value);
    }

    public string QuickPresetSearchText
    {
        get => _quickPresetSearchText;
        set
        {
            if (!SetProperty(ref _quickPresetSearchText, value))
            {
                return;
            }

            RefreshQuickPresetItems();
        }
    }

    public bool IsToolkitLogDrawerOpen
    {
        get => _isToolkitLogDrawerOpen;
        set
        {
            if (!SetProperty(ref _isToolkitLogDrawerOpen, value))
            {
                return;
            }

            NotifyCommandStates();
        }
    }

    public bool IsTrayPreviewLogDrawerOpen
    {
        get => _isTrayPreviewLogDrawerOpen;
        set
        {
            if (!SetProperty(ref _isTrayPreviewLogDrawerOpen, value))
            {
                return;
            }

            NotifyCommandStates();
        }
    }

    public bool IsToolkitAdvancedOpen
    {
        get => _isToolkitAdvancedOpen;
        set => SetProperty(ref _isToolkitAdvancedOpen, value);
    }

    public bool IsTrayPreviewAdvancedOpen
    {
        get => _isTrayPreviewAdvancedOpen;
        set => SetProperty(ref _isTrayPreviewAdvancedOpen, value);
    }

    public string ValidationSummaryText
    {
        get => _validationSummaryText;
        private set => SetProperty(ref _validationSummaryText, value);
    }

    public bool HasValidationErrors
    {
        get => _hasValidationErrors;
        private set
        {
            if (!SetProperty(ref _hasValidationErrors, value))
            {
                return;
            }

            NotifyCommandStates();
        }
    }

    public bool IsOrganizeVisible => SelectedAction == SimsAction.Organize;
    public bool IsFlattenVisible => SelectedAction == SimsAction.Flatten;
    public bool IsNormalizeVisible => SelectedAction == SimsAction.Normalize;
    public bool IsMergeVisible => SelectedAction == SimsAction.Merge;
    public bool IsFindDupVisible => SelectedAction == SimsAction.FindDuplicates;
    public bool IsTrayDependenciesVisible => SelectedAction == SimsAction.TrayDependencies;
    public bool IsToolkitWorkspace => Workspace == AppWorkspace.Toolkit;
    public bool IsTrayPreviewWorkspace => Workspace == AppWorkspace.TrayPreview;

    public bool CanGoPrevPage => !IsBusy && !_isTrayPreviewPageLoading && _trayPreviewCurrentPage > 1;
    public bool CanGoNextPage => !IsBusy && !_isTrayPreviewPageLoading && _trayPreviewCurrentPage < _trayPreviewTotalPages;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        var settings = await _settingsStore.LoadAsync();
        ApplySettings(settings);
        await ReloadQuickPresetsAsync();

        ScriptPath = ResolveFixedScriptPath();
        if (!File.Exists(ScriptPath))
        {
            StatusMessage = $"Script not found: {ScriptPath}";
        }

        ClearTrayPreview();
        if (File.Exists(ScriptPath))
        {
            StatusMessage = "Ready.";
        }
        _isInitialized = true;
        RefreshValidationNow();

        if (Workspace == AppWorkspace.TrayPreview)
        {
            _ = TryAutoLoadTrayPreviewAsync();
        }
    }

    public async Task PersistSettingsAsync()
    {
        _validationDebounceCts?.Cancel();
        var settings = CaptureSettings();
        await _settingsStore.SaveAsync(settings);
    }

    private void HookValidationTracking()
    {
        SubscribeForValidation(Organize);
        SubscribeForValidation(Flatten);
        SubscribeForValidation(Normalize);
        SubscribeForValidation(FindDup);
        SubscribeForValidation(TrayDependencies);
        SubscribeForValidation(TrayPreview);
        SubscribeForValidation(SharedFileOps);
        SubscribeForValidation(Merge);

        foreach (var sourcePath in Merge.SourcePaths)
        {
            sourcePath.PropertyChanged += OnMergeSourcePathPropertyChanged;
        }
    }

    private void SubscribeForValidation(INotifyPropertyChanged source)
    {
        source.PropertyChanged += (_, _) => QueueValidationRefresh();
    }

    private void OnMergeSourcePathsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var removed in e.OldItems.OfType<MergeSourcePathEntryViewModel>())
            {
                removed.PropertyChanged -= OnMergeSourcePathPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var added in e.NewItems.OfType<MergeSourcePathEntryViewModel>())
            {
                added.PropertyChanged += OnMergeSourcePathPropertyChanged;
            }
        }

        NotifyCommandStates();
        QueueValidationRefresh();
    }

    private void OnMergeSourcePathPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        QueueValidationRefresh();
    }

    private void QueueValidationRefresh()
    {
        if (!_isInitialized)
        {
            return;
        }

        _validationDebounceCts?.Cancel();
        _validationDebounceCts?.Dispose();
        _validationDebounceCts = new CancellationTokenSource();
        var cancellationToken = _validationDebounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            RefreshValidationNow();
        }, cancellationToken);
    }

    private void RefreshValidationNow()
    {
        ExecuteOnUi(() =>
        {
            if (IsBusy)
            {
                return;
            }

            if (IsToolkitWorkspace)
            {
                var module = _moduleRegistry.Get(SelectedAction);
                if (!TryBuildGlobalExecutionOptions(requireScriptPath: true, includeShared: module.UsesSharedFileOps, out var options, out var error))
                {
                    HasValidationErrors = true;
                    ValidationSummaryText = $"预检失败: {error}";
                    return;
                }

                if (!module.TryBuildPlan(options, out _, out error))
                {
                    HasValidationErrors = true;
                    ValidationSummaryText = $"预检失败: {error}";
                    return;
                }

                HasValidationErrors = false;
                ValidationSummaryText = $"预检通过: {module.DisplayName} 参数有效。";
                return;
            }

            if (!TryBuildTrayPreviewInput(out _, out var trayPreviewError))
            {
                HasValidationErrors = true;
                ValidationSummaryText = $"预检失败: {trayPreviewError}";
                return;
            }

            HasValidationErrors = false;
            ValidationSummaryText = "预检通过: Tray Preview 参数有效。";
        });
    }

    private void NotifyCommandStates()
    {
        BrowseFolderCommand.NotifyCanExecuteChanged();
        BrowseCsvPathCommand.NotifyCanExecuteChanged();
        SwitchToToolkitWorkspaceCommand.NotifyCanExecuteChanged();
        SwitchToTrayPreviewWorkspaceCommand.NotifyCanExecuteChanged();
        AddMergeSourcePathCommand.NotifyCanExecuteChanged();
        BrowseMergeSourcePathCommand.NotifyCanExecuteChanged();
        RemoveMergeSourcePathCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();
        RunTrayPreviewCommand.NotifyCanExecuteChanged();
        RunActiveWorkspaceCommand.NotifyCanExecuteChanged();
        RunQuickPresetCommand.NotifyCanExecuteChanged();
        ReloadQuickPresetsCommand.NotifyCanExecuteChanged();
        OpenQuickPresetFolderCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        PreviewPrevPageCommand.NotifyCanExecuteChanged();
        PreviewNextPageCommand.NotifyCanExecuteChanged();
        ToggleToolkitAdvancedCommand.NotifyCanExecuteChanged();
        ToggleTrayPreviewAdvancedCommand.NotifyCanExecuteChanged();
        ClearLogCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanGoPrevPage));
        OnPropertyChanged(nameof(CanGoNextPage));
    }

    private async Task RunActiveWorkspaceAsync()
    {
        if (IsToolkitWorkspace)
        {
            await RunToolkitAsync();
            return;
        }

        await RunTrayPreviewAsync();
    }

    private async Task BrowseFolderAsync(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        switch (target)
        {
            case "SourceDir":
                await BrowseSingleFolderAsync("Select SourceDir", path => Organize.SourceDir = path);
                break;
            case "ModsRoot":
                await BrowseSingleFolderAsync("Select ModsRoot", path => Organize.ModsRoot = path);
                break;
            case "TrayRoot":
                await BrowseSingleFolderAsync("Select TrayRoot", path => Organize.TrayRoot = path);
                break;
            case "FlattenRootPath":
                await BrowseSingleFolderAsync("Select FlattenRootPath", path => Flatten.RootPath = path);
                break;
            case "NormalizeRootPath":
                await BrowseSingleFolderAsync("Select NormalizeRootPath", path => Normalize.RootPath = path);
                break;
            case "MergeTargetPath":
                await BrowseSingleFolderAsync("Select MergeTargetPath", path => Merge.TargetPath = path);
                break;
            case "FindDupRootPath":
                await BrowseSingleFolderAsync("Select FindDup RootPath", path => FindDup.RootPath = path);
                break;
            case "ProbeTrayPath":
                await BrowseSingleFolderAsync("Select TrayPath (Dependency Probe)", path => TrayDependencies.TrayPath = path);
                break;
            case "ProbeModsPath":
                await BrowseSingleFolderAsync("Select ModsPath (Dependency Probe)", path => TrayDependencies.ModsPath = path);
                break;
            case "ProbeS4tiPath":
                await BrowseSingleFolderAsync("Select S4TI Install Folder", path => TrayDependencies.S4tiPath = path);
                break;
            case "ProbeExportTargetPath":
                await BrowseSingleFolderAsync("Select ExportTargetPath", path => TrayDependencies.ExportTargetPath = path);
                break;
            case "PreviewTrayRoot":
                await BrowseSingleFolderAsync("Select TrayPath (Preview)", path => TrayPreview.TrayRoot = path);
                break;
        }
    }

    private async Task BrowseCsvPathAsync(string? target)
    {
        if (!string.Equals(target, "FindDupOutputCsv", StringComparison.Ordinal))
        {
            return;
        }

        var path = await _fileDialogService.PickCsvSavePathAsync("Select OutputCsv path", "finddup-duplicates.csv");
        if (!string.IsNullOrWhiteSpace(path))
        {
            FindDup.OutputCsv = path;
        }
    }

    private async Task BrowseSingleFolderAsync(string title, Action<string> setter)
    {
        var paths = await _fileDialogService.PickFolderPathsAsync(title, allowMultiple: false);
        if (paths.Count > 0)
        {
            setter(paths[0]);
        }
    }

    private void AddMergeSourcePath(MergeSourcePathEntryViewModel? anchorEntry)
    {
        Merge.AddSourcePathAfter(anchorEntry);
    }

    private async Task BrowseMergeSourcePathAsync(MergeSourcePathEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        var selectedPaths = await _fileDialogService.PickFolderPathsAsync("Select Merge SourcePath", allowMultiple: false);
        if (selectedPaths.Count == 0)
        {
            return;
        }

        entry.Path = selectedPaths[0];
    }

    private void RemoveMergeSourcePath(MergeSourcePathEntryViewModel? entry)
    {
        Merge.RemoveSourcePath(entry);
    }

    private async Task ReloadQuickPresetsAsync()
    {
        await _quickPresetCatalog.ReloadAsync();
        _allQuickPresets.Clear();
        _allQuickPresets.AddRange(_quickPresetCatalog.GetAll());
        RefreshQuickPresetItems();

        var warnings = _quickPresetCatalog.LastWarnings;
        foreach (var warning in warnings)
        {
            AppendLog("[preset] " + warning);
        }
    }

    private void RefreshQuickPresetItems()
    {
        ExecuteOnUi(() =>
        {
            var keyword = QuickPresetSearchText.Trim();
            var filtered = _allQuickPresets
                .Where(preset => string.IsNullOrWhiteSpace(keyword)
                    || preset.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || preset.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || preset.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();

            QuickPresets.Clear();
            foreach (var preset in filtered)
            {
                var isLastApplied = string.Equals(preset.Id, _quickPresetSettings.LastPresetId, StringComparison.OrdinalIgnoreCase);
                QuickPresets.Add(new QuickPresetListItem(preset, isLastApplied));
            }

            if (_allQuickPresets.Count == 0)
            {
                QuickPresetStatusText = "No quick presets found.";
                return;
            }

            var appliedText = string.IsNullOrWhiteSpace(_quickPresetSettings.LastPresetId)
                ? "暂无最近应用"
                : $"最近应用: {_quickPresetSettings.LastPresetId}";
            QuickPresetStatusText = $"显示 {filtered.Count}/{_allQuickPresets.Count} 个预设。{appliedText}";
        });
    }

    private async Task RunQuickPresetAsync(QuickPresetDefinition? preset)
    {
        if (preset is null)
        {
            return;
        }

        if (!_quickPresetApplier.TryApply(preset, out var error))
        {
            StatusMessage = "Quick preset apply failed.";
            AppendLog("[preset-error] " + error);
            return;
        }

        if (preset.Action == SimsAction.TrayPreview)
        {
            Workspace = AppWorkspace.TrayPreview;
        }
        else
        {
            Workspace = AppWorkspace.Toolkit;
            SelectedAction = preset.Action;
        }

        _quickPresetSettings.LastPresetId = preset.Id;
        RefreshQuickPresetItems();
        AppendLog($"[preset] applied {preset.Id} ({preset.Name})");

        if (!preset.AutoRun)
        {
            StatusMessage = $"Preset applied: {preset.Name}";
            return;
        }

        if (preset.Action == SimsAction.TrayPreview)
        {
            await RunTrayPreviewAsync();
            return;
        }

        await RunToolkitAsync();
    }

    private void OpenQuickPresetFolder()
    {
        var directory = _quickPresetCatalog.UserPresetDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            StatusMessage = "Preset folder is unavailable.";
            return;
        }

        Directory.CreateDirectory(directory);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
            StatusMessage = $"Preset folder opened: {directory}";
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to open preset folder.";
            AppendLog("[preset-error] " + ex.Message);
        }
    }

    private async Task<bool> ConfirmDangerousFindDupCleanupAsync()
    {
        if (SelectedAction != SimsAction.FindDuplicates || !FindDup.Cleanup || WhatIf)
        {
            return true;
        }

        var confirmed = await _confirmationDialogService.ConfirmAsync(new ConfirmationRequest
        {
            Title = "确认危险操作",
            Message = "你即将执行 FindDuplicates 清理模式，系统会删除重复文件（保留首个路径）。",
            ConfirmText = "确认执行",
            CancelText = "取消",
            IsDangerous = true
        });

        if (!confirmed)
        {
            StatusMessage = "已取消危险操作。";
            AppendLog("[cancel] cleanup confirmation rejected");
            return false;
        }

        return true;
    }

    private async Task RunToolkitAsync()
    {
        if (_executionCts is not null)
        {
            StatusMessage = "Execution is already running.";
            return;
        }

        var module = _moduleRegistry.Get(SelectedAction);
        if (!TryBuildGlobalExecutionOptions(requireScriptPath: true, includeShared: module.UsesSharedFileOps, out var options, out var error))
        {
            StatusMessage = error;
            AppendLog("[validation] " + error);
            return;
        }

        if (!module.TryBuildPlan(options, out var plan, out error))
        {
            StatusMessage = error;
            AppendLog("[validation] " + error);
            return;
        }

        if (plan is not CliExecutionPlan cliPlan)
        {
            StatusMessage = $"Action {SelectedAction} is not a CLI action.";
            AppendLog("[validation] " + StatusMessage);
            return;
        }

        if (!await ConfirmDangerousFindDupCleanupAsync())
        {
            return;
        }

        var input = cliPlan.Input;

        _executionCts = new CancellationTokenSource();
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

        ClearLog();
        IsBusy = true;
        SetProgress(isIndeterminate: true, percent: 0, message: "Starting...");
        AppendLog("[start] " + startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        AppendLog("[action] " + input.Action.ToString().ToLowerInvariant());
        StatusMessage = "Running...";

        try
        {
            var result = await _executionCoordinator.ExecuteAsync(
                input,
                onOutput: AppendLog,
                onProgress: HandleProgress,
                cancellationToken: _executionCts.Token);
            stopwatch.Stop();

            AppendLog($"[exit] code={result.ExitCode}");
            StatusMessage = result.ExitCode == 0
                ? $"Completed in {stopwatch.Elapsed:mm\\:ss}."
                : $"Failed with exit code {result.ExitCode} in {stopwatch.Elapsed:mm\\:ss}.";
            SetProgress(
                isIndeterminate: false,
                percent: result.ExitCode == 0 ? 100 : 0,
                message: result.ExitCode == 0 ? "Completed." : "Failed.");
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            AppendLog("[cancelled]");
            StatusMessage = "Execution cancelled.";
            SetProgress(isIndeterminate: false, percent: 0, message: "Cancelled.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AppendLog("[error] " + ex.Message);
            StatusMessage = "Execution failed.";
            SetProgress(isIndeterminate: false, percent: 0, message: "Execution failed.");
        }
        finally
        {
            _executionCts.Dispose();
            _executionCts = null;
            IsBusy = false;
            RefreshValidationNow();
        }
    }

    private async Task RunTrayPreviewAsync(TrayPreviewInput? explicitInput = null)
    {
        if (_executionCts is not null)
        {
            StatusMessage = "Execution is already running.";
            return;
        }

        TrayPreviewInput input;
        if (explicitInput is null)
        {
            if (!TryBuildTrayPreviewInput(out var built, out var validationError))
            {
                StatusMessage = validationError;
                AppendLog("[validation] " + validationError);
                return;
            }

            input = built;
        }
        else
        {
            input = explicitInput;
        }

        _executionCts = new CancellationTokenSource();
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

        _trayPreviewCoordinator.Reset();
        ClearLog();
        ClearTrayPreview();
        IsBusy = true;
        SetTrayPreviewPageLoading(true);
        SetProgress(isIndeterminate: true, percent: 0, message: "Loading tray preview dashboard...");
        AppendLog("[start] " + startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        AppendLog("[action] traypreview");
        StatusMessage = "Loading tray preview dashboard...";

        try
        {
            var result = await _trayPreviewCoordinator.LoadAsync(input, _executionCts.Token);
            stopwatch.Stop();

            SetTrayPreviewDashboard(result.Dashboard);
            SetTrayPreviewPage(result.Page, result.LoadedPageCount);

            AppendLog($"[preview] trayPath={input.TrayPath}");
            if (!string.IsNullOrWhiteSpace(input.TrayItemKey))
            {
                AppendLog($"[preview] trayItemKey={input.TrayItemKey}");
            }

            AppendLog(input.TopN is int topN && topN > 0
                ? $"[preview] topN={topN}"
                : "[preview] topN=all");
            AppendLog($"[preview] pageSize={input.PageSize}");
            AppendLog($"[preview] totalItems={result.Dashboard.TotalItems}");

            StatusMessage =
                $"Tray preview loaded: {result.Dashboard.TotalItems} items, page 1/{result.Page.TotalPages}, {stopwatch.Elapsed:mm\\:ss}.";
            SetProgress(isIndeterminate: false, percent: 100, message: "Tray preview dashboard loaded.");
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            AppendLog("[cancelled]");
            StatusMessage = "Tray preview cancelled.";
            SetProgress(isIndeterminate: false, percent: 0, message: "Cancelled.");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AppendLog("[error] " + ex.Message);
            StatusMessage = "Tray preview failed.";
            SetProgress(isIndeterminate: false, percent: 0, message: "Tray preview failed.");
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
        await LoadTrayPreviewPageAsync(_trayPreviewCurrentPage - 1);
    }

    private async Task LoadNextTrayPreviewPageAsync()
    {
        await LoadTrayPreviewPageAsync(_trayPreviewCurrentPage + 1);
    }

    private async Task LoadTrayPreviewPageAsync(int requestedPageIndex)
    {
        if (_isTrayPreviewPageLoading)
        {
            StatusMessage = "Tray preview page load is already running.";
            return;
        }

        SetTrayPreviewPageLoading(true);
        try
        {
            var result = await _trayPreviewCoordinator.LoadPageAsync(requestedPageIndex);
            SetTrayPreviewPage(result.Page, result.LoadedPageCount);
            StatusMessage = $"Tray preview page {result.Page.PageIndex}/{result.Page.TotalPages}.";
        }
        catch (Exception ex)
        {
            AppendLog("[error] " + ex.Message);
            StatusMessage = "Tray preview page load failed.";
        }
        finally
        {
            SetTrayPreviewPageLoading(false);
        }
    }

    private async Task TryAutoLoadTrayPreviewAsync()
    {
        if (!IsTrayPreviewWorkspace || IsBusy || _isTrayPreviewPageLoading)
        {
            return;
        }

        if (!TryBuildTrayPreviewInput(out var input, out _))
        {
            return;
        }

        if (_trayPreviewCoordinator.TryGetCached(input, out var cached))
        {
            SetTrayPreviewDashboard(cached.Dashboard);
            SetTrayPreviewPage(cached.Page, cached.LoadedPageCount);
            StatusMessage = $"Tray preview page {cached.Page.PageIndex}/{cached.Page.TotalPages}.";
            return;
        }

        await RunTrayPreviewAsync(input);
    }

    private void CancelExecution()
    {
        var cts = _executionCts;
        if (cts is null)
        {
            StatusMessage = "No running execution.";
            return;
        }

        AppendLog("[cancel] requested");
        StatusMessage = "Cancelling...";
        SetProgress(isIndeterminate: true, percent: 0, message: "Cancelling...");

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignore cancellation race when operation has already completed.
        }
    }

    private bool TryBuildGlobalExecutionOptions(
        bool requireScriptPath,
        bool includeShared,
        out GlobalExecutionOptions options,
        out string error)
    {
        options = null!;
        error = string.Empty;

        var scriptPath = ScriptPath.Trim();
        if (requireScriptPath && string.IsNullOrWhiteSpace(scriptPath))
        {
            error = "Script path is required.";
            return false;
        }

        SharedFileOpsInput shared;
        if (includeShared)
        {
            if (!TryBuildSharedFileOpsInput(out shared, out error))
            {
                return false;
            }
        }
        else
        {
            shared = new SharedFileOpsInput();
        }

        options = new GlobalExecutionOptions
        {
            ScriptPath = scriptPath,
            WhatIf = WhatIf,
            Shared = shared
        };
        return true;
    }

    private bool TryBuildTrayPreviewInput(out TrayPreviewInput input, out string error)
    {
        input = null!;
        if (!TryBuildGlobalExecutionOptions(requireScriptPath: false, includeShared: false, out var options, out error))
        {
            return false;
        }

        var module = _moduleRegistry.Get(SimsAction.TrayPreview);
        if (!module.TryBuildPlan(options, out var plan, out error))
        {
            return false;
        }

        if (plan is not TrayPreviewExecutionPlan trayPreviewPlan)
        {
            error = "Tray preview module returned unsupported execution plan.";
            return false;
        }

        input = trayPreviewPlan.Input;
        return true;
    }

    private bool TryBuildSharedFileOpsInput(out SharedFileOpsInput input, out string error)
    {
        input = null!;
        error = string.Empty;

        if (!InputParsing.TryParseOptionalInt(SharedFileOps.PrefixHashBytesText, 1024, 104857600, out var prefixHashBytes, out error))
        {
            return false;
        }

        if (!InputParsing.TryParseOptionalInt(SharedFileOps.HashWorkerCountText, 1, 64, out var hashWorkerCount, out error))
        {
            return false;
        }

        input = new SharedFileOpsInput
        {
            SkipPruneEmptyDirs = SharedFileOps.SkipPruneEmptyDirs,
            ModFilesOnly = SharedFileOps.ModFilesOnly,
            ModExtensions = InputParsing.ParseDelimitedList(SharedFileOps.ModExtensionsText),
            VerifyContentOnNameConflict = SharedFileOps.VerifyContentOnNameConflict,
            PrefixHashBytes = prefixHashBytes,
            HashWorkerCount = hashWorkerCount
        };

        return true;
    }

    private void HandleProgress(SimsProgressUpdate progress)
    {
        var normalizedPercent = Math.Clamp(progress.Percent, 0, 100);
        var hasTotal = progress.Total > 0;
        var text = hasTotal
            ? $"{progress.Stage}: {progress.Current}/{progress.Total} ({normalizedPercent}%)"
            : progress.Stage;

        if (!string.IsNullOrWhiteSpace(progress.Detail))
        {
            text = $"{text} - {progress.Detail}";
        }

        SetProgress(
            isIndeterminate: !hasTotal || progress.Percent < 0,
            percent: normalizedPercent,
            message: text);
    }

    private void SetProgress(bool isIndeterminate, int percent, string message)
    {
        ExecuteOnUi(() =>
        {
            IsProgressIndeterminate = isIndeterminate;
            ProgressValue = isIndeterminate ? 0 : Math.Clamp(percent, 0, 100);
            ProgressMessage = message;
        });
    }

    private void ClearLog()
    {
        ExecuteOnUi(() =>
        {
            _logWriter.GetStringBuilder().Clear();
            LogText = string.Empty;
        });
    }

    private void AppendLog(string message)
    {
        ExecuteOnUi(() =>
        {
            _logWriter.WriteLine(message);
            LogText = _logWriter.ToString();
        });
    }

    private void ClearTrayPreview()
    {
        ExecuteOnUi(() =>
        {
            PreviewItems.Clear();
            PreviewSummaryText = "No preview data loaded.";
            PreviewDashboardTotalItems = "0";
            PreviewDashboardTotalFiles = "0";
            PreviewDashboardTotalSize = "0 MB";
            PreviewDashboardLatestWrite = "-";
            PreviewPageText = "Page 0/0";
            PreviewLazyLoadText = "Lazy cache 0/0 pages";
            _trayPreviewCurrentPage = 1;
            _trayPreviewTotalPages = 1;
            NotifyCommandStates();
        });
    }

    private void SetTrayPreviewDashboard(SimsTrayPreviewDashboard dashboard)
    {
        ExecuteOnUi(() =>
        {
            PreviewDashboardTotalItems = dashboard.TotalItems.ToString("N0");
            PreviewDashboardTotalFiles = dashboard.TotalFiles.ToString("N0");
            PreviewDashboardTotalSize = $"{dashboard.TotalMB:N2} MB";
            PreviewDashboardLatestWrite = dashboard.LatestWriteTimeLocal == DateTime.MinValue
                ? "-"
                : dashboard.LatestWriteTimeLocal.ToString("yyyy-MM-dd HH:mm");

            var breakdown = string.IsNullOrWhiteSpace(dashboard.PresetTypeBreakdown)
                ? "Type: n/a"
                : $"Type: {dashboard.PresetTypeBreakdown}";
            PreviewSummaryText = $"Dashboard ready. {breakdown}";
        });
    }

    private void SetTrayPreviewPage(SimsTrayPreviewPage page, int loadedPageCount)
    {
        ExecuteOnUi(() =>
        {
            PreviewItems.Clear();
            foreach (var item in page.Items)
            {
                PreviewItems.Add(item);
            }

            _trayPreviewCurrentPage = page.PageIndex;
            _trayPreviewTotalPages = Math.Max(page.TotalPages, 1);
            var firstItemIndex = page.Items.Count == 0 ? 0 : ((page.PageIndex - 1) * page.PageSize) + 1;
            var lastItemIndex = page.Items.Count == 0 ? 0 : firstItemIndex + page.Items.Count - 1;
            PreviewSummaryText = $"Showing {firstItemIndex}-{lastItemIndex} / {page.TotalItems} tray presets.";
            PreviewPageText = $"Page {page.PageIndex}/{Math.Max(page.TotalPages, 1)}";
            PreviewLazyLoadText = $"Lazy cache {loadedPageCount}/{Math.Max(page.TotalPages, 1)} pages";
            NotifyCommandStates();
        });
    }

    private void SetTrayPreviewPageLoading(bool loading)
    {
        ExecuteOnUi(() =>
        {
            _isTrayPreviewPageLoading = loading;
            NotifyCommandStates();
        });
    }
    private AppSettings CaptureSettings()
    {
        var settings = new AppSettings
        {
            ScriptPath = ScriptPath,
            SelectedWorkspace = Workspace,
            SelectedAction = SelectedAction,
            WhatIf = WhatIf,
            SharedFileOps = new AppSettings.SharedFileOpsSettings
            {
                SkipPruneEmptyDirs = SharedFileOps.SkipPruneEmptyDirs,
                ModFilesOnly = SharedFileOps.ModFilesOnly,
                VerifyContentOnNameConflict = SharedFileOps.VerifyContentOnNameConflict,
                ModExtensionsText = SharedFileOps.ModExtensionsText,
                PrefixHashBytesText = SharedFileOps.PrefixHashBytesText,
                HashWorkerCountText = SharedFileOps.HashWorkerCountText
            },
            QuickPresets = new AppSettings.QuickPresetSettings
            {
                EnableExternalModules = _quickPresetSettings.EnableExternalModules,
                LastPresetId = _quickPresetSettings.LastPresetId
            },
            UiState = new AppSettings.UiStateSettings
            {
                ToolkitLogDrawerOpen = IsToolkitLogDrawerOpen,
                TrayPreviewLogDrawerOpen = IsTrayPreviewLogDrawerOpen,
                ToolkitAdvancedOpen = IsToolkitAdvancedOpen,
                TrayPreviewAdvancedOpen = IsTrayPreviewAdvancedOpen
            }
        };

        foreach (var module in _moduleRegistry.All)
        {
            module.SaveToSettings(settings);
        }

        return settings;
    }

    private void ApplySettings(AppSettings settings)
    {
        ScriptPath = settings.ScriptPath;
        WhatIf = settings.WhatIf;

        SharedFileOps.SkipPruneEmptyDirs = settings.SharedFileOps.SkipPruneEmptyDirs;
        SharedFileOps.ModFilesOnly = settings.SharedFileOps.ModFilesOnly;
        SharedFileOps.VerifyContentOnNameConflict = settings.SharedFileOps.VerifyContentOnNameConflict;
        SharedFileOps.ModExtensionsText = settings.SharedFileOps.ModExtensionsText;
        SharedFileOps.PrefixHashBytesText = settings.SharedFileOps.PrefixHashBytesText;
        SharedFileOps.HashWorkerCountText = settings.SharedFileOps.HashWorkerCountText;
        _quickPresetSettings = new AppSettings.QuickPresetSettings
        {
            EnableExternalModules = settings.QuickPresets.EnableExternalModules,
            LastPresetId = settings.QuickPresets.LastPresetId
        };
        IsToolkitLogDrawerOpen = settings.UiState.ToolkitLogDrawerOpen;
        IsTrayPreviewLogDrawerOpen = settings.UiState.TrayPreviewLogDrawerOpen;
        IsToolkitAdvancedOpen = settings.UiState.ToolkitAdvancedOpen;
        IsTrayPreviewAdvancedOpen = settings.UiState.TrayPreviewAdvancedOpen;

        foreach (var module in _moduleRegistry.All)
        {
            module.LoadFromSettings(settings);
        }

        var resolvedAction = settings.SelectedAction == SimsAction.TrayPreview
            ? SimsAction.Organize
            : settings.SelectedAction;
        if (!AvailableToolkitActions.Contains(resolvedAction))
        {
            resolvedAction = SimsAction.Organize;
        }

        SelectedAction = resolvedAction;
        var resolvedWorkspace = Enum.IsDefined(settings.SelectedWorkspace)
            ? settings.SelectedWorkspace
            : AppWorkspace.Toolkit;

        Workspace = settings.SelectedAction == SimsAction.TrayPreview
            ? AppWorkspace.TrayPreview
            : resolvedWorkspace;
    }

    private static string ResolveFixedScriptPath()
    {
        var root = FindToolkitRootDirectory();
        if (string.IsNullOrWhiteSpace(root))
        {
            return string.Empty;
        }

        return Path.GetFullPath(Path.Combine(root, "sims-mod-cli.ps1"));
    }

    private static string? FindToolkitRootDirectory()
    {
        var startDirectories = new[]
        {
            new DirectoryInfo(Directory.GetCurrentDirectory()),
            new DirectoryInfo(AppContext.BaseDirectory)
        };

        foreach (var start in startDirectories)
        {
            var directory = start;
            for (var depth = 0; depth < 12 && directory is not null; depth++)
            {
                // Preferred markers at repo root.
                if (File.Exists(Path.Combine(directory.FullName, "sims-mod-cli.ps1")) ||
                    File.Exists(Path.Combine(directory.FullName, "SimsDesktopTools.sln")))
                {
                    return directory.FullName;
                }

                // Backward-compatible fallback for legacy nested solution layout.
                if (File.Exists(Path.Combine(directory.FullName, "src.sln")))
                {
                    return directory.Parent?.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static void ExecuteOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }
}
