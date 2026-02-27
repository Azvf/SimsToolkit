
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using SimsModDesktop.Application.Execution;
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
    private readonly ISettingsStore _settingsStore;

    private readonly StringWriter _logWriter = new();
    private CancellationTokenSource? _executionCts;
    private bool _isTrayPreviewPageLoading;
    private bool _isBusy;
    private SimsAction _selectedAction = SimsAction.Organize;
    private string _scriptPath = string.Empty;
    private bool _whatIf;
    private string _statusMessage = "Ready.";
    private bool _isProgressIndeterminate;
    private int _progressValue;
    private string _progressMessage = "Idle";
    private string _logText = string.Empty;
    private int _outputTabIndex;
    private int _trayPreviewCurrentPage = 1;
    private int _trayPreviewTotalPages = 1;
    private string _previewSummaryText = "No preview data loaded.";
    private string _previewDashboardTotalItems = "0";
    private string _previewDashboardTotalFiles = "0";
    private string _previewDashboardTotalSize = "0 MB";
    private string _previewDashboardLatestWrite = "-";
    private string _previewPageText = "Page 0/0";
    private string _previewLazyLoadText = "Lazy cache 0/0 pages";
    private bool _isInitialized;

    public MainWindowViewModel(
        IExecutionCoordinator executionCoordinator,
        ITrayPreviewCoordinator trayPreviewCoordinator,
        IFileDialogService fileDialogService,
        ISettingsStore settingsStore)
    {
        _executionCoordinator = executionCoordinator;
        _trayPreviewCoordinator = trayPreviewCoordinator;
        _fileDialogService = fileDialogService;
        _settingsStore = settingsStore;

        Organize = new OrganizePanelViewModel();
        Flatten = new FlattenPanelViewModel();
        Normalize = new NormalizePanelViewModel();
        Merge = new MergePanelViewModel();
        FindDup = new FindDupPanelViewModel();
        TrayDependencies = new TrayDependenciesPanelViewModel();
        TrayPreview = new TrayPreviewPanelViewModel();
        SharedFileOps = new SharedFileOpsPanelViewModel();
        PreviewItems = new ObservableCollection<SimsTrayPreviewItem>();
        AvailableActions = Enum.GetValues<SimsAction>();

        BrowseScriptPathCommand = new AsyncRelayCommand(BrowseScriptPathAsync, () => !IsBusy);
        BrowseFolderCommand = new AsyncRelayCommand<string>(BrowseFolderAsync, _ => !IsBusy);
        BrowseCsvPathCommand = new AsyncRelayCommand<string>(BrowseCsvPathAsync, _ => !IsBusy);
        RunCommand = new AsyncRelayCommand(RunAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(CancelExecution, () => IsBusy);
        PreviewPrevPageCommand = new AsyncRelayCommand(LoadPreviousTrayPreviewPageAsync, () => CanGoPrevPage);
        PreviewNextPageCommand = new AsyncRelayCommand(LoadNextTrayPreviewPageAsync, () => CanGoNextPage);
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

    public IReadOnlyList<SimsAction> AvailableActions { get; }

    public AsyncRelayCommand BrowseScriptPathCommand { get; }
    public AsyncRelayCommand<string> BrowseFolderCommand { get; }
    public AsyncRelayCommand<string> BrowseCsvPathCommand { get; }
    public AsyncRelayCommand RunCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand PreviewPrevPageCommand { get; }
    public AsyncRelayCommand PreviewNextPageCommand { get; }

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
            OnPropertyChanged(nameof(IsTrayPreviewVisible));
            OnPropertyChanged(nameof(IsSharedFileOpsVisible));
            OutputTabIndex = value == SimsAction.TrayPreview ? 1 : 0;
            StatusMessage = "Ready.";

            if (value == SimsAction.TrayPreview)
            {
                _ = TryAutoLoadTrayPreviewAsync();
            }
        }
    }

    public string ScriptPath
    {
        get => _scriptPath;
        set => SetProperty(ref _scriptPath, value);
    }

    public bool WhatIf
    {
        get => _whatIf;
        set => SetProperty(ref _whatIf, value);
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
        private set => SetProperty(ref _logText, value);
    }

    public int OutputTabIndex
    {
        get => _outputTabIndex;
        set => SetProperty(ref _outputTabIndex, value);
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

    public bool IsOrganizeVisible => SelectedAction == SimsAction.Organize;
    public bool IsFlattenVisible => SelectedAction == SimsAction.Flatten;
    public bool IsNormalizeVisible => SelectedAction == SimsAction.Normalize;
    public bool IsMergeVisible => SelectedAction == SimsAction.Merge;
    public bool IsFindDupVisible => SelectedAction == SimsAction.FindDuplicates;
    public bool IsTrayDependenciesVisible => SelectedAction == SimsAction.TrayDependencies;
    public bool IsTrayPreviewVisible => SelectedAction == SimsAction.TrayPreview;
    public bool IsSharedFileOpsVisible =>
        SelectedAction == SimsAction.Flatten ||
        SelectedAction == SimsAction.Merge ||
        SelectedAction == SimsAction.FindDuplicates;

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

        if (string.IsNullOrWhiteSpace(ScriptPath))
        {
            var defaultScriptPath = TryFindScriptPath();
            if (!string.IsNullOrWhiteSpace(defaultScriptPath))
            {
                ScriptPath = defaultScriptPath;
            }
        }

        ClearTrayPreview();
        StatusMessage = "Ready.";
        _isInitialized = true;
    }

    public async Task PersistSettingsAsync()
    {
        var settings = CaptureSettings();
        await _settingsStore.SaveAsync(settings);
    }

    private void NotifyCommandStates()
    {
        BrowseScriptPathCommand.NotifyCanExecuteChanged();
        BrowseFolderCommand.NotifyCanExecuteChanged();
        BrowseCsvPathCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        PreviewPrevPageCommand.NotifyCanExecuteChanged();
        PreviewNextPageCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanGoPrevPage));
        OnPropertyChanged(nameof(CanGoNextPage));
    }

    private async Task BrowseScriptPathAsync()
    {
        var path = await _fileDialogService.PickScriptPathAsync();
        if (!string.IsNullOrWhiteSpace(path))
        {
            ScriptPath = path;
            StatusMessage = "Script path updated.";
        }
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
            case "MergeSourcePaths":
                await BrowseMergeSourcePathsAsync();
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

    private async Task BrowseMergeSourcePathsAsync()
    {
        var selectedPaths = await _fileDialogService.PickFolderPathsAsync("Select MergeSourcePaths", allowMultiple: true);
        if (selectedPaths.Count == 0)
        {
            return;
        }

        var merged = ParsePathList(Merge.SourcePathsText);
        foreach (var path in selectedPaths)
        {
            if (!merged.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                merged.Add(path);
            }
        }

        Merge.SourcePathsText = string.Join(Environment.NewLine, merged);
    }
    private async Task RunAsync()
    {
        if (_executionCts is not null)
        {
            StatusMessage = "Execution is already running.";
            return;
        }

        if (SelectedAction == SimsAction.TrayPreview)
        {
            await RunTrayPreviewAsync();
            return;
        }

        if (!TryBuildExecutionInput(out var input, out var error))
        {
            StatusMessage = error;
            AppendLog("[validation] " + error);
            return;
        }

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
        if (SelectedAction != SimsAction.TrayPreview || IsBusy || _isTrayPreviewPageLoading)
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
    private bool TryBuildExecutionInput(out ISimsExecutionInput input, out string error)
    {
        input = null!;
        error = string.Empty;

        var scriptPath = ScriptPath.Trim();
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            error = "Script path is required.";
            return false;
        }

        if (!TryBuildSharedFileOpsInput(out var shared, out error))
        {
            return false;
        }

        switch (SelectedAction)
        {
            case SimsAction.Organize:
                input = new OrganizeInput
                {
                    ScriptPath = Path.GetFullPath(scriptPath),
                    WhatIf = WhatIf,
                    SourceDir = ToNullIfWhiteSpace(Organize.SourceDir),
                    ZipNamePattern = ToNullIfWhiteSpace(Organize.ZipNamePattern),
                    ModsRoot = ToNullIfWhiteSpace(Organize.ModsRoot),
                    UnifiedModsFolder = ToNullIfWhiteSpace(Organize.UnifiedModsFolder),
                    TrayRoot = ToNullIfWhiteSpace(Organize.TrayRoot),
                    KeepZip = Organize.KeepZip
                };
                return true;

            case SimsAction.Flatten:
                input = new FlattenInput
                {
                    ScriptPath = Path.GetFullPath(scriptPath),
                    WhatIf = WhatIf,
                    FlattenRootPath = ToNullIfWhiteSpace(Flatten.RootPath),
                    FlattenToRoot = Flatten.FlattenToRoot,
                    Shared = shared
                };
                return true;

            case SimsAction.Normalize:
                input = new NormalizeInput
                {
                    ScriptPath = Path.GetFullPath(scriptPath),
                    WhatIf = WhatIf,
                    NormalizeRootPath = ToNullIfWhiteSpace(Normalize.RootPath)
                };
                return true;

            case SimsAction.Merge:
                input = new MergeInput
                {
                    ScriptPath = Path.GetFullPath(scriptPath),
                    WhatIf = WhatIf,
                    MergeSourcePaths = InputParsing.ParseDelimitedList(Merge.SourcePathsText),
                    MergeTargetPath = ToNullIfWhiteSpace(Merge.TargetPath),
                    Shared = shared
                };
                return true;

            case SimsAction.FindDuplicates:
                input = new FindDupInput
                {
                    ScriptPath = Path.GetFullPath(scriptPath),
                    WhatIf = WhatIf,
                    FindDupRootPath = ToNullIfWhiteSpace(FindDup.RootPath),
                    FindDupOutputCsv = ToNullIfWhiteSpace(FindDup.OutputCsv),
                    FindDupRecurse = FindDup.Recurse,
                    FindDupCleanup = FindDup.Cleanup,
                    Shared = shared
                };
                return true;

            case SimsAction.TrayDependencies:
                if (!InputParsing.TryParseOptionalInt(TrayDependencies.MinMatchCountText, 1, 1000, out var minMatchCount, out error))
                {
                    return false;
                }

                if (!InputParsing.TryParseOptionalInt(TrayDependencies.TopNText, 1, 10000, out var topN, out error))
                {
                    return false;
                }

                if (!InputParsing.TryParseOptionalInt(TrayDependencies.MaxPackageCountText, 0, 1000000, out var maxPackageCount, out error))
                {
                    return false;
                }

                input = new TrayDependenciesInput
                {
                    ScriptPath = Path.GetFullPath(scriptPath),
                    WhatIf = WhatIf,
                    TrayPath = ToNullIfWhiteSpace(TrayDependencies.TrayPath),
                    ModsPath = ToNullIfWhiteSpace(TrayDependencies.ModsPath),
                    TrayItemKey = ToNullIfWhiteSpace(TrayDependencies.TrayItemKey),
                    AnalysisMode = ToNullIfWhiteSpace(TrayDependencies.AnalysisMode) ?? "StrictS4TI",
                    S4tiPath = ToNullIfWhiteSpace(TrayDependencies.S4tiPath),
                    MinMatchCount = minMatchCount,
                    TopN = topN,
                    MaxPackageCount = maxPackageCount,
                    ExportUnusedPackages = TrayDependencies.ExportUnusedPackages,
                    ExportMatchedPackages = TrayDependencies.ExportMatchedPackages,
                    OutputCsv = ToNullIfWhiteSpace(TrayDependencies.OutputCsv),
                    UnusedOutputCsv = ToNullIfWhiteSpace(TrayDependencies.UnusedOutputCsv),
                    ExportTargetPath = ToNullIfWhiteSpace(TrayDependencies.ExportTargetPath),
                    ExportMinConfidence = ToNullIfWhiteSpace(TrayDependencies.ExportMinConfidence) ?? "Low"
                };
                return true;
            default:
                error = "Unsupported action for execution.";
                return false;
        }
    }

    private bool TryBuildTrayPreviewInput(out TrayPreviewInput input, out string error)
    {
        input = null!;
        error = string.Empty;

        var trayPath = TrayPreview.TrayRoot.Trim();
        if (string.IsNullOrWhiteSpace(trayPath))
        {
            error = "TrayPath is required for tray preview.";
            return false;
        }

        if (!InputParsing.TryParseOptionalInt(TrayPreview.TopNText, 1, 50000, out var topN, out error))
        {
            return false;
        }

        if (!InputParsing.TryParseOptionalInt(TrayPreview.FilesPerItemText, 1, 200, out var filesPerItem, out error))
        {
            return false;
        }

        input = new TrayPreviewInput
        {
            TrayPath = Path.GetFullPath(trayPath),
            TrayItemKey = TrayPreview.TrayItemKey.Trim(),
            TopN = topN,
            MaxFilesPerItem = filesPerItem ?? 12,
            PageSize = 50
        };
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
            OutputTabIndex = 1;
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
            OutputTabIndex = 1;
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
        return new AppSettings
        {
            ScriptPath = ScriptPath,
            SelectedAction = SelectedAction,
            WhatIf = WhatIf,
            Organize = new AppSettings.OrganizeSettings
            {
                SourceDir = Organize.SourceDir,
                ZipNamePattern = Organize.ZipNamePattern,
                ModsRoot = Organize.ModsRoot,
                UnifiedModsFolder = Organize.UnifiedModsFolder,
                TrayRoot = Organize.TrayRoot,
                KeepZip = Organize.KeepZip
            },
            Flatten = new AppSettings.FlattenSettings
            {
                RootPath = Flatten.RootPath,
                FlattenToRoot = Flatten.FlattenToRoot
            },
            Normalize = new AppSettings.NormalizeSettings
            {
                RootPath = Normalize.RootPath
            },
            Merge = new AppSettings.MergeSettings
            {
                SourcePathsText = Merge.SourcePathsText,
                TargetPath = Merge.TargetPath
            },
            FindDup = new AppSettings.FindDupSettings
            {
                RootPath = FindDup.RootPath,
                OutputCsv = FindDup.OutputCsv,
                Recurse = FindDup.Recurse,
                Cleanup = FindDup.Cleanup
            },
            TrayDependencies = new AppSettings.TrayDependenciesSettings
            {
                TrayPath = TrayDependencies.TrayPath,
                ModsPath = TrayDependencies.ModsPath,
                TrayItemKey = TrayDependencies.TrayItemKey,
                AnalysisMode = TrayDependencies.AnalysisMode,
                S4tiPath = TrayDependencies.S4tiPath,
                MinMatchCountText = TrayDependencies.MinMatchCountText,
                TopNText = TrayDependencies.TopNText,
                MaxPackageCountText = TrayDependencies.MaxPackageCountText,
                ExportUnusedPackages = TrayDependencies.ExportUnusedPackages,
                ExportMatchedPackages = TrayDependencies.ExportMatchedPackages,
                OutputCsv = TrayDependencies.OutputCsv,
                UnusedOutputCsv = TrayDependencies.UnusedOutputCsv,
                ExportTargetPath = TrayDependencies.ExportTargetPath,
                ExportMinConfidence = TrayDependencies.ExportMinConfidence
            },
            TrayPreview = new AppSettings.TrayPreviewSettings
            {
                TrayRoot = TrayPreview.TrayRoot,
                TrayItemKey = TrayPreview.TrayItemKey,
                TopNText = TrayPreview.TopNText,
                FilesPerItemText = TrayPreview.FilesPerItemText
            },
            SharedFileOps = new AppSettings.SharedFileOpsSettings
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

    private void ApplySettings(AppSettings settings)
    {
        ScriptPath = settings.ScriptPath;
        WhatIf = settings.WhatIf;

        Organize.SourceDir = settings.Organize.SourceDir;
        Organize.ZipNamePattern = settings.Organize.ZipNamePattern;
        Organize.ModsRoot = settings.Organize.ModsRoot;
        Organize.UnifiedModsFolder = settings.Organize.UnifiedModsFolder;
        Organize.TrayRoot = settings.Organize.TrayRoot;
        Organize.KeepZip = settings.Organize.KeepZip;

        Flatten.RootPath = settings.Flatten.RootPath;
        Flatten.FlattenToRoot = settings.Flatten.FlattenToRoot;

        Normalize.RootPath = settings.Normalize.RootPath;

        Merge.SourcePathsText = settings.Merge.SourcePathsText;
        Merge.TargetPath = settings.Merge.TargetPath;

        FindDup.RootPath = settings.FindDup.RootPath;
        FindDup.OutputCsv = settings.FindDup.OutputCsv;
        FindDup.Recurse = settings.FindDup.Recurse;
        FindDup.Cleanup = settings.FindDup.Cleanup;

        TrayDependencies.TrayPath = settings.TrayDependencies.TrayPath;
        TrayDependencies.ModsPath = settings.TrayDependencies.ModsPath;
        TrayDependencies.TrayItemKey = settings.TrayDependencies.TrayItemKey;
        TrayDependencies.AnalysisMode = settings.TrayDependencies.AnalysisMode;
        TrayDependencies.S4tiPath = settings.TrayDependencies.S4tiPath;
        TrayDependencies.MinMatchCountText = settings.TrayDependencies.MinMatchCountText;
        TrayDependencies.TopNText = settings.TrayDependencies.TopNText;
        TrayDependencies.MaxPackageCountText = settings.TrayDependencies.MaxPackageCountText;
        TrayDependencies.ExportUnusedPackages = settings.TrayDependencies.ExportUnusedPackages;
        TrayDependencies.ExportMatchedPackages = settings.TrayDependencies.ExportMatchedPackages;
        TrayDependencies.OutputCsv = settings.TrayDependencies.OutputCsv;
        TrayDependencies.UnusedOutputCsv = settings.TrayDependencies.UnusedOutputCsv;
        TrayDependencies.ExportTargetPath = settings.TrayDependencies.ExportTargetPath;
        TrayDependencies.ExportMinConfidence = settings.TrayDependencies.ExportMinConfidence;

        TrayPreview.TrayRoot = settings.TrayPreview.TrayRoot;
        TrayPreview.TrayItemKey = settings.TrayPreview.TrayItemKey;
        TrayPreview.TopNText = settings.TrayPreview.TopNText;
        TrayPreview.FilesPerItemText = settings.TrayPreview.FilesPerItemText;

        SharedFileOps.SkipPruneEmptyDirs = settings.SharedFileOps.SkipPruneEmptyDirs;
        SharedFileOps.ModFilesOnly = settings.SharedFileOps.ModFilesOnly;
        SharedFileOps.VerifyContentOnNameConflict = settings.SharedFileOps.VerifyContentOnNameConflict;
        SharedFileOps.ModExtensionsText = settings.SharedFileOps.ModExtensionsText;
        SharedFileOps.PrefixHashBytesText = settings.SharedFileOps.PrefixHashBytesText;
        SharedFileOps.HashWorkerCountText = settings.SharedFileOps.HashWorkerCountText;

        SelectedAction = settings.SelectedAction;
    }

    private static string? ToNullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static List<string> ParsePathList(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return new List<string>();
        }

        return rawValue
            .Split(new[] { '\r', '\n', ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? TryFindScriptPath()
    {
        var currentDirectoryCandidate = Path.Combine(Directory.GetCurrentDirectory(), "sims-mod-cli.ps1");
        if (File.Exists(currentDirectoryCandidate))
        {
            return currentDirectoryCandidate;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(baseDirectory);
        for (var depth = 0; depth < 10 && directory is not null; depth++)
        {
            var candidate = Path.Combine(directory.FullName, "sims-mod-cli.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
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
