
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.Infrastructure.Localization;
using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.Models;
using SimsModDesktop.Services;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.ViewModels.Infrastructure;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private const string DefaultLanguageCode = "en-US";

    private readonly IToolkitExecutionRunner _toolkitExecutionRunner;
    private readonly ITrayPreviewRunner _trayPreviewRunner;
    private readonly ITrayThumbnailService _trayThumbnailService;
    private readonly ITrayDependencyExportService _trayDependencyExportService;
    private readonly ITrayDependencyAnalysisService _trayDependencyAnalysisService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ILocalizationService _localization;
    private readonly ISettingsStore _settingsStore;
    private readonly IMainWindowSettingsProjection _settingsProjection;
    private readonly IActionModuleRegistry _moduleRegistry;
    private readonly IMainWindowPlanBuilder _planBuilder;
    private readonly Stack<TrayPreviewListItemViewModel> _trayPreviewDetailHistory = new();
    private readonly HashSet<string> _selectedTrayPreviewKeys = new(StringComparer.OrdinalIgnoreCase);

    private readonly StringWriter _logWriter = new();
    private CancellationTokenSource? _executionCts;
    private CancellationTokenSource? _validationDebounceCts;
    private CancellationTokenSource? _settingsPersistDebounceCts;
    private CancellationTokenSource? _trayPreviewAutoLoadDebounceCts;
    private CancellationTokenSource? _trayPreviewThumbnailCts;
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
    private TrayPreviewListItemViewModel? _trayPreviewDetailItem;
    private bool _hasTrayPreviewLoadedOnce;
    private string _validationSummaryText = string.Empty;
    private bool _hasValidationErrors;
    private bool _isToolkitLogDrawerOpen;
    private bool _isTrayPreviewLogDrawerOpen;
    private bool _isToolkitAdvancedOpen;
    private bool _isInitialized;
    private bool _isTrayExportQueueExpanded = true;
    private int _trayPreviewThumbnailBatchId;
    private string? _trayPreviewSelectionAnchorKey;

    public MainWindowViewModel(
        IToolkitExecutionRunner toolkitExecutionRunner,
        ITrayPreviewRunner trayPreviewRunner,
        ITrayDependencyExportService trayDependencyExportService,
        ITrayDependencyAnalysisService trayDependencyAnalysisService,
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
        ModPreviewPanelViewModel modPreview,
        TrayPreviewPanelViewModel trayPreview,
        SharedFileOpsPanelViewModel sharedFileOps,
        ITrayThumbnailService? trayThumbnailService = null)
    {
        _toolkitExecutionRunner = toolkitExecutionRunner;
        _trayPreviewRunner = trayPreviewRunner;
        _trayDependencyExportService = trayDependencyExportService;
        _trayDependencyAnalysisService = trayDependencyAnalysisService;
        _trayThumbnailService = trayThumbnailService ?? NullTrayThumbnailService.Instance;
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
        ModPreview = modPreview;
        TrayPreview = trayPreview;
        SharedFileOps = sharedFileOps;
        PreviewItems = new ObservableCollection<TrayPreviewListItemViewModel>();
        PreviewItems.CollectionChanged += OnPreviewItemsChanged;
        TrayExportTasks = new ObservableCollection<TrayExportTaskItemViewModel>();
        TrayExportTasks.CollectionChanged += OnTrayExportTasksChanged;

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
        OpenSelectedTrayPreviewPathsCommand = new RelayCommand(OpenSelectedTrayPreviewPaths, () => HasSelectedTrayPreviewItems);
        SelectAllTrayPreviewPageCommand = new RelayCommand(SelectAllTrayPreviewPage, () => HasTrayPreviewItems);
        ClearTrayPreviewSelectionCommand = new RelayCommand(ClearTrayPreviewSelection, () => HasSelectedTrayPreviewItems);
        ExportSelectedTrayPreviewFilesCommand = new AsyncRelayCommand(ExportSelectedTrayPreviewFilesAsync, () => HasSelectedTrayPreviewItems);
        ClearCompletedTrayExportTasksCommand = new RelayCommand(ClearCompletedTrayExportTasks, () => HasCompletedTrayExportTasks);
        ToggleTrayExportQueueCommand = new RelayCommand(ToggleTrayExportQueue, () => HasTrayExportTasks);
        OpenTrayExportTaskPathCommand = new RelayCommand<TrayExportTaskItemViewModel>(OpenTrayExportTaskPath, task => task?.HasExportRoot == true);
        ToggleTrayExportTaskDetailsCommand = new RelayCommand<TrayExportTaskItemViewModel>(ToggleTrayExportTaskDetails, task => task?.HasDetails == true);
        RunActiveWorkspaceCommand = new AsyncRelayCommand(RunActiveWorkspaceAsync, () => !IsBusy && !IsModPreviewWorkspace);
        CancelCommand = new RelayCommand(CancelExecution, () => IsBusy);
        PreviewPrevPageCommand = new AsyncRelayCommand(LoadPreviousTrayPreviewPageAsync, () => CanGoPrevPage);
        PreviewNextPageCommand = new AsyncRelayCommand(LoadNextTrayPreviewPageAsync, () => CanGoNextPage);
        PreviewJumpPageCommand = new AsyncRelayCommand(JumpToTrayPreviewPageAsync, () => CanJumpToPage);
        GoBackTrayPreviewDetailCommand = new RelayCommand(GoBackTrayPreviewDetails, () => CanGoBackTrayPreviewDetail);
        CloseTrayPreviewDetailCommand = new RelayCommand(CloseTrayPreviewDetails, () => IsTrayPreviewDetailVisible);
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
    public ModPreviewPanelViewModel ModPreview { get; }
    public TrayPreviewPanelViewModel TrayPreview { get; }
    public SharedFileOpsPanelViewModel SharedFileOps { get; }
    public ObservableCollection<TrayPreviewListItemViewModel> PreviewItems { get; }
    public ObservableCollection<TrayExportTaskItemViewModel> TrayExportTasks { get; }

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
    public IReadOnlyList<string> TrayPreviewLayoutModeOptions => ["Entry", "Grid"];
    public IReadOnlyList<string> ModPreviewPackageTypeFilterOptions => ["All", ".package", ".ts4script", "Override", "Script Mod"];
    public IReadOnlyList<string> ModPreviewScopeFilterOptions => ["All", "Build/Buy", "CAS", "Gameplay"];
    public IReadOnlyList<string> ModPreviewSortOptions => ["Last Updated", "Name", "Size"];

    public AsyncRelayCommand<string> BrowseFolderCommand { get; }
    public AsyncRelayCommand<string> BrowseCsvPathCommand { get; }
    public RelayCommand SwitchToToolkitWorkspaceCommand { get; }
    public RelayCommand SwitchToTrayPreviewWorkspaceCommand { get; }
    public RelayCommand<MergeSourcePathEntryViewModel> AddMergeSourcePathCommand { get; }
    public AsyncRelayCommand<MergeSourcePathEntryViewModel> BrowseMergeSourcePathCommand { get; }
    public RelayCommand<MergeSourcePathEntryViewModel> RemoveMergeSourcePathCommand { get; }
    public AsyncRelayCommand RunCommand { get; }
    public AsyncRelayCommand RunTrayPreviewCommand { get; }
    public RelayCommand OpenSelectedTrayPreviewPathsCommand { get; }
    public RelayCommand SelectAllTrayPreviewPageCommand { get; }
    public RelayCommand ClearTrayPreviewSelectionCommand { get; }
    public AsyncRelayCommand ExportSelectedTrayPreviewFilesCommand { get; }
    public RelayCommand ClearCompletedTrayExportTasksCommand { get; }
    public RelayCommand ToggleTrayExportQueueCommand { get; }
    public RelayCommand<TrayExportTaskItemViewModel> OpenTrayExportTaskPathCommand { get; }
    public RelayCommand<TrayExportTaskItemViewModel> ToggleTrayExportTaskDetailsCommand { get; }
    public AsyncRelayCommand RunActiveWorkspaceCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand PreviewPrevPageCommand { get; }
    public AsyncRelayCommand PreviewNextPageCommand { get; }
    public AsyncRelayCommand PreviewJumpPageCommand { get; }
    public RelayCommand GoBackTrayPreviewDetailCommand { get; }
    public RelayCommand CloseTrayPreviewDetailCommand { get; }
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
            OnPropertyChanged(nameof(IsModPreviewWorkspace));
            OnPropertyChanged(nameof(IsTrayPreviewWorkspace));
            OnPropertyChanged(nameof(IsSharedFileOpsVisible));
            StatusMessage = L("status.ready");
            NotifyCommandStates();
            QueueValidationRefresh();

            if (value != AppWorkspace.TrayPreview)
            {
                CloseTrayPreviewDetails();
                CancelTrayPreviewThumbnailLoading();
            }

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
        private set
        {
            if (!SetProperty(ref _previewTotalItems, value))
            {
                return;
            }

            OnPropertyChanged(nameof(TrayPreviewSelectionSummaryText));
        }
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
    public bool IsModPreviewWorkspace => Workspace == AppWorkspace.ModPreview;
    public bool IsTrayPreviewWorkspace => Workspace == AppWorkspace.TrayPreview;
    public bool IsSharedFileOpsVisible =>
        IsToolkitWorkspace &&
        _moduleRegistry.All.Any(module => module.Action == SelectedAction && module.UsesSharedFileOps);

    public bool HasValidModPreviewPath =>
        !string.IsNullOrWhiteSpace(ModPreview.ModsRoot) &&
        Directory.Exists(ModPreview.ModsRoot);

    public string ModPreviewPathHintText => HasValidModPreviewPath
        ? "Mods Path comes from Settings."
        : "Set a valid Mods Path in Settings before building preview.";

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
    public bool HasSelectedTrayPreviewItems => _selectedTrayPreviewKeys.Count > 0;
    public int SelectedTrayPreviewCount => _selectedTrayPreviewKeys.Count;
    public string TrayPreviewSelectionSummaryText => $"{SelectedTrayPreviewCount} selected / {PreviewItems.Count} on page / {PreviewTotalItems} total";
    public bool HasTrayExportTasks => TrayExportTasks.Count > 0;
    public bool HasCompletedTrayExportTasks => TrayExportTasks.Any(item => item.IsCompleted);
    public bool HasRunningTrayExportTasks => TrayExportTasks.Any(item => item.IsRunning);
    public bool IsTrayExportQueueDockVisible => IsTrayPreviewWorkspace && HasTrayExportTasks;
    public bool IsTrayExportQueueVisible => IsTrayExportQueueDockVisible && _isTrayExportQueueExpanded;
    public string TrayExportQueueSummaryText =>
        HasTrayExportTasks
            ? $"{TrayExportTasks.Count(item => item.IsRunning)} running / {TrayExportTasks.Count(item => item.IsCompleted)} finished"
            : "No export tasks";
    public string TrayExportQueueToggleText => IsTrayExportQueueVisible
        ? "Hide Tasks"
        : $"Show Tasks ({TrayExportTasks.Count})";
    public bool IsTrayPreviewLoadingStateVisible => IsBusy && !HasTrayPreviewItems;
    public bool IsTrayPreviewEmptyStateVisible => !IsBusy && !HasTrayPreviewItems;
    public bool IsTrayPreviewPagerVisible => HasTrayPreviewItems;
    public bool IsTrayPreviewEntryMode => !string.Equals(TrayPreview.LayoutMode, "Grid", StringComparison.OrdinalIgnoreCase);
    public bool IsTrayPreviewGridMode => string.Equals(TrayPreview.LayoutMode, "Grid", StringComparison.OrdinalIgnoreCase);
    public TrayPreviewListItemViewModel? TrayPreviewDetailItem
    {
        get => _trayPreviewDetailItem;
        private set
        {
            if (!SetProperty(ref _trayPreviewDetailItem, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsTrayPreviewDetailVisible));
            OnPropertyChanged(nameof(IsTrayPreviewDetailDescriptionEmpty));
            OnPropertyChanged(nameof(IsTrayPreviewDetailOverviewEmpty));
            OnPropertyChanged(nameof(CanGoBackTrayPreviewDetail));
            GoBackTrayPreviewDetailCommand.NotifyCanExecuteChanged();
            CloseTrayPreviewDetailCommand.NotifyCanExecuteChanged();
        }
    }
    public bool IsTrayPreviewDetailVisible => TrayPreviewDetailItem is not null;
    public bool IsTrayPreviewDetailDescriptionEmpty => TrayPreviewDetailItem is null || !TrayPreviewDetailItem.Item.HasDisplayDescription;
    public bool IsTrayPreviewDetailOverviewEmpty =>
        TrayPreviewDetailItem is null ||
        (!TrayPreviewDetailItem.Item.HasDisplayPrimaryMeta &&
         !TrayPreviewDetailItem.Item.HasDisplaySecondaryMeta &&
         !TrayPreviewDetailItem.Item.HasDisplayTertiaryMeta);
    public bool CanGoBackTrayPreviewDetail => _trayPreviewDetailHistory.Count > 0;
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
        ModPreview.ModsRoot = resolved.ModPreview.ModsRoot;
        ModPreview.PackageTypeFilter = resolved.ModPreview.PackageTypeFilter;
        ModPreview.ScopeFilter = resolved.ModPreview.ScopeFilter;
        ModPreview.SortBy = resolved.ModPreview.SortBy;
        ModPreview.SearchQuery = resolved.ModPreview.SearchQuery;
        ModPreview.ShowOverridesOnly = resolved.ModPreview.ShowOverridesOnly;
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
        _settingsPersistDebounceCts?.Cancel();
        _trayPreviewAutoLoadDebounceCts?.Cancel();
        CancelTrayPreviewThumbnailLoading();
        await SaveCurrentSettingsAsync();
    }

    private void HookValidationTracking()
    {
        SubscribeForValidation(Organize);
        SubscribeForValidation(Flatten);
        SubscribeForValidation(Normalize);
        SubscribeForValidation(FindDup);
        SubscribeForValidation(TrayDependencies);
        SubscribeForValidation(ModPreview);
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

        if (ReferenceEquals(sender, TrayPreview) &&
            string.Equals(e.PropertyName, nameof(TrayPreviewPanelViewModel.EnableDebugPreview), StringComparison.Ordinal))
        {
            ApplyTrayPreviewDebugVisibility();

            if (_isInitialized)
            {
                QueueSettingsPersist();
            }
        }

        if (ReferenceEquals(sender, TrayPreview) &&
            string.Equals(e.PropertyName, nameof(TrayPreviewPanelViewModel.LayoutMode), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(IsTrayPreviewEntryMode));
            OnPropertyChanged(nameof(IsTrayPreviewGridMode));

            if (_isInitialized)
            {
                QueueSettingsPersist();
            }
        }

        if (ReferenceEquals(sender, ModPreview))
        {
            OnPropertyChanged(nameof(HasValidModPreviewPath));
            OnPropertyChanged(nameof(ModPreviewPathHintText));
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

    private void QueueSettingsPersist()
    {
        _settingsPersistDebounceCts?.Cancel();
        _settingsPersistDebounceCts?.Dispose();
        _settingsPersistDebounceCts = new CancellationTokenSource();
        var cancellationToken = _settingsPersistDebounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cancellationToken);
                await SaveCurrentSettingsAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }, cancellationToken);
    }

    private async Task SaveCurrentSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);

        settings.UiLanguageCode = string.IsNullOrWhiteSpace(SelectedLanguageCode)
            ? DefaultLanguageCode
            : SelectedLanguageCode.Trim();
        settings.ScriptPath = ScriptPath;
        settings.SelectedWorkspace = Workspace;
        settings.SelectedAction = SelectedAction;
        settings.WhatIf = WhatIf;
        settings.SharedFileOps = new AppSettings.SharedFileOpsSettings
        {
            SkipPruneEmptyDirs = SharedFileOps.SkipPruneEmptyDirs,
            ModFilesOnly = SharedFileOps.ModFilesOnly,
            VerifyContentOnNameConflict = SharedFileOps.VerifyContentOnNameConflict,
            ModExtensionsText = SharedFileOps.ModExtensionsText,
            PrefixHashBytesText = SharedFileOps.PrefixHashBytesText,
            HashWorkerCountText = SharedFileOps.HashWorkerCountText
        };
        settings.UiState = new AppSettings.UiStateSettings
        {
            ToolkitLogDrawerOpen = IsToolkitLogDrawerOpen,
            TrayPreviewLogDrawerOpen = IsTrayPreviewLogDrawerOpen,
            ToolkitAdvancedOpen = IsToolkitAdvancedOpen
        };
        settings.ModPreview = new AppSettings.ModPreviewSettings
        {
            ModsRoot = ModPreview.ModsRoot,
            PackageTypeFilter = ModPreview.PackageTypeFilter,
            ScopeFilter = ModPreview.ScopeFilter,
            SortBy = ModPreview.SortBy,
            SearchQuery = ModPreview.SearchQuery,
            ShowOverridesOnly = ModPreview.ShowOverridesOnly
        };

        foreach (var module in _moduleRegistry.All)
        {
            module.SaveToSettings(settings);
        }

        await _settingsStore.SaveAsync(settings, cancellationToken);
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
                if (SelectedAction == SimsAction.TrayDependencies)
                {
                    if (!_planBuilder.TryBuildTrayDependenciesPlan(CreatePlanBuilderState(), out _, out var trayDependencyError))
                    {
                        HasValidationErrors = true;
                        ValidationSummaryText = LF("validation.failed", trayDependencyError);
                        return;
                    }
                }
                else if (!_planBuilder.TryBuildToolkitCliPlan(CreatePlanBuilderState(), out _, out _, out var error))
                {
                    HasValidationErrors = true;
                    ValidationSummaryText = LF("validation.failed", error);
                    return;
                }

                HasValidationErrors = false;
                ValidationSummaryText = LF("validation.okToolkit", module.DisplayName);
                return;
            }

            if (IsModPreviewWorkspace)
            {
                HasValidationErrors = false;
                ValidationSummaryText = HasValidModPreviewPath
                    ? "Mod preview scaffold is ready."
                    : "Set a valid Mods Path in Settings to prepare the mod preview scaffold.";
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
        OpenSelectedTrayPreviewPathsCommand.NotifyCanExecuteChanged();
        SelectAllTrayPreviewPageCommand.NotifyCanExecuteChanged();
        ClearTrayPreviewSelectionCommand.NotifyCanExecuteChanged();
        ExportSelectedTrayPreviewFilesCommand.NotifyCanExecuteChanged();
        ClearCompletedTrayExportTasksCommand.NotifyCanExecuteChanged();
        ToggleTrayExportQueueCommand.NotifyCanExecuteChanged();
        OpenTrayExportTaskPathCommand.NotifyCanExecuteChanged();
        ToggleTrayExportTaskDetailsCommand.NotifyCanExecuteChanged();
        RunActiveWorkspaceCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        PreviewPrevPageCommand.NotifyCanExecuteChanged();
        PreviewNextPageCommand.NotifyCanExecuteChanged();
        PreviewJumpPageCommand.NotifyCanExecuteChanged();
        ToggleToolkitAdvancedCommand.NotifyCanExecuteChanged();
        ClearLogCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasValidModPreviewPath));
        OnPropertyChanged(nameof(ModPreviewPathHintText));
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

        if (IsTrayPreviewWorkspace)
        {
            await RunTrayPreviewAsync();
        }
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

        if (SelectedAction == SimsAction.TrayDependencies)
        {
            await RunTrayDependenciesAsync();
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

    private async Task RunTrayDependenciesAsync()
    {
        if (_executionCts is not null)
        {
            StatusMessage = L("status.executionAlreadyRunning");
            return;
        }

        if (!_planBuilder.TryBuildTrayDependenciesPlan(CreatePlanBuilderState(), out var plan, out var error))
        {
            StatusMessage = error;
            AppendLog("[validation] " + error);
            await ShowErrorPopupAsync(L("status.validationFailed"));
            return;
        }

        _executionCts = new CancellationTokenSource();
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

        ClearLog();
        IsBusy = true;
        SetProgress(isIndeterminate: true, percent: 0, message: "Preparing tray dependency analysis...");
        AppendLog("[start] " + startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        AppendLog("[action] traydependencies");
        StatusMessage = L("status.running");

        try
        {
            var result = await _trayDependencyAnalysisService.AnalyzeAsync(
                plan.Request,
                new Progress<TrayDependencyAnalysisProgress>(HandleTrayDependencyAnalysisProgress),
                _executionCts.Token);
            stopwatch.Stop();

            AppendLog($"[traydependencies] matched={result.MatchedPackageCount}");
            AppendLog($"[traydependencies] unused={result.UnusedPackageCount}");

            if (!string.IsNullOrWhiteSpace(result.OutputCsvPath))
            {
                AppendLog("CSV: " + result.OutputCsvPath);
            }

            if (!string.IsNullOrWhiteSpace(result.UnusedOutputCsvPath))
            {
                AppendLog("UNUSED CSV: " + result.UnusedOutputCsvPath);
            }

            if (!string.IsNullOrWhiteSpace(result.MatchedExportPath))
            {
                AppendLog("[export] matched=" + result.MatchedExportPath);
            }

            if (!string.IsNullOrWhiteSpace(result.UnusedExportPath))
            {
                AppendLog("[export] unused=" + result.UnusedExportPath);
            }

            foreach (var row in result.MatchedPackages.Take(10))
            {
                AppendLog(
                    $"[match] {Path.GetFileName(row.PackagePath)} confidence={row.Confidence} count={row.MatchInstanceCount} rate={row.MatchRatePct:0.##}%");
            }

            foreach (var issue in result.Issues)
            {
                var prefix = issue.Severity == TrayDependencyIssueSeverity.Error ? "[error]" : "[warn]";
                AppendLog(prefix + " " + issue.Message);
            }

            if (result.Success)
            {
                var hasWarnings = result.Issues.Any(issue => issue.Severity == TrayDependencyIssueSeverity.Warning);
                StatusMessage = hasWarnings
                    ? $"Tray dependencies completed with warnings ({stopwatch.Elapsed:mm\\:ss})"
                    : $"Tray dependencies completed ({stopwatch.Elapsed:mm\\:ss})";
                SetProgress(
                    isIndeterminate: false,
                    percent: 100,
                    message: hasWarnings ? "Tray dependency analysis completed with warnings." : "Tray dependency analysis completed.");
                return;
            }

            StatusMessage = "Tray dependency analysis failed.";
            SetProgress(isIndeterminate: false, percent: 0, message: "Tray dependency analysis failed.");
            await ShowErrorPopupAsync("Tray dependency analysis failed.");
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            AppendLog("[cancelled]");
            StatusMessage = L("status.executionCancelled");
            SetProgress(isIndeterminate: false, percent: 0, message: L("progress.cancelled"));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AppendLog("[error] " + ex.Message);
            StatusMessage = "Tray dependency analysis failed.";
            SetProgress(isIndeterminate: false, percent: 0, message: "Tray dependency analysis failed.");
            await ShowErrorPopupAsync("Tray dependency analysis failed.");
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

    private void HandleTrayDependencyAnalysisProgress(TrayDependencyAnalysisProgress progress)
    {
        var message = string.IsNullOrWhiteSpace(progress.Detail)
            ? progress.Stage.ToString()
            : progress.Detail;
        SetProgress(
            isIndeterminate: progress.Percent <= 0,
            percent: Math.Clamp(progress.Percent, 0, 100),
            message: message);
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

        CancelTrayPreviewThumbnailLoading();
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
            CloseTrayPreviewDetails();
            CancelTrayPreviewThumbnailLoading();
            ClearTrayPreviewSelection();
            ClearPreviewItems();
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

            _hasTrayPreviewLoadedOnce = true;
            _trayPreviewCurrentPage = page.PageIndex;
            _trayPreviewTotalPages = Math.Max(page.TotalPages, 1);
            PreviewTotalItems = page.TotalItems.ToString("N0");
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
        ArgumentNullException.ThrowIfNull(selectedItem);

        if (!PreviewItems.Contains(selectedItem))
        {
            return;
        }

        if (shiftPressed)
        {
            ApplyTrayPreviewRangeSelection(selectedItem, preserveExisting: controlPressed);
            _trayPreviewSelectionAnchorKey = BuildTrayPreviewSelectionKey(selectedItem.Item);
            return;
        }

        var targetSelected = !selectedItem.IsSelected;
        SetTrayPreviewItemSelected(selectedItem, targetSelected);
        _trayPreviewSelectionAnchorKey = BuildTrayPreviewSelectionKey(selectedItem.Item);
    }

    private void OpenTrayPreviewDetails(TrayPreviewListItemViewModel selectedItem)
    {
        ArgumentNullException.ThrowIfNull(selectedItem);

        if (TrayPreviewDetailItem is null)
        {
            _trayPreviewDetailHistory.Clear();
        }
        else if (!ReferenceEquals(TrayPreviewDetailItem, selectedItem))
        {
            _trayPreviewDetailHistory.Push(TrayPreviewDetailItem);
        }

        TrayPreviewDetailItem = selectedItem;
        LoadTrayPreviewChildThumbnails(selectedItem);
    }

    private void GoBackTrayPreviewDetails()
    {
        if (_trayPreviewDetailHistory.Count == 0)
        {
            return;
        }

        var previousItem = _trayPreviewDetailHistory.Pop();
        TrayPreviewDetailItem = previousItem;
        LoadTrayPreviewChildThumbnails(previousItem);
    }

    private void CloseTrayPreviewDetails()
    {
        _trayPreviewDetailHistory.Clear();
        TrayPreviewDetailItem = null;
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
            _trayPreviewCurrentPage,
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

    private void OpenSelectedTrayPreviewPaths()
    {
        var sourcePaths = GetSelectedTrayPreviewSourceFilePaths();
        if (sourcePaths.Count == 0)
        {
            return;
        }

        try
        {
            if (sourcePaths.Count == 1)
            {
                LaunchExplorer(sourcePaths[0], selectFile: true);
                StatusMessage = "Opened selected tray file location.";
                AppendLog($"[tray-selection] opened path={sourcePaths[0]}");
                return;
            }

            var directories = sourcePaths
                .Select(path => Path.GetDirectoryName(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (directories.Count == 0)
            {
                return;
            }

            foreach (var directory in directories)
            {
                LaunchExplorer(directory!, selectFile: false);
            }

            StatusMessage = directories.Count == 1
                ? "Opened selected tray directory."
                : $"Opened {directories.Count} directories for selected tray files.";
            AppendLog($"[tray-selection] opened-directories count={directories.Count}");
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to open selected tray path.";
            AppendLog("[tray-selection] open failed: " + ex.Message);
        }
    }

    private async Task ExportSelectedTrayPreviewFilesAsync()
    {
        var selectedItems = GetSelectedTrayPreviewItems();
        if (selectedItems.Count == 0)
        {
            return;
        }

        var pickedFolders = await _fileDialogService.PickFolderPathsAsync("Select export folder", allowMultiple: false);
        var exportRoot = pickedFolders.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(exportRoot))
        {
            return;
        }

        if (!TryBuildTrayDependencyExportRequests(selectedItems, exportRoot, out var dependencyRequests, out var error))
        {
            var setupTask = EnqueueTrayExportTask("Export setup");
            setupTask.SetExportRoot(exportRoot);
            setupTask.MarkFailed(error);
            StatusMessage = error;
            AppendLog("[tray-selection] export blocked: " + error);
            return;
        }

        var queueEntries = dependencyRequests
            .Select(request => (
                request.Item,
                request.ItemExportRoot,
                request.Request,
                Task: EnqueueTrayExportTask(request.Item.Item.DisplayTitle)))
            .ToArray();
        var copiedTrayFileCount = 0;
        var copiedModFileCount = 0;
        var warningCount = 0;
        var createdItemRoots = new List<string>(dependencyRequests.Count);

        foreach (var queueEntry in queueEntries)
        {
            var exportTask = queueEntry.Task;
            exportTask.SetExportRoot(queueEntry.ItemExportRoot);
            exportTask.MarkTrayRunning();

            createdItemRoots.Add(queueEntry.ItemExportRoot);

            var movedToModsStage = false;
            TrayDependencyExportResult result;
            try
            {
                result = await _trayDependencyExportService.ExportAsync(
                    queueEntry.Request,
                    new Progress<TrayDependencyExportProgress>(progress =>
                    {
                        if (!movedToModsStage && progress.Stage != TrayDependencyExportStage.Preparing)
                        {
                            exportTask.MarkTrayCompleted(0, skippedCount: 0);
                            exportTask.MarkModsRunning();
                            movedToModsStage = true;
                        }

                        exportTask.UpdateModsProgress(
                            progress.Percent,
                            string.IsNullOrWhiteSpace(progress.Detail)
                                ? $"Exporting referenced mods... {progress.Percent}%"
                                : progress.Detail);
                    }),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                exportTask.MarkFailed("Mods export failed: " + ex.Message);
                exportTask.AppendDetailLine(ex.ToString());
                MarkTrayExportBatchFailed(queueEntries.Select(entry => entry.Task).ToArray(), exportTask, "Rolled back after batch failure.");
                await Task.Run(() => RollbackTraySelectionExports(createdItemRoots));
                StatusMessage = "Export failed: " + ex.Message;
                AppendLog($"[tray-selection][internal] export failed for trayKey={queueEntry.Request.TrayItemKey}: {ex.Message}");
                return;
            }

            exportTask.MarkTrayCompleted(result.CopiedTrayFileCount, skippedCount: 0);
            if (!movedToModsStage)
            {
                exportTask.MarkModsRunning();
            }

            foreach (var issue in result.Issues)
            {
                exportTask.AppendDetailLine(issue.Message);
                AppendLog($"[tray-selection][internal] {issue.Severity}: {issue.Message}");
            }

            if (!result.Success)
            {
                var failure = result.Issues.FirstOrDefault(issue => issue.Severity == TrayDependencyIssueSeverity.Error)?.Message
                              ?? "Unknown error.";
                exportTask.MarkFailed("Mods export failed: " + failure);
                MarkTrayExportBatchFailed(queueEntries.Select(entry => entry.Task).ToArray(), exportTask, "Rolled back after batch failure.");
                await Task.Run(() => RollbackTraySelectionExports(createdItemRoots));
                StatusMessage = "Export failed: " + failure;
                AppendLog($"[tray-selection][internal] export failed for trayKey={queueEntry.Request.TrayItemKey}: {failure}");
                return;
            }

            copiedTrayFileCount += result.CopiedTrayFileCount;
            copiedModFileCount += result.CopiedModFileCount;
            if (result.HasMissingReferenceWarnings)
            {
                warningCount++;
                exportTask.MarkCompleted("Completed (missing references ignored).", failed: false);
                continue;
            }

            exportTask.MarkCompleted("Completed.", failed: false);
        }

        StatusMessage = warningCount == 0
            ? $"Exported {copiedTrayFileCount} tray files and {copiedModFileCount} referenced mod files."
            : $"Exported {copiedTrayFileCount} tray files and {copiedModFileCount} referenced mod files ({warningCount} warning item(s) ignored).";

        AppendLog($"[tray-selection] export tray={copiedTrayFileCount} mods={copiedModFileCount} items={dependencyRequests.Count} warnings={warningCount} target={exportRoot}");
    }

    private void SelectAllTrayPreviewPage()
    {
        if (PreviewItems.Count == 0)
        {
            return;
        }

        foreach (var item in PreviewItems)
        {
            SetTrayPreviewItemSelected(item, true);
        }

        _trayPreviewSelectionAnchorKey = BuildTrayPreviewSelectionKey(PreviewItems[^1].Item);
    }

    private IReadOnlyList<TrayPreviewListItemViewModel> GetSelectedTrayPreviewItems()
    {
        return PreviewItems
            .Where(item => item.IsSelected)
            .ToArray();
    }

    private IReadOnlyList<string> GetSelectedTrayPreviewSourceFilePaths(IReadOnlyCollection<TrayPreviewListItemViewModel>? selectedItems = null)
    {
        var source = selectedItems ?? GetSelectedTrayPreviewItems();
        return source
            .SelectMany(item => item.Item.SourceFilePaths)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private TrayExportTaskItemViewModel EnqueueTrayExportTask(string? title)
    {
        var task = new TrayExportTaskItemViewModel(
            string.IsNullOrWhiteSpace(title)
                ? "Tray Export"
                : title.Trim());
        TrayExportTasks.Add(task);
        return task;
    }

    private static void MarkTrayExportBatchFailed(
        IReadOnlyList<TrayExportTaskItemViewModel> queueEntries,
        TrayExportTaskItemViewModel failedTask,
        string rollbackStatus)
    {
        foreach (var queueTask in queueEntries)
        {
            if (ReferenceEquals(queueTask, failedTask))
            {
                continue;
            }

            if (queueTask.IsCompleted && !queueTask.IsFailed)
            {
                queueTask.MarkFailed(rollbackStatus);
                continue;
            }

            if (queueTask.IsRunning)
            {
                queueTask.MarkFailed("Cancelled after batch failure.");
            }
        }
    }

    private static void AppendTrayExportTaskDetails(
        TrayExportTaskItemViewModel task,
        IEnumerable<string> outputLines)
    {
        foreach (var line in outputLines)
        {
            task.AppendDetailLine(line);
        }
    }

    private static bool TryExportTraySelectionFiles(
        IReadOnlyList<string> sourceFilePaths,
        string exportRoot,
        out int exportedCount,
        out string error)
    {
        exportedCount = 0;
        error = string.Empty;

        var distinctSourcePaths = sourceFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctSourcePaths.Length == 0)
        {
            error = "Selected tray item has no source files to export.";
            return false;
        }

        foreach (var sourcePath in distinctSourcePaths)
        {
            if (!File.Exists(sourcePath))
            {
                error = $"Tray source file is missing: {Path.GetFileName(sourcePath)}";
                return false;
            }

            try
            {
                var destinationPath = BuildUniqueExportPath(exportRoot, sourcePath);
                File.Copy(sourcePath, destinationPath, overwrite: false);
                exportedCount++;
            }
            catch (Exception ex)
            {
                error = $"Failed to export tray file {Path.GetFileName(sourcePath)}: {ex.Message}";
                return false;
            }
        }

        return true;
    }

    private static bool TryCompleteTrayDependencyExport(
        ToolkitRunResult runResult,
        IReadOnlyList<string> outputLines,
        out bool ignoredMissingOnly,
        out string error)
    {
        ignoredMissingOnly = false;
        error = string.Empty;

        if (runResult.Status == ExecutionRunStatus.Cancelled)
        {
            error = "Referenced mod export was cancelled.";
            return false;
        }

        if (runResult.Status == ExecutionRunStatus.Failed)
        {
            if (IsIgnorableMissingModFileFailure(runResult.ErrorMessage, outputLines))
            {
                ignoredMissingOnly = true;
                return true;
            }

            error = string.IsNullOrWhiteSpace(runResult.ErrorMessage)
                ? "Referenced mod export failed."
                : runResult.ErrorMessage.Trim();
            return false;
        }

        var exitCode = runResult.ExecutionResult?.ExitCode ?? 0;
        if (exitCode == 0)
        {
            return true;
        }

        if (IsIgnorableMissingModFileFailure(runResult.ErrorMessage, outputLines))
        {
            ignoredMissingOnly = true;
            return true;
        }

        var detail = outputLines
            .LastOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?.Trim();
        error = string.IsNullOrWhiteSpace(detail)
            ? $"Referenced mod export failed with exit code {exitCode}."
            : $"Referenced mod export failed with exit code {exitCode}: {detail}";
        return false;
    }

    private static bool IsIgnorableMissingModFileFailure(string errorMessage, IReadOnlyList<string> outputLines)
    {
        if (ContainsNonIgnorableModExportFailure(errorMessage))
        {
            return false;
        }

        if (ContainsIgnorableMissingModFileMessage(errorMessage))
        {
            return true;
        }

        var hasIgnorableMissingFileMessage = false;
        foreach (var line in outputLines)
        {
            if (ContainsNonIgnorableModExportFailure(line))
            {
                return false;
            }

            if (ContainsIgnorableMissingModFileMessage(line))
            {
                hasIgnorableMissingFileMessage = true;
            }
        }

        return hasIgnorableMissingFileMessage;
    }

    private static bool ContainsIgnorableMissingModFileMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = message.Trim().ToLowerInvariant();
        var missingHint =
            normalized.Contains("missing", StringComparison.Ordinal) ||
            normalized.Contains("not found", StringComparison.Ordinal) ||
            normalized.Contains("could not find", StringComparison.Ordinal) ||
            normalized.Contains("cannot find", StringComparison.Ordinal);
        if (!missingHint)
        {
            return false;
        }

        if (normalized.Contains("script path", StringComparison.Ordinal) ||
            normalized.Contains("mods path", StringComparison.Ordinal) ||
            normalized.Contains("s4ti path", StringComparison.Ordinal))
        {
            return false;
        }

        return normalized.Contains(".package", StringComparison.Ordinal) ||
               normalized.Contains("package file", StringComparison.Ordinal) ||
               normalized.Contains("package files", StringComparison.Ordinal) ||
               normalized.Contains("matched package", StringComparison.Ordinal) ||
               normalized.Contains("mod file", StringComparison.Ordinal) ||
               normalized.Contains("mod files", StringComparison.Ordinal);
    }

    private static bool ContainsNonIgnorableModExportFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var normalized = message.Trim().ToLowerInvariant();
        return normalized.Contains("access denied", StringComparison.Ordinal) ||
               normalized.Contains("permission", StringComparison.Ordinal) ||
               normalized.Contains("failed", StringComparison.Ordinal) ||
               normalized.Contains("exception", StringComparison.Ordinal) ||
               normalized.Contains("script path", StringComparison.Ordinal) ||
               normalized.Contains("mods path", StringComparison.Ordinal) ||
               normalized.Contains("s4ti path", StringComparison.Ordinal);
    }

    private static void RollbackTraySelectionExports(IEnumerable<string> exportRoots)
    {
        foreach (var exportRoot in exportRoots
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(exportRoot))
                {
                    Directory.Delete(exportRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort rollback only.
            }
        }
    }

    private bool TryBuildTrayDependencyExportRequests(
        IReadOnlyList<TrayPreviewListItemViewModel> selectedItems,
        string exportRoot,
        out List<(TrayPreviewListItemViewModel Item, string ItemExportRoot, TrayDependencyExportRequest Request)> requests,
        out string error)
    {
        requests = new List<(TrayPreviewListItemViewModel Item, string ItemExportRoot, TrayDependencyExportRequest Request)>();
        error = string.Empty;

        var modsPath = TrayDependencies.ModsPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(modsPath) || !Directory.Exists(modsPath))
        {
            error = "Mods Path is missing. Set a valid Mods Path before exporting referenced mods.";
            return false;
        }

        var usedDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var selectedItem in selectedItems)
        {
            var trayKey = selectedItem.Item.TrayItemKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trayKey))
            {
                error = "A selected tray item is missing TrayItemKey, cannot export referenced mods.";
                requests.Clear();
                return false;
            }

            var trayPath = selectedItem.Item.TrayRootPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trayPath) || !Directory.Exists(trayPath))
            {
                error = $"Tray path is missing for selected tray item {trayKey}.";
                requests.Clear();
                return false;
            }

            if (selectedItem.Item.SourceFilePaths.Count == 0)
            {
                error = $"Selected tray item {trayKey} has no source files to export.";
                requests.Clear();
                return false;
            }

            var exportDirectoryName = BuildTraySelectionExportDirectoryName(selectedItem.Item, usedDirectoryNames);
            var itemExportRoot = Path.Combine(exportRoot, exportDirectoryName);
            var trayExportRoot = Path.Combine(itemExportRoot, "Tray");
            var modsExportRoot = Path.Combine(itemExportRoot, "Mods");

            requests.Add((selectedItem, itemExportRoot, new TrayDependencyExportRequest
            {
                ItemTitle = selectedItem.Item.DisplayTitle,
                TrayItemKey = trayKey,
                TrayRootPath = trayPath,
                TraySourceFiles = selectedItem.Item.SourceFilePaths.ToArray(),
                ModsRootPath = modsPath,
                TrayExportRoot = trayExportRoot,
                ModsExportRoot = modsExportRoot
            }));
        }

        return true;
    }

    private void ApplyTrayPreviewRangeSelection(TrayPreviewListItemViewModel selectedItem, bool preserveExisting)
    {
        var targetIndex = PreviewItems.IndexOf(selectedItem);
        if (targetIndex < 0)
        {
            return;
        }

        var anchorIndex = -1;
        if (!string.IsNullOrWhiteSpace(_trayPreviewSelectionAnchorKey))
        {
            anchorIndex = PreviewItems
                .Select((item, index) => new { item, index })
                .Where(entry => string.Equals(
                    BuildTrayPreviewSelectionKey(entry.item.Item),
                    _trayPreviewSelectionAnchorKey,
                    StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.index)
                .DefaultIfEmpty(-1)
                .First();
        }

        if (anchorIndex < 0)
        {
            if (!preserveExisting)
            {
                ClearTrayPreviewSelection();
            }

            SetTrayPreviewItemSelected(selectedItem, true);
            return;
        }

        if (!preserveExisting)
        {
            ClearTrayPreviewSelection();
        }

        var startIndex = Math.Min(anchorIndex, targetIndex);
        var endIndex = Math.Max(anchorIndex, targetIndex);
        for (var index = startIndex; index <= endIndex; index++)
        {
            SetTrayPreviewItemSelected(PreviewItems[index], true);
        }
    }

    private void ClearTrayPreviewSelection()
    {
        foreach (var item in PreviewItems.Where(item => item.IsSelected))
        {
            item.SetSelected(false);
        }

        if (_selectedTrayPreviewKeys.Count == 0 && string.IsNullOrWhiteSpace(_trayPreviewSelectionAnchorKey))
        {
            return;
        }

        _selectedTrayPreviewKeys.Clear();
        _trayPreviewSelectionAnchorKey = null;
        OnPropertyChanged(nameof(HasSelectedTrayPreviewItems));
        OnPropertyChanged(nameof(SelectedTrayPreviewCount));
        OnPropertyChanged(nameof(TrayPreviewSelectionSummaryText));
        NotifyCommandStates();
    }

    private void SetTrayPreviewItemSelected(TrayPreviewListItemViewModel item, bool selected)
    {
        var key = BuildTrayPreviewSelectionKey(item.Item);
        var changed = selected
            ? _selectedTrayPreviewKeys.Add(key)
            : _selectedTrayPreviewKeys.Remove(key);

        item.SetSelected(selected);

        if (changed)
        {
            OnPropertyChanged(nameof(HasSelectedTrayPreviewItems));
            OnPropertyChanged(nameof(SelectedTrayPreviewCount));
            OnPropertyChanged(nameof(TrayPreviewSelectionSummaryText));
            NotifyCommandStates();
        }
    }

    private bool IsTrayPreviewItemSelected(SimsTrayPreviewItem item)
    {
        return _selectedTrayPreviewKeys.Contains(BuildTrayPreviewSelectionKey(item));
    }

    private static string BuildTrayPreviewSelectionKey(SimsTrayPreviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return $"{item.TrayRootPath}|{item.TrayItemKey}";
    }

    private static void LaunchExplorer(string path, bool selectFile)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var arguments = selectFile
            ? $"/select,\"{path}\""
            : $"\"{path}\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    private static string BuildUniqueExportPath(string exportRoot, string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var destinationPath = Path.Combine(exportRoot, fileName);
        if (!File.Exists(destinationPath))
        {
            return destinationPath;
        }

        for (var suffix = 2; ; suffix++)
        {
            destinationPath = Path.Combine(exportRoot, $"{baseName} ({suffix}){extension}");
            if (!File.Exists(destinationPath))
            {
                return destinationPath;
            }
        }
    }

    private static string BuildTraySelectionExportDirectoryName(SimsTrayPreviewItem item, ISet<string> usedDirectoryNames)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(usedDirectoryNames);

        var rawLabel = !string.IsNullOrWhiteSpace(item.DisplayTitle)
            ? item.DisplayTitle
            : !string.IsNullOrWhiteSpace(item.ItemName)
                ? item.ItemName
                : item.PresetType;
        var safeLabel = SanitizePathSegment(rawLabel);
        var safeKey = SanitizePathSegment(item.TrayItemKey);
        var baseName = string.IsNullOrWhiteSpace(safeLabel)
            ? safeKey
            : $"{safeLabel}_{safeKey}";
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "TrayItem";
        }

        var candidate = baseName;
        var suffix = 2;
        while (!usedDirectoryNames.Add(candidate))
        {
            candidate = $"{baseName}_{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static string SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (invalidChars.Contains(character))
            {
                builder.Append('_');
            }
            else if (char.IsWhiteSpace(character))
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder
            .ToString()
            .Trim()
            .TrimEnd('.');
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

    private void OnTrayExportTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is { Count: > 0 })
        {
            SetTrayExportQueueExpanded(true);
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<TrayExportTaskItemViewModel>())
            {
                item.PropertyChanged -= OnTrayExportTaskPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<TrayExportTaskItemViewModel>())
            {
                item.PropertyChanged += OnTrayExportTaskPropertyChanged;
            }
        }

        NotifyTrayExportQueueChanged();
    }

    private void OnTrayExportTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyTrayExportQueueChanged();
    }

    private void NotifyTrayPreviewFilterVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsBuildSizeFilterVisible));
        OnPropertyChanged(nameof(IsHouseholdSizeFilterVisible));
    }

    private void NotifyTrayPreviewViewStateChanged()
    {
        OnPropertyChanged(nameof(HasTrayPreviewItems));
        OnPropertyChanged(nameof(TrayPreviewSelectionSummaryText));
        OnPropertyChanged(nameof(IsTrayExportQueueDockVisible));
        OnPropertyChanged(nameof(IsTrayExportQueueVisible));
        OnPropertyChanged(nameof(TrayExportQueueToggleText));
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

    private void NotifyTrayExportQueueChanged()
    {
        OnPropertyChanged(nameof(HasTrayExportTasks));
        OnPropertyChanged(nameof(HasCompletedTrayExportTasks));
        OnPropertyChanged(nameof(HasRunningTrayExportTasks));
        OnPropertyChanged(nameof(IsTrayExportQueueDockVisible));
        OnPropertyChanged(nameof(IsTrayExportQueueVisible));
        OnPropertyChanged(nameof(TrayExportQueueSummaryText));
        OnPropertyChanged(nameof(TrayExportQueueToggleText));
        ClearCompletedTrayExportTasksCommand.NotifyCanExecuteChanged();
        ToggleTrayExportQueueCommand.NotifyCanExecuteChanged();
        OpenTrayExportTaskPathCommand.NotifyCanExecuteChanged();
        ToggleTrayExportTaskDetailsCommand.NotifyCanExecuteChanged();
    }

    private void ClearCompletedTrayExportTasks()
    {
        for (var index = TrayExportTasks.Count - 1; index >= 0; index--)
        {
            if (TrayExportTasks[index].IsCompleted)
            {
                TrayExportTasks.RemoveAt(index);
            }
        }
    }

    private void ToggleTrayExportQueue()
    {
        if (!HasTrayExportTasks)
        {
            return;
        }

        SetTrayExportQueueExpanded(!_isTrayExportQueueExpanded);
    }

    private void OpenTrayExportTaskPath(TrayExportTaskItemViewModel? task)
    {
        if (task is null || !task.HasExportRoot)
        {
            return;
        }

        try
        {
            LaunchExplorer(task.ExportRootPath, selectFile: false);
        }
        catch (Exception ex)
        {
            task.AppendDetailLine("Failed to open export folder: " + ex.Message);
            StatusMessage = "Failed to open export folder.";
        }
    }

    private void ToggleTrayExportTaskDetails(TrayExportTaskItemViewModel? task)
    {
        task?.ToggleDetails();
    }

    private void SetTrayExportQueueExpanded(bool expanded)
    {
        if (_isTrayExportQueueExpanded == expanded)
        {
            return;
        }

        _isTrayExportQueueExpanded = expanded;
        OnPropertyChanged(nameof(IsTrayExportQueueVisible));
        OnPropertyChanged(nameof(TrayExportQueueToggleText));
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

