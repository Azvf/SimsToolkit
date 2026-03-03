
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

public sealed class MainWindowViewModel : ObservableObject
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
    private readonly ITextureCompressionService _textureCompressionService;
    private readonly ITextureDimensionProbe _textureDimensionProbe;
    private readonly Stack<TrayPreviewListItemViewModel> _trayPreviewDetailHistory = new();
    private readonly HashSet<string> _selectedTrayPreviewKeys = new(StringComparer.OrdinalIgnoreCase);

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
    private TrayPreviewListItemViewModel? _trayPreviewDetailItem;
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

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await _recoveryController.InitializeAsync();

        var settings = await _settingsPersistenceController.LoadAsync();
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

        _toolkitActionPlanner.LoadModuleSettings(settings);
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

    }

    public async Task PersistSettingsAsync()
    {
        _validationDebounceCts?.Cancel();
        _settingsPersistenceController.CancelPending();
        CancelTrayPreviewThumbnailLoading();
        await SaveCurrentSettingsAsync();
    }

    public async Task ResumeRecoverableOperationAsync(RecoverableOperationRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            switch (record.Payload.PayloadKind)
            {
                case "ToolkitCli":
                    await RunToolkitPlanAsync(_recoveryController.BuildToolkitCliPlan(record.Payload), record.OperationId);
                    break;
                case "TrayDependencies":
                    await RunTrayDependenciesPlanAsync(
                        new TrayDependenciesExecutionPlan(_recoveryController.BuildTrayDependenciesRequest(record.Payload)),
                        record.OperationId);
                    break;
                case "TrayPreview":
                    await RunTrayPreviewCoreAsync(_recoveryController.BuildTrayPreviewInput(record.Payload), record.OperationId);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported recovery payload kind: {record.Payload.PayloadKind}");
            }
        }
        catch (Exception ex)
        {
            await _recoveryController.MarkRecoveryCompletedAsync(
                record.OperationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Failed,
                    FailureMessage = ex.Message
                });

            AppendLog("[recovery] " + ex.Message);
            StatusMessage = "Failed to resume the previous task.";
            await ShowErrorPopupAsync("Failed to resume the previous task.");
        }
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
        // Tray preview auto-load now lives in TrayPreviewWorkspace.Surface.
    }

    private void QueueSettingsPersist()
    {
        _settingsPersistenceController.QueuePersist(ApplyCurrentSettings);
    }

    private async Task SaveCurrentSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _settingsPersistenceController.SaveAsync(ApplyCurrentSettings, cancellationToken);
    }

    private void ApplyCurrentSettings(AppSettings settings)
    {
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

        _toolkitActionPlanner.SaveModuleSettings(settings);
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
                if (SelectedAction == SimsAction.TrayDependencies)
                {
                    if (!_toolkitActionPlanner.TryBuildTrayDependenciesPlan(CreatePlanBuilderState(), out _, out var trayDependencyError))
                    {
                        HasValidationErrors = true;
                        ValidationSummaryText = LF("validation.failed", trayDependencyError);
                        return;
                    }
                }
                else if (SelectedAction == SimsAction.TextureCompress)
                {
                    if (!_toolkitActionPlanner.TryBuildTextureCompressionPlan(CreatePlanBuilderState(), out _, out var textureCompressError))
                    {
                        HasValidationErrors = true;
                        ValidationSummaryText = LF("validation.failed", textureCompressError);
                        return;
                    }
                }
                else if (!_toolkitActionPlanner.TryBuildToolkitCliPlan(CreatePlanBuilderState(), out _, out var error))
                {
                    HasValidationErrors = true;
                    ValidationSummaryText = LF("validation.failed", error);
                    return;
                }

                HasValidationErrors = false;
                ValidationSummaryText = LF("validation.okToolkit", _toolkitActionPlanner.GetDisplayName(SelectedAction));
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

            if (!_toolkitActionPlanner.TryBuildTrayPreviewInput(CreatePlanBuilderState(), out _, out var trayPreviewError))
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

    private Task RunTrayPreviewAsync(TrayPreviewInput? explicitInput = null)
    {
        return RunTrayPreviewCoreAsync(explicitInput, existingOperationId: null);
    }

    private async Task RunTrayPreviewCoreAsync(TrayPreviewInput? explicitInput = null, string? existingOperationId = null)
    {
        if (_executionCts is not null)
        {
            StatusMessage = L("status.executionAlreadyRunning");
            return;
        }

        TrayPreviewInput input;
        if (explicitInput is null)
        {
            if (!_toolkitActionPlanner.TryBuildTrayPreviewInput(CreatePlanBuilderState(), out var built, out var validationError))
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

        var operationId = existingOperationId ?? await RegisterRecoveryAsync(_recoveryController.BuildTrayPreviewRecoveryPayload(input));

        _executionCts = new CancellationTokenSource();
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        _trayPreviewCoordinator.Reset();
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
            await MarkRecoveryStartedAsync(operationId);
            var result = await _trayPreviewCoordinator.LoadAsync(input, _executionCts.Token);
            stopwatch.Stop();
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
            await MarkRecoveryCompletedAsync(
                operationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Succeeded,
                    ResultSummaryJson = JsonSerializer.Serialize(new
                    {
                        result.Summary.TotalItems,
                        result.Page.TotalPages
                    })
                });
            await SaveResultHistoryAsync(SimsAction.TrayPreview, "TrayPreview", $"Loaded {result.Summary.TotalItems} items", operationId);
        }
        catch (OperationCanceledException)
        {
            AppendLog("[cancelled]");
            StatusMessage = L("status.trayPreviewCancelled");
            SetProgress(isIndeterminate: false, percent: 0, message: L("progress.cancelled"));
            await MarkRecoveryCompletedAsync(
                operationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Cancelled
                });
            await SaveResultHistoryAsync(SimsAction.TrayPreview, "TrayPreview", "Cancelled", operationId);
        }
        catch (Exception ex)
        {
            var errorMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? L("status.unknownTrayPreviewError")
                : ex.Message;
            AppendLog("[error] " + errorMessage);
            StatusMessage = L("status.trayPreviewFailed");
            SetProgress(isIndeterminate: false, percent: 0, message: L("progress.trayFailed"));
            await MarkRecoveryCompletedAsync(
                operationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Failed,
                    FailureMessage = errorMessage
                });
            await SaveResultHistoryAsync(SimsAction.TrayPreview, "TrayPreview", errorMessage, operationId);
            await ShowErrorPopupAsync(L("status.trayPreviewFailed"));
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

    private async Task LoadPreviousTrayPreviewPageAsync()
    {
        await LoadTrayPreviewPageAsync(_trayPreviewStateController.CurrentPage - 1);
    }

    private async Task LoadNextTrayPreviewPageAsync()
    {
        await LoadTrayPreviewPageAsync(_trayPreviewStateController.CurrentPage + 1);
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
            var result = await _trayPreviewCoordinator.LoadPageAsync(requestedPageIndex);
            SetTrayPreviewPage(result.Page, result.LoadedPageCount);
            StatusMessage = LF("status.trayPageLoaded", result.Page.PageIndex, result.Page.TotalPages);
            return;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = L("status.trayPageCancelled");
            return;
        }
        catch (Exception ex)
        {
            var errorMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? L("status.unknownTrayPreviewPageError")
                : ex.Message;
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

        if (!_toolkitActionPlanner.TryBuildTrayPreviewInput(CreatePlanBuilderState(), out var input, out _))
        {
            return;
        }

        if (_trayPreviewCoordinator.TryGetCached(input, out var cached))
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

    private void ClearTrayPreview()
    {
        ExecuteOnUi(() =>
        {
            CloseTrayPreviewDetails();
            CancelTrayPreviewThumbnailLoading();
            ClearTrayPreviewSelection();
            ClearPreviewItems();
            _trayPreviewStateController.Reset(
                L("preview.noneLoaded"),
                LF("preview.page", 0, 0),
                LF("preview.lazyCache", 0, 0));
            NotifyTrayPreviewViewStateChanged();
            NotifyCommandStates();
        });
    }

    private void SetTrayPreviewSummary(SimsTrayPreviewSummary summary)
    {
        ExecuteOnUi(() =>
        {
            var breakdown = string.IsNullOrWhiteSpace(summary.PresetTypeBreakdown)
                ? L("preview.typeNa")
                : LF("preview.type", summary.PresetTypeBreakdown);
            _trayPreviewStateController.ApplySummary(
                summary.TotalItems.ToString("N0"),
                summary.TotalFiles.ToString("N0"),
                $"{summary.TotalMB:N2} MB",
                summary.LatestWriteTimeLocal == DateTime.MinValue
                    ? "-"
                    : summary.LatestWriteTimeLocal.ToString("yyyy-MM-dd HH:mm"),
                LF("preview.summaryReady", breakdown));
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

            var firstItemIndex = page.Items.Count == 0 ? 0 : ((page.PageIndex - 1) * page.PageSize) + 1;
            var lastItemIndex = page.Items.Count == 0 ? 0 : firstItemIndex + page.Items.Count - 1;
            var safeTotalPages = Math.Max(page.TotalPages, 1);
            _trayPreviewStateController.ApplyPage(
                page.PageIndex,
                safeTotalPages,
                page.TotalItems.ToString("N0"),
                LF("preview.range", firstItemIndex, lastItemIndex, page.TotalItems),
                LF("preview.page", page.PageIndex, safeTotalPages),
                LF("preview.lazyCache", loadedPageCount, safeTotalPages),
                page.PageIndex.ToString());
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
            _trayPreviewStateController.CurrentPage,
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

    private void OnStatusControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowStatusController.StatusMessage):
                OnPropertyChanged(nameof(StatusMessage));
                return;
            case nameof(MainWindowStatusController.IsProgressIndeterminate):
                OnPropertyChanged(nameof(IsProgressIndeterminate));
                return;
            case nameof(MainWindowStatusController.ProgressValue):
                OnPropertyChanged(nameof(ProgressValue));
                return;
            case nameof(MainWindowStatusController.ProgressMessage):
                OnPropertyChanged(nameof(ProgressMessage));
                return;
            case nameof(MainWindowStatusController.LogText):
                OnPropertyChanged(nameof(LogText));
                ClearLogCommand.NotifyCanExecuteChanged();
                return;
        }
    }

    private void OnTrayPreviewStateControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowTrayPreviewStateController.SummaryText):
                OnPropertyChanged(nameof(PreviewSummaryText));
                return;
            case nameof(MainWindowTrayPreviewStateController.TotalItems):
                OnPropertyChanged(nameof(PreviewTotalItems));
                OnPropertyChanged(nameof(TrayPreviewSelectionSummaryText));
                return;
            case nameof(MainWindowTrayPreviewStateController.TotalFiles):
                OnPropertyChanged(nameof(PreviewTotalFiles));
                return;
            case nameof(MainWindowTrayPreviewStateController.TotalSize):
                OnPropertyChanged(nameof(PreviewTotalSize));
                return;
            case nameof(MainWindowTrayPreviewStateController.LatestWrite):
                OnPropertyChanged(nameof(PreviewLatestWrite));
                return;
            case nameof(MainWindowTrayPreviewStateController.PageText):
                OnPropertyChanged(nameof(PreviewPageText));
                return;
            case nameof(MainWindowTrayPreviewStateController.LazyLoadText):
                OnPropertyChanged(nameof(PreviewLazyLoadText));
                return;
            case nameof(MainWindowTrayPreviewStateController.JumpPageText):
                OnPropertyChanged(nameof(PreviewJumpPageText));
                OnPropertyChanged(nameof(CanJumpToPage));
                PreviewJumpPageCommand.NotifyCanExecuteChanged();
                return;
            case nameof(MainWindowTrayPreviewStateController.CurrentPage):
            case nameof(MainWindowTrayPreviewStateController.TotalPages):
                OnPropertyChanged(nameof(CanGoPrevPage));
                OnPropertyChanged(nameof(CanGoNextPage));
                OnPropertyChanged(nameof(CanJumpToPage));
                PreviewPrevPageCommand.NotifyCanExecuteChanged();
                PreviewNextPageCommand.NotifyCanExecuteChanged();
                PreviewJumpPageCommand.NotifyCanExecuteChanged();
                return;
            case nameof(MainWindowTrayPreviewStateController.HasLoadedOnce):
                OnPropertyChanged(nameof(IsTrayPreviewEmptyStatusOk));
                OnPropertyChanged(nameof(IsTrayPreviewEmptyStatusWarning));
                OnPropertyChanged(nameof(TrayPreviewEmptyTitleText));
                OnPropertyChanged(nameof(TrayPreviewEmptyDescriptionText));
                OnPropertyChanged(nameof(TrayPreviewEmptyStatusText));
                return;
        }
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

