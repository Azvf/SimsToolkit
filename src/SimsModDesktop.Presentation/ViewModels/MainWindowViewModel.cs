
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
    private readonly IFileDialogService _fileDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly ILocalizationService _localization;
    private readonly IToolkitActionPlanner _toolkitActionPlanner;
    private readonly MainWindowExecutionController _executionController;
    private readonly MainWindowRecoveryController _recoveryController;
    private readonly MainWindowTrayPreviewController _trayPreviewController;
    private readonly MainWindowTrayExportController _trayExportController;
    private readonly MainWindowValidationController _validationController;
    private readonly MainWindowLifecycleController _lifecycleController;
    private readonly MainWindowTrayPreviewStateController _trayPreviewStateController;
    private readonly MainWindowTrayPreviewSelectionController _trayPreviewSelectionController;

    private readonly MainWindowStatusController _statusController;
    private CancellationTokenSource? _executionCts;
    private bool _isTrayPreviewPageLoading;
    private bool _isBusy;
    private AppWorkspace _workspace = AppWorkspace.Toolkit;
    private SimsAction _selectedAction = SimsAction.Organize;
    private string _selectedLanguageCode = DefaultLanguageCode;
    private bool _whatIf;
    private string _validationSummaryText = string.Empty;
    private bool _hasValidationErrors;
    private bool _isToolkitLogDrawerOpen;
    private bool _isTrayPreviewLogDrawerOpen;
    private bool _isToolkitAdvancedOpen;
    private bool _isInitialized;
    private bool _isTrayExportQueueExpanded = true;

    public MainWindowViewModel(
        IFileDialogService fileDialogService,
        IConfirmationDialogService confirmationDialogService,
        ILocalizationService localization,
        IToolkitActionPlanner toolkitActionPlanner,
        MainWindowExecutionController executionController,
        MainWindowStatusController statusController,
        MainWindowRecoveryController recoveryController,
        MainWindowTrayPreviewController trayPreviewController,
        MainWindowTrayExportController trayExportController,
        MainWindowValidationController validationController,
        MainWindowLifecycleController lifecycleController,
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
        SharedFileOpsPanelViewModel sharedFileOps)
    {
        _fileDialogService = fileDialogService;
        _confirmationDialogService = confirmationDialogService;
        _localization = localization;
        _toolkitActionPlanner = toolkitActionPlanner;
        _executionController = executionController;
        _statusController = statusController;
        _recoveryController = recoveryController;
        _trayPreviewController = trayPreviewController;
        _trayExportController = trayExportController;
        _validationController = validationController;
        _lifecycleController = lifecycleController;
        _trayPreviewStateController = trayPreviewStateController;
        _trayPreviewSelectionController = trayPreviewSelectionController;
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
        ExportSelectedTrayPreviewFilesCommand = new AsyncRelayCommand(
            ExportSelectedTrayPreviewFilesAsync,
            () => HasSelectedTrayPreviewItems &&
                  TrayPreviewWorkspace.IsTrayDependencyCacheReady &&
                  !TrayPreviewWorkspace.IsDependencyCacheWarmupBlocking);
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
        TrayPreviewWorkspace.PropertyChanged += OnTrayPreviewWorkspacePropertyChanged;
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

