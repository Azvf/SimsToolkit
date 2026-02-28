
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.Infrastructure.Localization;
using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.Models;
using SimsModDesktop.ViewModels.Infrastructure;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private const string DefaultLanguageCode = "en-US";

    private readonly IToolkitExecutionRunner _toolkitExecutionRunner;
    private readonly ITrayPreviewRunner _trayPreviewRunner;
    private readonly IFileDialogService _fileDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ILocalizationService _localization;
    private readonly ISettingsStore _settingsStore;
    private readonly IMainWindowSettingsProjection _settingsProjection;
    private readonly IActionModuleRegistry _moduleRegistry;
    private readonly IMainWindowPlanBuilder _planBuilder;

    private readonly StringWriter _logWriter = new();
    private CancellationTokenSource? _executionCts;
    private CancellationTokenSource? _validationDebounceCts;
    private bool _isTrayPreviewPageLoading;
    private bool _isBusy;
    private AppWorkspace _workspace = AppWorkspace.Toolkit;
    private SimsAction _selectedAction = SimsAction.Organize;
    private string _selectedLanguageCode = DefaultLanguageCode;
    private string _scriptPath = string.Empty;
    private bool _whatIf;
    private string _statusMessage = string.Empty;
    private bool _isProgressIndeterminate;
    private int _progressValue;
    private string _progressMessage = string.Empty;
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
    private string _validationSummaryText = string.Empty;
    private bool _hasValidationErrors;
    private bool _isToolkitLogDrawerOpen;
    private bool _isTrayPreviewLogDrawerOpen;
    private bool _isToolkitAdvancedOpen;
    private bool _isTrayPreviewAdvancedOpen;
    private bool _isInitialized;

    public MainWindowViewModel(
        IToolkitExecutionRunner toolkitExecutionRunner,
        ITrayPreviewRunner trayPreviewRunner,
        IFileDialogService fileDialogService,
        IConfirmationDialogService confirmationDialogService,
        ILocalizationService localization,
        ISettingsStore settingsStore,
        IMainWindowSettingsProjection settingsProjection,
        IActionModuleRegistry moduleRegistry,
        IMainWindowPlanBuilder planBuilder,
        OrganizePanelViewModel organize,
        FlattenPanelViewModel flatten,
        NormalizePanelViewModel normalize,
        MergePanelViewModel merge,
        FindDupPanelViewModel findDup,
        TrayDependenciesPanelViewModel trayDependencies,
        TrayPreviewPanelViewModel trayPreview,
        SharedFileOpsPanelViewModel sharedFileOps)
    {
        _toolkitExecutionRunner = toolkitExecutionRunner;
        _trayPreviewRunner = trayPreviewRunner;
        _fileDialogService = fileDialogService;
        _confirmationDialogService = confirmationDialogService;
        _localization = localization;
        _settingsStore = settingsStore;
        _settingsProjection = settingsProjection;
        _moduleRegistry = moduleRegistry;
        _planBuilder = planBuilder;

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
        CancelCommand = new RelayCommand(CancelExecution, () => IsBusy);
        PreviewPrevPageCommand = new AsyncRelayCommand(LoadPreviousTrayPreviewPageAsync, () => CanGoPrevPage);
        PreviewNextPageCommand = new AsyncRelayCommand(LoadNextTrayPreviewPageAsync, () => CanGoNextPage);
        ToggleToolkitAdvancedCommand = new RelayCommand(() => IsToolkitAdvancedOpen = !IsToolkitAdvancedOpen, () => IsToolkitWorkspace);
        ToggleTrayPreviewAdvancedCommand = new RelayCommand(() => IsTrayPreviewAdvancedOpen = !IsTrayPreviewAdvancedOpen, () => IsTrayPreviewWorkspace);
        ClearLogCommand = new RelayCommand(ClearLog, () => !string.IsNullOrWhiteSpace(LogText));

        _localization.PropertyChanged += OnLocalizationPropertyChanged;
        _localization.SetLanguage(_selectedLanguageCode);
        _selectedLanguageCode = _localization.CurrentLanguageCode;
        ProgressMessage = L("progress.idle");
        ClearTrayPreview();
        StatusMessage = L("status.ready");
        ValidationSummaryText = L("validation.notStarted");

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

    public IReadOnlyList<SimsAction> AvailableToolkitActions { get; }
    public IReadOnlyList<LanguageOption> AvailableLanguages => _localization.AvailableLanguages;
    public ILocalizationService Loc => _localization;
    public LanguageOption? SelectedLanguage
    {
        get => AvailableLanguages.FirstOrDefault(option =>
                   string.Equals(option.Code, _selectedLanguageCode, StringComparison.OrdinalIgnoreCase))
               ?? AvailableLanguages.FirstOrDefault();
        set => SelectedLanguageCode = value?.Code ?? DefaultLanguageCode;
    }

    public string SettingsTitleText => L("ui.settings.title");
    public string SettingsLowFrequencySectionTitleText => L("ui.settings.section.lowFrequency");
    public string SettingsLowFrequencySectionHintText => L("ui.settings.section.lowFrequencyHint");
    public string SettingsLanguageLabelText => L("ui.settings.language.label");
    public string SettingsLanguageTodoHintText => L("ui.settings.language.todoHint");
    public string SettingsPrefixHashBytesLabelText => L("ui.settings.prefixHashBytes.label");
    public string SettingsHashWorkerCountLabelText => L("ui.settings.hashWorkerCount.label");
    public string SettingsPerformanceHintText => L("ui.settings.performance.hint");

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
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand PreviewPrevPageCommand { get; }
    public AsyncRelayCommand PreviewNextPageCommand { get; }
    public RelayCommand ToggleToolkitAdvancedCommand { get; }
    public RelayCommand ToggleTrayPreviewAdvancedCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    public string SelectedLanguageCode
    {
        get => _selectedLanguageCode;
        set
        {
            _localization.SetLanguage(value);
            var normalized = _localization.CurrentLanguageCode;
            if (!SetProperty(ref _selectedLanguageCode, normalized))
            {
                return;
            }

            NotifyLocalizationDependentProperties();

            if (!_isInitialized)
            {
                return;
            }

            StatusMessage = L("status.ready");
            if (!IsBusy)
            {
                ProgressMessage = L("progress.idle");
            }

            if (PreviewItems.Count == 0)
            {
                ClearTrayPreview();
            }

            QueueValidationRefresh();
        }
    }

    public bool IsChineseTranslationTodo =>
        SelectedLanguage?.DisplayName.Contains("(TODO)", StringComparison.OrdinalIgnoreCase) == true;

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
            OnPropertyChanged(nameof(IsSharedFileOpsVisible));
            StatusMessage = L("status.ready");
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
            OnPropertyChanged(nameof(IsSharedFileOpsVisible));
            StatusMessage = L("status.ready");
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
    public bool IsSharedFileOpsVisible =>
        IsToolkitWorkspace &&
        _moduleRegistry.All.Any(module => module.Action == SelectedAction && module.UsesSharedFileOps);

    public bool CanGoPrevPage => !IsBusy && !_isTrayPreviewPageLoading && _trayPreviewCurrentPage > 1;
    public bool CanGoNextPage => !IsBusy && !_isTrayPreviewPageLoading && _trayPreviewCurrentPage < _trayPreviewTotalPages;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        var settings = await _settingsStore.LoadAsync();
        var resolved = _settingsProjection.Resolve(settings, AvailableToolkitActions);

        SelectedLanguageCode = resolved.UiLanguageCode;
        ScriptPath = resolved.ScriptPath;
        WhatIf = resolved.WhatIf;

        SharedFileOps.SkipPruneEmptyDirs = resolved.SharedFileOps.SkipPruneEmptyDirs;
        SharedFileOps.ModFilesOnly = resolved.SharedFileOps.ModFilesOnly;
        SharedFileOps.VerifyContentOnNameConflict = resolved.SharedFileOps.VerifyContentOnNameConflict;
        SharedFileOps.ModExtensionsText = resolved.SharedFileOps.ModExtensionsText;
        SharedFileOps.PrefixHashBytesText = resolved.SharedFileOps.PrefixHashBytesText;
        SharedFileOps.HashWorkerCountText = resolved.SharedFileOps.HashWorkerCountText;
        IsToolkitLogDrawerOpen = resolved.UiState.ToolkitLogDrawerOpen;
        IsTrayPreviewLogDrawerOpen = resolved.UiState.TrayPreviewLogDrawerOpen;
        IsToolkitAdvancedOpen = resolved.UiState.ToolkitAdvancedOpen;
        IsTrayPreviewAdvancedOpen = resolved.UiState.TrayPreviewAdvancedOpen;

        _settingsProjection.LoadModuleSettings(settings, _moduleRegistry);
        SelectedAction = resolved.SelectedAction;
        Workspace = resolved.Workspace;

        ScriptPath = ResolveFixedScriptPath();
        if (!File.Exists(ScriptPath))
        {
            StatusMessage = LF("status.scriptNotFound", ScriptPath);
        }

        ClearTrayPreview();
        if (File.Exists(ScriptPath))
        {
            StatusMessage = L("status.ready");
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
        var settings = _settingsProjection.Capture(
            new MainWindowSettingsSnapshot
            {
                UiLanguageCode = SelectedLanguageCode,
                ScriptPath = ScriptPath,
                Workspace = Workspace,
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
                UiState = new AppSettings.UiStateSettings
                {
                    ToolkitLogDrawerOpen = IsToolkitLogDrawerOpen,
                    TrayPreviewLogDrawerOpen = IsTrayPreviewLogDrawerOpen,
                    ToolkitAdvancedOpen = IsToolkitAdvancedOpen,
                    TrayPreviewAdvancedOpen = IsTrayPreviewAdvancedOpen
                }
            },
            _moduleRegistry);
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
                if (!_planBuilder.TryBuildToolkitCliPlan(CreatePlanBuilderState(), out _, out _, out var error))
                {
                    HasValidationErrors = true;
                    ValidationSummaryText = LF("validation.failed", error);
                    return;
                }

                HasValidationErrors = false;
                ValidationSummaryText = LF("validation.okToolkit", module.DisplayName);
                return;
            }

            if (!_planBuilder.TryBuildTrayPreviewInput(CreatePlanBuilderState(), out _, out var trayPreviewError))
            {
                HasValidationErrors = true;
                ValidationSummaryText = LF("validation.failed", trayPreviewError);
                return;
            }

            HasValidationErrors = false;
            ValidationSummaryText = L("validation.okTrayPreview");
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
            ReportUnsupportedBrowseTarget("folder", target);
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
            default:
                ReportUnsupportedBrowseTarget("folder", target);
                break;
        }
    }

    private async Task BrowseCsvPathAsync(string? target)
    {
        if (!string.Equals(target, "FindDupOutputCsv", StringComparison.Ordinal))
        {
            ReportUnsupportedBrowseTarget("csv", target);
            return;
        }

        var path = await _fileDialogService.PickCsvSavePathAsync("Select OutputCsv path", "finddup-duplicates.csv");
        if (!string.IsNullOrWhiteSpace(path))
        {
            FindDup.OutputCsv = path;
        }
    }

    private void ReportUnsupportedBrowseTarget(string kind, string? target)
    {
        var normalizedTarget = string.IsNullOrWhiteSpace(target) ? "<empty>" : target.Trim();
        StatusMessage = LF("status.unsupportedBrowseTarget", kind, normalizedTarget);
        AppendLog($"[ui] unsupported {kind} browse target: {normalizedTarget}");
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

    private async Task<bool> ConfirmDangerousFindDupCleanupAsync()
    {
        if (SelectedAction != SimsAction.FindDuplicates || !FindDup.Cleanup || WhatIf)
        {
            return true;
        }

        var confirmed = await _confirmationDialogService.ConfirmAsync(new ConfirmationRequest
        {
            Title = L("dialog.danger.title"),
            Message = L("dialog.danger.message"),
            ConfirmText = L("dialog.danger.confirm"),
            CancelText = L("dialog.danger.cancel"),
            IsDangerous = true
        });

        if (!confirmed)
        {
            StatusMessage = L("status.dangerCancelled");
            AppendLog("[cancel] cleanup confirmation rejected");
            return false;
        }

        return true;
    }

    private async Task RunToolkitAsync()
    {
        if (_executionCts is not null)
        {
            StatusMessage = L("status.executionAlreadyRunning");
            return;
        }

        if (!_planBuilder.TryBuildToolkitCliPlan(CreatePlanBuilderState(), out _, out var cliPlan, out var error))
        {
            StatusMessage = error;
            AppendLog("[validation] " + error);
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
        SetProgress(isIndeterminate: true, percent: 0, message: L("progress.starting"));
        AppendLog("[start] " + startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        AppendLog("[action] " + input.Action.ToString().ToLowerInvariant());
        StatusMessage = L("status.running");

        try
        {
            var runResult = await _toolkitExecutionRunner.RunAsync(
                cliPlan,
                onOutput: AppendLog,
                onProgress: HandleProgress,
                cancellationToken: _executionCts.Token);
            stopwatch.Stop();

            if (runResult.Status == ExecutionRunStatus.Success)
            {
                var result = runResult.ExecutionResult!;
                AppendLog($"[exit] code={result.ExitCode}");
                StatusMessage = result.ExitCode == 0
                    ? LF("status.executionCompleted", stopwatch.Elapsed.ToString("mm\\:ss"))
                    : LF("status.executionFailedExit", result.ExitCode, stopwatch.Elapsed.ToString("mm\\:ss"));
                SetProgress(
                    isIndeterminate: false,
                    percent: result.ExitCode == 0 ? 100 : 0,
                    message: result.ExitCode == 0 ? L("progress.completed") : L("progress.failed"));
            }
            else if (runResult.Status == ExecutionRunStatus.Cancelled)
            {
                AppendLog("[cancelled]");
                StatusMessage = L("status.executionCancelled");
                SetProgress(isIndeterminate: false, percent: 0, message: L("progress.cancelled"));
            }
            else
            {
                var errorMessage = string.IsNullOrWhiteSpace(runResult.ErrorMessage)
                    ? L("status.unknownExecutionError")
                    : runResult.ErrorMessage;
                AppendLog("[error] " + errorMessage);
                StatusMessage = L("status.executionFailed");
                SetProgress(isIndeterminate: false, percent: 0, message: L("progress.executionFailed"));
            }
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
            StatusMessage = L("status.executionAlreadyRunning");
            return;
        }

        TrayPreviewInput input;
        if (explicitInput is null)
        {
            if (!_planBuilder.TryBuildTrayPreviewInput(CreatePlanBuilderState(), out var built, out var validationError))
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

        _trayPreviewRunner.Reset();
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
            var runResult = await _trayPreviewRunner.LoadDashboardAsync(input, _executionCts.Token);
            stopwatch.Stop();

            if (runResult.Status == ExecutionRunStatus.Success)
            {
                var result = runResult.LoadResult!;
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
                    LF("status.trayPreviewLoaded", result.Dashboard.TotalItems, result.Page.TotalPages, stopwatch.Elapsed.ToString("mm\\:ss"));
                SetProgress(isIndeterminate: false, percent: 100, message: L("progress.trayLoaded"));
            }
            else if (runResult.Status == ExecutionRunStatus.Cancelled)
            {
                AppendLog("[cancelled]");
                StatusMessage = L("status.trayPreviewCancelled");
                SetProgress(isIndeterminate: false, percent: 0, message: L("progress.cancelled"));
            }
            else
            {
                var errorMessage = string.IsNullOrWhiteSpace(runResult.ErrorMessage)
                    ? L("status.unknownTrayPreviewError")
                    : runResult.ErrorMessage;
                AppendLog("[error] " + errorMessage);
                StatusMessage = L("status.trayPreviewFailed");
                SetProgress(isIndeterminate: false, percent: 0, message: L("progress.trayFailed"));
            }
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
            StatusMessage = L("status.trayPageLoadingAlready");
            return;
        }

        SetTrayPreviewPageLoading(true);
        try
        {
            var runResult = await _trayPreviewRunner.LoadPageAsync(requestedPageIndex);
            if (runResult.Status == ExecutionRunStatus.Success)
            {
                var result = runResult.PageResult!;
                SetTrayPreviewPage(result.Page, result.LoadedPageCount);
                StatusMessage = LF("status.trayPageLoaded", result.Page.PageIndex, result.Page.TotalPages);
                return;
            }

            if (runResult.Status == ExecutionRunStatus.Cancelled)
            {
                StatusMessage = L("status.trayPageCancelled");
                return;
            }

            var errorMessage = string.IsNullOrWhiteSpace(runResult.ErrorMessage)
                ? L("status.unknownTrayPreviewPageError")
                : runResult.ErrorMessage;
            AppendLog("[error] " + errorMessage);
            StatusMessage = L("status.trayPageFailed");
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

        if (!_planBuilder.TryBuildTrayPreviewInput(CreatePlanBuilderState(), out var input, out _))
        {
            return;
        }

        if (_trayPreviewRunner.TryGetCached(input, out var cached))
        {
            SetTrayPreviewDashboard(cached.Dashboard);
            SetTrayPreviewPage(cached.Page, cached.LoadedPageCount);
            StatusMessage = LF("status.trayPageLoaded", cached.Page.PageIndex, cached.Page.TotalPages);
            return;
        }

        await RunTrayPreviewAsync(input);
    }

    private void CancelExecution()
    {
        var cts = _executionCts;
        if (cts is null)
        {
            StatusMessage = L("status.noRunningExecution");
            return;
        }

        AppendLog("[cancel] requested");
        StatusMessage = L("status.cancelling");
        SetProgress(isIndeterminate: true, percent: 0, message: L("status.cancelling"));

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignore cancellation race when operation has already completed.
        }
    }

    private MainWindowPlanBuilderState CreatePlanBuilderState()
    {
        return new MainWindowPlanBuilderState
        {
            ScriptPath = ScriptPath,
            WhatIf = WhatIf,
            SelectedAction = SelectedAction,
            SharedFileOps = new SharedFileOpsPlanState
            {
                SkipPruneEmptyDirs = SharedFileOps.SkipPruneEmptyDirs,
                ModFilesOnly = SharedFileOps.ModFilesOnly,
                VerifyContentOnNameConflict = SharedFileOps.VerifyContentOnNameConflict,
                ModExtensionsText = SharedFileOps.ModExtensionsText,
                PrefixHashBytesText = SharedFileOps.PrefixHashBytesText,
                HashWorkerCountText = SharedFileOps.HashWorkerCountText
            }
        };
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
            PreviewSummaryText = L("preview.noneLoaded");
            PreviewDashboardTotalItems = "0";
            PreviewDashboardTotalFiles = "0";
            PreviewDashboardTotalSize = "0 MB";
            PreviewDashboardLatestWrite = "-";
            PreviewPageText = LF("preview.page", 0, 0);
            PreviewLazyLoadText = LF("preview.lazyCache", 0, 0);
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
                ? L("preview.typeNa")
                : LF("preview.type", dashboard.PresetTypeBreakdown);
            PreviewSummaryText = LF("preview.dashboardReady", breakdown);
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
            var safeTotalPages = Math.Max(page.TotalPages, 1);
            PreviewSummaryText = LF("preview.range", firstItemIndex, lastItemIndex, page.TotalItems);
            PreviewPageText = LF("preview.page", page.PageIndex, safeTotalPages);
            PreviewLazyLoadText = LF("preview.lazyCache", loadedPageCount, safeTotalPages);
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

    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(ILocalizationService.AvailableLanguages), StringComparison.Ordinal) &&
            !string.Equals(e.PropertyName, nameof(ILocalizationService.CurrentLanguageCode), StringComparison.Ordinal) &&
            !string.Equals(e.PropertyName, "Item[]", StringComparison.Ordinal))
        {
            return;
        }

        ExecuteOnUi(() =>
        {
            var normalized = _localization.CurrentLanguageCode;
            if (!string.Equals(_selectedLanguageCode, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _selectedLanguageCode = normalized;
                OnPropertyChanged(nameof(SelectedLanguageCode));
            }

            NotifyLocalizationDependentProperties();
        });
    }

    private void NotifyLocalizationDependentProperties()
    {
        OnPropertyChanged(nameof(AvailableLanguages));
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(IsChineseTranslationTodo));
        OnPropertyChanged(nameof(SettingsTitleText));
        OnPropertyChanged(nameof(SettingsLowFrequencySectionTitleText));
        OnPropertyChanged(nameof(SettingsLowFrequencySectionHintText));
        OnPropertyChanged(nameof(SettingsLanguageLabelText));
        OnPropertyChanged(nameof(SettingsLanguageTodoHintText));
        OnPropertyChanged(nameof(SettingsPrefixHashBytesLabelText));
        OnPropertyChanged(nameof(SettingsHashWorkerCountLabelText));
        OnPropertyChanged(nameof(SettingsPerformanceHintText));
    }

    private string L(string key) => _localization[key];

    private string LF(string key, params object[] args) => _localization.Format(key, args);

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
