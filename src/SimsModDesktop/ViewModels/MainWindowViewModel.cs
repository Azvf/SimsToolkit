
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
    private CancellationTokenSource? _trayPreviewAutoLoadDebounceCts;
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
    private string _previewTotalItems = "0";
    private string _previewTotalFiles = "0";
    private string _previewTotalSize = "0 MB";
    private string _previewLatestWrite = "-";
    private string _previewPageText = "Page 0/0";
    private string _previewLazyLoadText = "Lazy cache 0/0 pages";
    private string _previewJumpPageText = string.Empty;
    private bool _hasTrayPreviewLoadedOnce;
    private string _validationSummaryText = string.Empty;
    private bool _hasValidationErrors;
    private bool _isToolkitLogDrawerOpen;
    private bool _isTrayPreviewLogDrawerOpen;
    private bool _isToolkitAdvancedOpen;
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
        PreviewItems.CollectionChanged += OnPreviewItemsChanged;

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
        RunCommand = new AsyncRelayCommand(RunToolkitAsync, () => !IsBusy && IsToolkitWorkspace);
        RunTrayPreviewCommand = new AsyncRelayCommand(
            () => RunTrayPreviewAsync(),
            () => !IsBusy && IsTrayPreviewWorkspace && HasValidTrayPreviewPath);
        RunActiveWorkspaceCommand = new AsyncRelayCommand(RunActiveWorkspaceAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(CancelExecution, () => IsBusy);
        PreviewPrevPageCommand = new AsyncRelayCommand(LoadPreviousTrayPreviewPageAsync, () => CanGoPrevPage);
        PreviewNextPageCommand = new AsyncRelayCommand(LoadNextTrayPreviewPageAsync, () => CanGoNextPage);
        PreviewJumpPageCommand = new AsyncRelayCommand(JumpToTrayPreviewPageAsync, () => CanJumpToPage);
        ToggleToolkitAdvancedCommand = new RelayCommand(() => IsToolkitAdvancedOpen = !IsToolkitAdvancedOpen, () => IsToolkitWorkspace);
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
    public IReadOnlyList<string> TrayPreviewPresetTypeFilterOptions => ["All", "Lot", "Room", "Household"];
    public IReadOnlyList<string> TrayPreviewBuildSizeFilterOptions => ["All", "15 x 20", "20 x 20", "30 x 20", "30 x 30", "40 x 30", "40 x 40", "50 x 40", "50 x 50", "64 x 64"];
    public IReadOnlyList<string> TrayPreviewHouseholdSizeFilterOptions => ["All", "1", "2", "3", "4", "5", "6", "7", "8"];
    public IReadOnlyList<string> TrayPreviewTimeFilterOptions => ["All", "Last24h", "Last7d", "Last30d", "Last90d"];

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
    public AsyncRelayCommand PreviewJumpPageCommand { get; }
    public RelayCommand ToggleToolkitAdvancedCommand { get; }
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

    public string PreviewTotalItems
    {
        get => _previewTotalItems;
        private set => SetProperty(ref _previewTotalItems, value);
    }

    public string PreviewTotalFiles
    {
        get => _previewTotalFiles;
        private set => SetProperty(ref _previewTotalFiles, value);
    }

    public string PreviewTotalSize
    {
        get => _previewTotalSize;
        private set => SetProperty(ref _previewTotalSize, value);
    }

    public string PreviewLatestWrite
    {
        get => _previewLatestWrite;
        private set => SetProperty(ref _previewLatestWrite, value);
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

    public string PreviewJumpPageText
    {
        get => _previewJumpPageText;
        set
        {
            if (!SetProperty(ref _previewJumpPageText, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanJumpToPage));
            PreviewJumpPageCommand.NotifyCanExecuteChanged();
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

    public bool HasValidTrayPreviewPath =>
        !string.IsNullOrWhiteSpace(TrayPreview.TrayRoot) &&
        Directory.Exists(TrayPreview.TrayRoot);

    public string TrayPreviewPathHintText => HasValidTrayPreviewPath
        ? "Tray Path comes from Settings."
        : "Set a valid Tray Path in Settings before loading preview.";

    public bool CanGoPrevPage => !IsBusy && !_isTrayPreviewPageLoading && _trayPreviewCurrentPage > 1;
    public bool CanGoNextPage => !IsBusy && !_isTrayPreviewPageLoading && _trayPreviewCurrentPage < _trayPreviewTotalPages;
    public bool CanJumpToPage => !IsBusy && !_isTrayPreviewPageLoading && TryParsePreviewJumpPage(PreviewJumpPageText, out var page) && page >= 1 && page <= _trayPreviewTotalPages;
    public bool IsBuildSizeFilterVisible =>
        string.Equals(TrayPreview.PresetTypeFilter, "Lot", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(TrayPreview.PresetTypeFilter, "Room", StringComparison.OrdinalIgnoreCase);
    public bool IsHouseholdSizeFilterVisible =>
        string.Equals(TrayPreview.PresetTypeFilter, "Household", StringComparison.OrdinalIgnoreCase);
    public bool HasTrayPreviewItems => PreviewItems.Count > 0;
    public bool IsTrayPreviewLoadingStateVisible => IsBusy && !HasTrayPreviewItems;
    public bool IsTrayPreviewEmptyStateVisible => !IsBusy && !HasTrayPreviewItems;
    public bool IsTrayPreviewPagerVisible => HasTrayPreviewItems;
    public bool IsTrayPreviewEmptyStatusOk => HasValidTrayPreviewPath && !_hasTrayPreviewLoadedOnce;
    public bool IsTrayPreviewEmptyStatusWarning => HasValidTrayPreviewPath && _hasTrayPreviewLoadedOnce;
    public bool IsTrayPreviewEmptyStatusMissing => !HasValidTrayPreviewPath;
    public bool IsTrayPreviewPathMissing => !HasValidTrayPreviewPath;

    public string TrayPreviewEmptyTitleText
    {
        get
        {
            if (!HasValidTrayPreviewPath)
            {
                return L("preview.empty.pathMissing.title");
            }

            if (_hasTrayPreviewLoadedOnce)
            {
                return L("preview.empty.noResults.title");
            }

            return L("preview.empty.initial.title");
        }
    }

    public string TrayPreviewEmptyDescriptionText
    {
        get
        {
            if (!HasValidTrayPreviewPath)
            {
                return L("preview.empty.pathMissing.description");
            }

            if (_hasTrayPreviewLoadedOnce)
            {
                return L("preview.empty.noResults.description");
            }

            return L("preview.empty.initial.description");
        }
    }

    public string TrayPreviewEmptyStatusText
    {
        get
        {
            if (!HasValidTrayPreviewPath)
            {
                return L("preview.empty.status.pathMissing");
            }

            if (_hasTrayPreviewLoadedOnce)
            {
                return L("preview.empty.status.noResults");
            }

            return L("preview.empty.status.ready");
        }
    }

    public string TrayPreviewLoadingText => L("status.trayPreviewLoading");

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
        _trayPreviewAutoLoadDebounceCts?.Cancel();
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
                    ToolkitAdvancedOpen = IsToolkitAdvancedOpen
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
        source.PropertyChanged += OnPanelPropertyChanged;
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        QueueValidationRefresh();

        if (ReferenceEquals(sender, TrayPreview) &&
            string.Equals(e.PropertyName, nameof(TrayPreviewPanelViewModel.PresetTypeFilter), StringComparison.Ordinal))
        {
            if (IsBuildSizeFilterVisible && !string.Equals(TrayPreview.HouseholdSizeFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                TrayPreview.HouseholdSizeFilter = "All";
            }
            else if (IsHouseholdSizeFilterVisible && !string.Equals(TrayPreview.BuildSizeFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                TrayPreview.BuildSizeFilter = "All";
            }
            else if (!IsBuildSizeFilterVisible &&
                     !IsHouseholdSizeFilterVisible &&
                     (!string.Equals(TrayPreview.BuildSizeFilter, "All", StringComparison.OrdinalIgnoreCase) ||
                      !string.Equals(TrayPreview.HouseholdSizeFilter, "All", StringComparison.OrdinalIgnoreCase)))
            {
                TrayPreview.BuildSizeFilter = "All";
                TrayPreview.HouseholdSizeFilter = "All";
            }

            NotifyTrayPreviewFilterVisibilityChanged();
        }

        if (!ReferenceEquals(sender, TrayPreview) || !IsTrayPreviewAutoReloadProperty(e.PropertyName))
        {
            return;
        }

        if (!HasValidTrayPreviewPath)
        {
            ClearTrayPreview();
            return;
        }

        if (IsTrayPreviewWorkspace)
        {
            QueueTrayPreviewAutoLoad();
        }
    }

    private static bool IsTrayPreviewAutoReloadProperty(string? propertyName)
    {
        return string.Equals(propertyName, nameof(TrayPreviewPanelViewModel.TrayRoot), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayPreviewPanelViewModel.PresetTypeFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayPreviewPanelViewModel.BuildSizeFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayPreviewPanelViewModel.HouseholdSizeFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayPreviewPanelViewModel.AuthorFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayPreviewPanelViewModel.TimeFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayPreviewPanelViewModel.SearchQuery), StringComparison.Ordinal);
    }

    private void QueueTrayPreviewAutoLoad()
    {
        _trayPreviewAutoLoadDebounceCts?.Cancel();
        _trayPreviewAutoLoadDebounceCts?.Dispose();
        _trayPreviewAutoLoadDebounceCts = new CancellationTokenSource();
        var cancellationToken = _trayPreviewAutoLoadDebounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested || !IsTrayPreviewWorkspace || !HasValidTrayPreviewPath)
            {
                return;
            }

            ExecuteOnUi(() => _ = TryAutoLoadTrayPreviewAsync());
        }, cancellationToken);
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
        PreviewJumpPageCommand.NotifyCanExecuteChanged();
        ToggleToolkitAdvancedCommand.NotifyCanExecuteChanged();
        ClearLogCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasValidTrayPreviewPath));
        OnPropertyChanged(nameof(TrayPreviewPathHintText));
        OnPropertyChanged(nameof(CanGoPrevPage));
        OnPropertyChanged(nameof(CanGoNextPage));
        OnPropertyChanged(nameof(CanJumpToPage));
        NotifyTrayPreviewViewStateChanged();
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
            case "ProbeS4tiPath":
                await BrowseSingleFolderAsync("Select S4TI Install Folder", path => TrayDependencies.S4tiPath = path);
                break;
            case "ProbeExportTargetPath":
                await BrowseSingleFolderAsync("Select ExportTargetPath", path => TrayDependencies.ExportTargetPath = path);
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

    private async Task ShowErrorPopupAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            await _confirmationDialogService.ConfirmAsync(new ConfirmationRequest
            {
                Title = L("dialog.error.title"),
                Message = $"{message}{Environment.NewLine}{L("dialog.error.checkLog")}",
                ConfirmText = L("dialog.error.confirm"),
                ShowCancel = false
            });
        }
        catch (Exception ex)
        {
            AppendLog("[ui] failed to show error dialog: " + ex.Message);
        }
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
            await ShowErrorPopupAsync(L("status.validationFailed"));
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

                if (result.ExitCode != 0)
                {
                    await ShowErrorPopupAsync(L("status.executionFailed"));
                }
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
                await ShowErrorPopupAsync(L("status.executionFailed"));
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
                await ShowErrorPopupAsync(L("status.validationFailed"));
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
            var runResult = await _trayPreviewRunner.LoadPreviewAsync(input, _executionCts.Token);
            stopwatch.Stop();

            if (runResult.Status == ExecutionRunStatus.Success)
            {
                var result = runResult.LoadResult!;
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
                await ShowErrorPopupAsync(L("status.trayPreviewFailed"));
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

        if (!_planBuilder.TryBuildTrayPreviewInput(CreatePlanBuilderState(), out var input, out _))
        {
            return;
        }

        if (_trayPreviewRunner.TryGetCached(input, out var cached))
        {
            SetTrayPreviewSummary(cached.Summary);
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
            PreviewTotalItems = "0";
            PreviewTotalFiles = "0";
            PreviewTotalSize = "0 MB";
            PreviewLatestWrite = "-";
            PreviewPageText = LF("preview.page", 0, 0);
            PreviewLazyLoadText = LF("preview.lazyCache", 0, 0);
            PreviewJumpPageText = string.Empty;
            _hasTrayPreviewLoadedOnce = false;
            _trayPreviewCurrentPage = 1;
            _trayPreviewTotalPages = 1;
            NotifyTrayPreviewViewStateChanged();
            NotifyCommandStates();
        });
    }

    private void SetTrayPreviewSummary(SimsTrayPreviewSummary summary)
    {
        ExecuteOnUi(() =>
        {
            PreviewTotalItems = summary.TotalItems.ToString("N0");
            PreviewTotalFiles = summary.TotalFiles.ToString("N0");
            PreviewTotalSize = $"{summary.TotalMB:N2} MB";
            PreviewLatestWrite = summary.LatestWriteTimeLocal == DateTime.MinValue
                ? "-"
                : summary.LatestWriteTimeLocal.ToString("yyyy-MM-dd HH:mm");

            var breakdown = string.IsNullOrWhiteSpace(summary.PresetTypeBreakdown)
                ? L("preview.typeNa")
                : LF("preview.type", summary.PresetTypeBreakdown);
            PreviewSummaryText = LF("preview.summaryReady", breakdown);
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

            _hasTrayPreviewLoadedOnce = true;
            _trayPreviewCurrentPage = page.PageIndex;
            _trayPreviewTotalPages = Math.Max(page.TotalPages, 1);
            var firstItemIndex = page.Items.Count == 0 ? 0 : ((page.PageIndex - 1) * page.PageSize) + 1;
            var lastItemIndex = page.Items.Count == 0 ? 0 : firstItemIndex + page.Items.Count - 1;
            var safeTotalPages = Math.Max(page.TotalPages, 1);
            PreviewSummaryText = LF("preview.range", firstItemIndex, lastItemIndex, page.TotalItems);
            PreviewPageText = LF("preview.page", page.PageIndex, safeTotalPages);
            PreviewLazyLoadText = LF("preview.lazyCache", loadedPageCount, safeTotalPages);
            PreviewJumpPageText = page.PageIndex.ToString();
            NotifyTrayPreviewViewStateChanged();
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
        OnPropertyChanged(nameof(TrayPreviewBuildSizeFilterOptions));
        OnPropertyChanged(nameof(TrayPreviewHouseholdSizeFilterOptions));
        OnPropertyChanged(nameof(TrayPreviewEmptyTitleText));
        OnPropertyChanged(nameof(TrayPreviewEmptyDescriptionText));
        OnPropertyChanged(nameof(TrayPreviewEmptyStatusText));
        OnPropertyChanged(nameof(TrayPreviewLoadingText));
    }

    private void OnPreviewItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotifyTrayPreviewViewStateChanged();
    }

    private void NotifyTrayPreviewFilterVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsBuildSizeFilterVisible));
        OnPropertyChanged(nameof(IsHouseholdSizeFilterVisible));
    }

    private void NotifyTrayPreviewViewStateChanged()
    {
        OnPropertyChanged(nameof(HasTrayPreviewItems));
        OnPropertyChanged(nameof(IsTrayPreviewLoadingStateVisible));
        OnPropertyChanged(nameof(IsTrayPreviewEmptyStateVisible));
        OnPropertyChanged(nameof(IsTrayPreviewPagerVisible));
        OnPropertyChanged(nameof(IsTrayPreviewEmptyStatusOk));
        OnPropertyChanged(nameof(IsTrayPreviewEmptyStatusWarning));
        OnPropertyChanged(nameof(IsTrayPreviewEmptyStatusMissing));
        OnPropertyChanged(nameof(IsTrayPreviewPathMissing));
        OnPropertyChanged(nameof(TrayPreviewEmptyTitleText));
        OnPropertyChanged(nameof(TrayPreviewEmptyDescriptionText));
        OnPropertyChanged(nameof(TrayPreviewEmptyStatusText));
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

