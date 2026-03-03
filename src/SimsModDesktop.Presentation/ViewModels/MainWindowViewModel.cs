
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Localization;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Results;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Application.TextureProcessing;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.Presentation.Dialogs;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.Presentation.ViewModels.Infrastructure;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private const string DefaultLanguageCode = "en-US";
    private readonly IExecutionCoordinator _executionCoordinator;
    private readonly ITrayPreviewCoordinator _trayPreviewCoordinator;
    private readonly ITrayThumbnailService _trayThumbnailService;
    private readonly ITrayDependencyExportService _trayDependencyExportService;
    private readonly ITrayDependencyAnalysisService _trayDependencyAnalysisService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ILocalizationService _localization;
    private readonly MainWindowSettingsPersistenceController _settingsPersistenceController;
    private readonly IMainWindowSettingsProjection _settingsProjection;
    private readonly IToolkitActionPlanner _toolkitActionPlanner;
    private readonly MainWindowRecoveryController _recoveryController;
    private readonly MainWindowTrayPreviewStateController _trayPreviewStateController;
    private readonly MainWindowTrayPreviewSelectionController _trayPreviewSelectionController;
    private readonly ITextureCompressionService _textureCompressionService;
    private readonly ITextureDimensionProbe _textureDimensionProbe;

    private readonly MainWindowStatusController _statusController;
    private CancellationTokenSource? _executionCts;
    private CancellationTokenSource? _validationDebounceCts;
    private CancellationTokenSource? _trayPreviewThumbnailCts;
    private bool _isTrayPreviewPageLoading;
    private bool _isBusy;
    private AppWorkspace _workspace = AppWorkspace.Toolkit;
    private SimsAction _selectedAction = SimsAction.Organize;
    private string _selectedLanguageCode = DefaultLanguageCode;
    private string _scriptPath = string.Empty;
    private bool _whatIf;
    private string _validationSummaryText = string.Empty;
    private bool _hasValidationErrors;
    private bool _isToolkitLogDrawerOpen;
    private bool _isTrayPreviewLogDrawerOpen;
    private bool _isToolkitAdvancedOpen;
    private bool _isInitialized;
    private bool _isTrayExportQueueExpanded = true;
    private int _trayPreviewThumbnailBatchId;

    public MainWindowViewModel(
        IExecutionCoordinator executionCoordinator,
        ITrayPreviewCoordinator trayPreviewCoordinator,
        ITrayDependencyExportService trayDependencyExportService,
        ITrayDependencyAnalysisService trayDependencyAnalysisService,
        IFileDialogService fileDialogService,
        IConfirmationDialogService confirmationDialogService,
        ILocalizationService localization,
        MainWindowSettingsPersistenceController settingsPersistenceController,
        IMainWindowSettingsProjection settingsProjection,
        IToolkitActionPlanner toolkitActionPlanner,
        MainWindowStatusController statusController,
        MainWindowRecoveryController recoveryController,
        MainWindowTrayPreviewStateController trayPreviewStateController,
        MainWindowTrayPreviewSelectionController trayPreviewSelectionController,
        ModPreviewWorkspaceViewModel modPreviewWorkspace,
        TrayPreviewWorkspaceViewModel trayPreviewWorkspace,
        OrganizePanelViewModel organize,
        TextureCompressPanelViewModel textureCompress,
        FlattenPanelViewModel flatten,
        NormalizePanelViewModel normalize,
        MergePanelViewModel merge,
        FindDupPanelViewModel findDup,
        TrayDependenciesPanelViewModel trayDependencies,
        ModPreviewPanelViewModel modPreview,
        TrayPreviewPanelViewModel trayPreview,
        SharedFileOpsPanelViewModel sharedFileOps,
        ITrayThumbnailService trayThumbnailService,
        ITextureCompressionService textureCompressionService,
        ITextureDimensionProbe textureDimensionProbe)
    {
        _executionCoordinator = executionCoordinator;
        _trayPreviewCoordinator = trayPreviewCoordinator;
        _trayDependencyExportService = trayDependencyExportService;
        _trayDependencyAnalysisService = trayDependencyAnalysisService;
        _trayThumbnailService = trayThumbnailService;
        _fileDialogService = fileDialogService;
        _confirmationDialogService = confirmationDialogService;
        _localization = localization;
        _settingsPersistenceController = settingsPersistenceController;
        _settingsProjection = settingsProjection;
        _toolkitActionPlanner = toolkitActionPlanner;
        _statusController = statusController;
        _recoveryController = recoveryController;
        _trayPreviewStateController = trayPreviewStateController;
        _trayPreviewSelectionController = trayPreviewSelectionController;
        _textureCompressionService = textureCompressionService;
        _textureDimensionProbe = textureDimensionProbe;
        ModPreviewWorkspace = modPreviewWorkspace;
        TrayPreviewWorkspace = trayPreviewWorkspace;

        Organize = organize;
        TextureCompress = textureCompress;
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

        AvailableToolkitActions = _toolkitActionPlanner.AvailableToolkitActions;

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
        _statusController.PropertyChanged += OnStatusControllerPropertyChanged;
        _trayPreviewStateController.PropertyChanged += OnTrayPreviewStateControllerPropertyChanged;
        _trayPreviewSelectionController.PropertyChanged += OnTrayPreviewSelectionControllerPropertyChanged;
        _localization.SetLanguage(_selectedLanguageCode);
        _selectedLanguageCode = _localization.CurrentLanguageCode;
        ProgressMessage = L("progress.idle");
        ClearTrayPreview();
        StatusMessage = L("status.ready");
        ValidationSummaryText = L("validation.notStarted");

        HookValidationTracking();
    }

    public OrganizePanelViewModel Organize { get; }
    public TextureCompressPanelViewModel TextureCompress { get; }
    public FlattenPanelViewModel Flatten { get; }
    public NormalizePanelViewModel Normalize { get; }
    public MergePanelViewModel Merge { get; }
    public FindDupPanelViewModel FindDup { get; }
    public TrayDependenciesPanelViewModel TrayDependencies { get; }
    public ModPreviewPanelViewModel ModPreview { get; }
    public ModPreviewWorkspaceViewModel ModPreviewWorkspace { get; }
    public TrayPreviewPanelViewModel TrayPreview { get; }
    public TrayPreviewWorkspaceViewModel TrayPreviewWorkspace { get; }
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
    public IReadOnlyList<string> ModPreviewPackageTypeFilterOptions => ["All", "Cas", "BuildBuy"];
    public IReadOnlyList<string> ModPreviewScopeFilterOptions => ["All", "CAS Part", "Object"];
    public IReadOnlyList<string> ModPreviewSortOptions => ["Last Indexed", "Name", "Package"];

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
            OnPropertyChanged(nameof(IsTextureCompressVisible));
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
        get => _statusController.IsProgressIndeterminate;
        private set => _statusController.IsProgressIndeterminate = value;
    }

    public int ProgressValue
    {
        get => _statusController.ProgressValue;
        private set => _statusController.ProgressValue = value;
    }

    public string ProgressMessage
    {
        get => _statusController.ProgressMessage;
        private set => _statusController.ProgressMessage = value;
    }

    public string StatusMessage
    {
        get => _statusController.StatusMessage;
        private set => _statusController.StatusMessage = value;
    }

    public string LogText
    {
        get => _statusController.LogText;
    }

    public string PreviewSummaryText
    {
        get => _trayPreviewStateController.SummaryText;
    }

    public string PreviewTotalItems
    {
        get => _trayPreviewStateController.TotalItems;
    }

    public string PreviewTotalFiles
    {
        get => _trayPreviewStateController.TotalFiles;
    }

    public string PreviewTotalSize
    {
        get => _trayPreviewStateController.TotalSize;
    }

    public string PreviewLatestWrite
    {
        get => _trayPreviewStateController.LatestWrite;
    }

    public string PreviewPageText
    {
        get => _trayPreviewStateController.PageText;
    }

    public string PreviewLazyLoadText
    {
        get => _trayPreviewStateController.LazyLoadText;
    }

    public string PreviewJumpPageText
    {
        get => _trayPreviewStateController.JumpPageText;
        set
        {
            if (string.Equals(_trayPreviewStateController.JumpPageText, value, StringComparison.Ordinal))
            {
                return;
            }

            _trayPreviewStateController.JumpPageText = value;
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
    public bool IsTextureCompressVisible => SelectedAction == SimsAction.TextureCompress;
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
        _toolkitActionPlanner.UsesSharedFileOps(SelectedAction);

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

    public bool CanGoPrevPage => !IsBusy && !_isTrayPreviewPageLoading && _trayPreviewStateController.CurrentPage > 1;
    public bool CanGoNextPage => !IsBusy && !_isTrayPreviewPageLoading && _trayPreviewStateController.CurrentPage < _trayPreviewStateController.TotalPages;
    public bool CanJumpToPage => !IsBusy && !_isTrayPreviewPageLoading && TryParsePreviewJumpPage(PreviewJumpPageText, out var page) && page >= 1 && page <= _trayPreviewStateController.TotalPages;
    public bool IsBuildSizeFilterVisible =>
        string.Equals(TrayPreview.PresetTypeFilter, "Lot", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(TrayPreview.PresetTypeFilter, "Room", StringComparison.OrdinalIgnoreCase);
    public bool IsHouseholdSizeFilterVisible =>
        string.Equals(TrayPreview.PresetTypeFilter, "Household", StringComparison.OrdinalIgnoreCase);
    public bool HasTrayPreviewItems => PreviewItems.Count > 0;
    public bool HasSelectedTrayPreviewItems => _trayPreviewSelectionController.HasSelectedItems;
    public int SelectedTrayPreviewCount => _trayPreviewSelectionController.SelectedCount;
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
        get => _trayPreviewSelectionController.DetailItem;
    }
    public bool IsTrayPreviewDetailVisible => TrayPreviewDetailItem is not null;
    public bool IsTrayPreviewDetailDescriptionEmpty => TrayPreviewDetailItem is null || !TrayPreviewDetailItem.Item.HasDisplayDescription;
    public bool IsTrayPreviewDetailOverviewEmpty =>
        TrayPreviewDetailItem is null ||
        (!TrayPreviewDetailItem.Item.HasDisplayPrimaryMeta &&
         !TrayPreviewDetailItem.Item.HasDisplaySecondaryMeta &&
         !TrayPreviewDetailItem.Item.HasDisplayTertiaryMeta);
    public bool CanGoBackTrayPreviewDetail => _trayPreviewSelectionController.CanGoBackDetail;
    public bool IsTrayPreviewEmptyStatusOk => HasValidTrayPreviewPath && !_trayPreviewStateController.HasLoadedOnce;
    public bool IsTrayPreviewEmptyStatusWarning => HasValidTrayPreviewPath && _trayPreviewStateController.HasLoadedOnce;
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

            if (_trayPreviewStateController.HasLoadedOnce)
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

            if (_trayPreviewStateController.HasLoadedOnce)
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

            if (_trayPreviewStateController.HasLoadedOnce)
            {
                return L("preview.empty.status.noResults");
            }

            return L("preview.empty.status.ready");
        }
    }

    public string TrayPreviewLoadingText => L("status.trayPreviewLoading");

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
        if (SelectedAction == SimsAction.TrayDependencies)
        {
            await RunTrayDependenciesAsync();
            return;
        }

        if (SelectedAction == SimsAction.TextureCompress)
        {
            await RunTextureCompressionAsync();
            return;
        }

        if (!_toolkitActionPlanner.TryBuildToolkitCliPlan(CreatePlanBuilderState(), out var cliPlan, out var error))
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

        await RunToolkitPlanAsync(cliPlan, existingOperationId: null);
    }

    private async Task RunToolkitPlanAsync(CliExecutionPlan cliPlan, string? existingOperationId)
    {
        if (_executionCts is not null)
        {
            StatusMessage = L("status.executionAlreadyRunning");
            return;
        }

        var input = cliPlan.Input;
        var operationId = existingOperationId ?? await RegisterRecoveryAsync(_recoveryController.BuildToolkitRecoveryPayload(cliPlan));

        _executionCts = new CancellationTokenSource();
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var recoveryCompleted = false;

        ClearLog();
        IsBusy = true;
        SetProgress(isIndeterminate: true, percent: 0, message: L("progress.starting"));
        AppendLog("[start] " + startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        AppendLog("[action] " + input.Action.ToString().ToLowerInvariant());
        StatusMessage = L("status.running");

        try
        {
            await MarkRecoveryStartedAsync(operationId);
            SimsExecutionResult result;
            try
            {
                result = await _executionCoordinator.ExecuteAsync(
                    cliPlan.Input,
                    AppendLog,
                    HandleProgress,
                    _executionCts.Token);
            }
            catch (OperationCanceledException)
            {
                AppendLog("[cancelled]");
                StatusMessage = L("status.executionCancelled");
                SetProgress(isIndeterminate: false, percent: 0, message: L("progress.cancelled"));
                await MarkRecoveryCompletedAsync(
                    operationId,
                    new RecoverableOperationCompletion
                    {
                        Status = OperationRecoveryStatus.Cancelled
                    });
                await SaveResultHistoryAsync(input.Action, "Toolkit", "Cancelled", operationId);
                recoveryCompleted = true;
                return;
            }
            catch (Exception ex)
            {
                var errorMessage = string.IsNullOrWhiteSpace(ex.Message)
                    ? L("status.unknownExecutionError")
                    : ex.Message;
                AppendLog("[error] " + errorMessage);
                StatusMessage = L("status.executionFailed");
                SetProgress(isIndeterminate: false, percent: 0, message: L("progress.executionFailed"));
                await MarkRecoveryCompletedAsync(
                    operationId,
                    new RecoverableOperationCompletion
                    {
                        Status = OperationRecoveryStatus.Failed,
                        FailureMessage = errorMessage
                    });
                await SaveResultHistoryAsync(input.Action, "Toolkit", errorMessage, operationId);
                recoveryCompleted = true;
                await ShowErrorPopupAsync(L("status.executionFailed"));
                return;
            }
            stopwatch.Stop();
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
                await MarkRecoveryCompletedAsync(
                    operationId,
                    new RecoverableOperationCompletion
                    {
                        Status = OperationRecoveryStatus.Failed,
                        FailureMessage = $"Process exited with code {result.ExitCode}.",
                        ResultSummaryJson = JsonSerializer.Serialize(new
                        {
                            result.ExitCode,
                            Elapsed = stopwatch.Elapsed.ToString("mm\\:ss")
                        })
                    });
                await SaveResultHistoryAsync(input.Action, "Toolkit", $"Exit code {result.ExitCode}", operationId);
                recoveryCompleted = true;
                await ShowErrorPopupAsync(L("status.executionFailed"));
            }
            else
            {
                await MarkRecoveryCompletedAsync(
                    operationId,
                    new RecoverableOperationCompletion
                    {
                        Status = OperationRecoveryStatus.Succeeded,
                        ResultSummaryJson = JsonSerializer.Serialize(new
                        {
                            result.ExitCode,
                            Elapsed = stopwatch.Elapsed.ToString("mm\\:ss")
                        })
                    });
                await SaveResultHistoryAsync(input.Action, "Toolkit", "Completed", operationId);
                recoveryCompleted = true;
            }
        }
        catch (Exception ex)
        {
            if (!recoveryCompleted)
            {
                await MarkRecoveryCompletedAsync(
                    operationId,
                    new RecoverableOperationCompletion
                    {
                        Status = OperationRecoveryStatus.Failed,
                        FailureMessage = ex.Message
                    });
            }

            throw;
        }
        finally
        {
            _executionCts.Dispose();
            _executionCts = null;
            IsBusy = false;
            RefreshValidationNow();
        }
    }

    private async Task RunTextureCompressionAsync()
    {
        if (!_toolkitActionPlanner.TryBuildTextureCompressionPlan(CreatePlanBuilderState(), out var plan, out var error))
        {
            StatusMessage = error;
            AppendLog("[validation] " + error);
            await ShowErrorPopupAsync(L("status.validationFailed"));
            return;
        }

        await RunTextureCompressionAsync(plan);
    }

    private async Task RunTextureCompressionAsync(TextureCompressionExecutionPlan plan)
    {
        if (_executionCts is not null)
        {
            StatusMessage = L("status.executionAlreadyRunning");
            return;
        }

        _executionCts = new CancellationTokenSource();
        IsBusy = true;
        SetProgress(isIndeterminate: true, percent: 0, message: "Compressing texture...");
        StatusMessage = L("status.running");
        AppendLog("[action] texturecompress");

        try
        {
            var request = plan.Request;
            if (request.WhatIf)
            {
                TextureCompress.LastOutputPath = request.OutputPath;
                TextureCompress.LastRunSummary = $"WhatIf: would compress '{Path.GetFileName(request.SourcePath)}' to '{Path.GetFileName(request.OutputPath)}'.";
                AppendLog("[whatif] source=" + request.SourcePath);
                AppendLog("[whatif] output=" + request.OutputPath);
                SetProgress(isIndeterminate: false, percent: 100, message: "Texture compression preview completed.");
                StatusMessage = "Texture compression preview completed.";
                return;
            }

            var sourceBytes = await File.ReadAllBytesAsync(request.SourcePath, _executionCts.Token);
            if (!TryResolveTextureContainerKind(request.SourcePath, out var containerKind, out var containerError))
            {
                throw new InvalidOperationException(containerError);
            }

            if (!_textureDimensionProbe.TryGetDimensions(containerKind, sourceBytes, out var sourceWidth, out var sourceHeight, out var probeError))
            {
                throw new InvalidOperationException(probeError);
            }

            var result = _textureCompressionService.Compress(new TextureCompressionRequest
            {
                Source = new TextureSourceDescriptor
                {
                    ResourceKey = default,
                    ContainerKind = containerKind,
                    SourcePixelFormat = containerKind == TextureContainerKind.Png ? TexturePixelFormatKind.Rgba32 : TexturePixelFormatKind.Rgba32,
                    Width = sourceWidth,
                    Height = sourceHeight,
                    HasAlpha = request.HasAlphaHint,
                    MipMapCount = 1
                },
                SourceBytes = sourceBytes,
                TargetWidth = request.TargetWidth,
                TargetHeight = request.TargetHeight,
                GenerateMipMaps = request.GenerateMipMaps,
                PreferredFormat = request.PreferredFormat
            });

            if (!result.Success || result.TranscodeResult is null || !result.TranscodeResult.Success)
            {
                throw new InvalidOperationException(result.Error ?? result.TranscodeResult?.Error ?? "Texture compression failed.");
            }

            var outputDirectory = Path.GetDirectoryName(request.OutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            await File.WriteAllBytesAsync(request.OutputPath, result.TranscodeResult.EncodedBytes, _executionCts.Token);

            TextureCompress.LastOutputPath = request.OutputPath;
            TextureCompress.LastRunSummary =
                $"Compressed {Path.GetFileName(request.SourcePath)} to {result.SelectedFormat} {result.TranscodeResult.OutputWidth}x{result.TranscodeResult.OutputHeight}.";

            AppendLog("[output] " + request.OutputPath);
            SetProgress(isIndeterminate: false, percent: 100, message: "Texture compression completed.");
            StatusMessage = "Texture compression completed.";
        }
        catch (OperationCanceledException)
        {
            AppendLog("[cancelled]");
            StatusMessage = L("status.executionCancelled");
            SetProgress(isIndeterminate: false, percent: 0, message: L("progress.cancelled"));
        }
        catch (Exception ex)
        {
            AppendLog("[error] " + ex.Message);
            StatusMessage = "Texture compression failed.";
            SetProgress(isIndeterminate: false, percent: 0, message: "Texture compression failed.");
            await ShowErrorPopupAsync("Texture compression failed.");
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
        if (!_toolkitActionPlanner.TryBuildTrayDependenciesPlan(CreatePlanBuilderState(), out var plan, out var error))
        {
            StatusMessage = error;
            AppendLog("[validation] " + error);
            await ShowErrorPopupAsync(L("status.validationFailed"));
            return;
        }

        await RunTrayDependenciesPlanAsync(plan, existingOperationId: null);
    }

    private async Task RunTrayDependenciesPlanAsync(TrayDependenciesExecutionPlan plan, string? existingOperationId)
    {
        if (_executionCts is not null)
        {
            StatusMessage = L("status.executionAlreadyRunning");
            return;
        }

        var operationId = existingOperationId ?? await RegisterRecoveryAsync(_recoveryController.BuildTrayDependenciesRecoveryPayload(plan));

        _executionCts = new CancellationTokenSource();
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var recoveryCompleted = false;

        ClearLog();
        IsBusy = true;
        SetProgress(isIndeterminate: true, percent: 0, message: "Preparing tray dependency analysis...");
        AppendLog("[start] " + startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        AppendLog("[action] traydependencies");
        StatusMessage = L("status.running");

        try
        {
            await MarkRecoveryStartedAsync(operationId);
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
                await MarkRecoveryCompletedAsync(
                    operationId,
                    new RecoverableOperationCompletion
                    {
                        Status = OperationRecoveryStatus.Succeeded,
                        ResultSummaryJson = JsonSerializer.Serialize(new
                        {
                            result.MatchedPackageCount,
                            result.UnusedPackageCount,
                            HasWarnings = hasWarnings
                        })
                    });
                await SaveResultHistoryAsync(SimsAction.TrayDependencies, "TrayDependencies", "Completed", operationId);
                recoveryCompleted = true;
                return;
            }

            StatusMessage = "Tray dependency analysis failed.";
            SetProgress(isIndeterminate: false, percent: 0, message: "Tray dependency analysis failed.");
            await MarkRecoveryCompletedAsync(
                operationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Failed,
                    FailureMessage = "Tray dependency analysis failed."
                });
            await SaveResultHistoryAsync(SimsAction.TrayDependencies, "TrayDependencies", "Failed", operationId);
            recoveryCompleted = true;
            await ShowErrorPopupAsync("Tray dependency analysis failed.");
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            AppendLog("[cancelled]");
            StatusMessage = L("status.executionCancelled");
            SetProgress(isIndeterminate: false, percent: 0, message: L("progress.cancelled"));
            await MarkRecoveryCompletedAsync(
                operationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Cancelled
                });
            await SaveResultHistoryAsync(SimsAction.TrayDependencies, "TrayDependencies", "Cancelled", operationId);
            recoveryCompleted = true;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AppendLog("[error] " + ex.Message);
            StatusMessage = "Tray dependency analysis failed.";
            SetProgress(isIndeterminate: false, percent: 0, message: "Tray dependency analysis failed.");
            if (!recoveryCompleted)
            {
                await MarkRecoveryCompletedAsync(
                    operationId,
                    new RecoverableOperationCompletion
                    {
                        Status = OperationRecoveryStatus.Failed,
                        FailureMessage = ex.Message
                    });
                await SaveResultHistoryAsync(SimsAction.TrayDependencies, "TrayDependencies", ex.Message, operationId);
            }
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

    public async Task RunTrayDependenciesForTrayItemAsync(string trayPath, string trayItemKey)
    {
        if (string.IsNullOrWhiteSpace(trayPath))
        {
            throw new ArgumentException("Tray path is required.", nameof(trayPath));
        }

        if (string.IsNullOrWhiteSpace(trayItemKey))
        {
            throw new ArgumentException("Tray item key is required.", nameof(trayItemKey));
        }

        TrayDependencies.TrayPath = Path.GetFullPath(trayPath.Trim());
        TrayDependencies.TrayItemKey = trayItemKey.Trim();
        Workspace = AppWorkspace.Toolkit;
        SelectedAction = SimsAction.TrayDependencies;
        await RunTrayDependenciesAsync();
    }

    private Task<string?> RegisterRecoveryAsync(RecoverableOperationPayload payload) =>
        _recoveryController.RegisterRecoveryAsync(payload);

    private Task MarkRecoveryStartedAsync(string? operationId) =>
        _recoveryController.MarkRecoveryStartedAsync(operationId);

    private Task MarkRecoveryCompletedAsync(string? operationId, RecoverableOperationCompletion completion) =>
        _recoveryController.MarkRecoveryCompletedAsync(operationId, completion);

    private Task SaveResultHistoryAsync(SimsAction action, string source, string summary, string? relatedOperationId) =>
        _recoveryController.SaveResultHistoryAsync(action, source, summary, relatedOperationId);


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

    private ToolkitPlanningState CreatePlanBuilderState()
    {
        return new ToolkitPlanningState
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
        ExecuteOnUi(() => _statusController.SetProgress(isIndeterminate, percent, message));
    }

    private void ClearLog()
    {
        ExecuteOnUi(_statusController.ClearLog);
    }

    private void AppendLog(string message)
    {
        ExecuteOnUi(() => _statusController.AppendLog(message));
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

    private static bool TryResolveTextureContainerKind(string sourcePath, out TextureContainerKind containerKind, out string error)
    {
        error = string.Empty;
        switch (Path.GetExtension(sourcePath).Trim().ToLowerInvariant())
        {
            case ".png":
                containerKind = TextureContainerKind.Png;
                return true;
            case ".dds":
                containerKind = TextureContainerKind.Dds;
                return true;
            case ".tga":
                containerKind = TextureContainerKind.Tga;
                return true;
            default:
                containerKind = default;
                error = "Source file must be a .png, .dds, or .tga file.";
                return false;
        }
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

