using System.Diagnostics;
using System.ComponentModel;
using Avalonia.Threading;
using SimsModDesktop.PackageCore;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Presentation.Dialogs;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.Presentation.ViewModels.Infrastructure;
using SimsModDesktop.Presentation.ViewModels.Panels;

namespace SimsModDesktop.Presentation.ViewModels.Preview;

public sealed class TrayPreviewWorkspaceViewModel : ObservableObject
{
    private const string TrayPreviewLogSource = "TrayPreview";
    private const string TrayCacheLogSource = "TrayCache";

    private readonly IUiLogSink _uiLogSink;
    private readonly IFileDialogService _fileDialogService;
    private readonly ITrayDependencyExportService _trayDependencyExportService;
    private readonly MainWindowCacheWarmupController _cacheWarmupController;
    private readonly TrayDependenciesPanelViewModel _trayDependencies;
    private readonly IPathIdentityResolver _pathIdentityResolver;
    private bool _isActive;
    private bool _hasPendingRefresh = true;
    private int _isExportRunning;
    private CancellationTokenSource? _workspaceLoadCts;
    private int _workspaceLoadOperationId;
    private bool _isDependencyCacheWarmupBlocking;
    private bool _isTrayDependencyCacheReady;
    private int _trayDependencyCacheWarmupPercent;
    private string _trayDependencyCacheWarmupStageText = string.Empty;
    private string _trayDependencyCacheWarmupDetail = string.Empty;
    private string _lastPersistedLogText = string.Empty;
    private string _logText = "Tray preview ready.";

    public TrayPreviewWorkspaceViewModel(
        TrayPreviewPanelViewModel filter,
        ITrayPreviewCoordinator trayPreviewCoordinator,
        ITrayThumbnailService trayThumbnailService,
        IUiLogSink uiLogSink,
        IFileDialogService fileDialogService,
        ITrayDependencyExportService trayDependencyExportService,
        MainWindowCacheWarmupController cacheWarmupController,
        TrayDependenciesPanelViewModel trayDependencies,
        IPathIdentityResolver? pathIdentityResolver = null)
    {
        Filter = filter;
        Surface = new TrayLikePreviewSurfaceViewModel(trayPreviewCoordinator, trayThumbnailService);
        _uiLogSink = uiLogSink;
        _fileDialogService = fileDialogService;
        _trayDependencyExportService = trayDependencyExportService;
        _cacheWarmupController = cacheWarmupController;
        _trayDependencies = trayDependencies;
        _pathIdentityResolver = pathIdentityResolver ?? new SystemPathIdentityResolver();

        OpenSelectedCommand = new RelayCommand(OpenSelected, () => Surface.HasSelection);
        ExportSelectedCommand = new AsyncRelayCommand(
            ExportSelectedAsync,
            () => Surface.HasSelection && IsTrayDependencyCacheReady && !IsDependencyCacheWarmupBlocking,
            disableWhileRunning: false);

        Surface.Configure(Filter, () => Filter.TrayRoot, PreviewSurfaceSelectionMode.Multiple, autoLoad: false);
        Surface.SetActionButtons(
        [
            new PreviewSurfaceActionButtonViewModel { Label = "Refresh", Command = Surface.RefreshCommand },
            new PreviewSurfaceActionButtonViewModel { Label = "Open Selected", Command = OpenSelectedCommand },
            new PreviewSurfaceActionButtonViewModel { Label = "Select Page", Command = Surface.SelectAllPageCommand },
            new PreviewSurfaceActionButtonViewModel { Label = "Export Selected", Command = ExportSelectedCommand },
            new PreviewSurfaceActionButtonViewModel { Label = "Clear", Command = Surface.ClearSelectionCommand }
        ]);
        Surface.SetFooter("Tray Preview Log", LogText);

        Filter.PropertyChanged += OnFilterPropertyChanged;
        _trayDependencies.PropertyChanged += OnTrayDependenciesPropertyChanged;
        Surface.PropertyChanged += OnSurfacePropertyChanged;
    }

    public TrayPreviewPanelViewModel Filter { get; }
    public TrayLikePreviewSurfaceViewModel Surface { get; }
    public RelayCommand OpenSelectedCommand { get; }
    public AsyncRelayCommand ExportSelectedCommand { get; }

    public bool IsDependencyCacheWarmupBlocking
    {
        get => _isDependencyCacheWarmupBlocking;
        private set
        {
            if (!SetProperty(ref _isDependencyCacheWarmupBlocking, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsInteractionEnabled));
            ExportSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsTrayDependencyCacheReady
    {
        get => _isTrayDependencyCacheReady;
        private set
        {
            if (!SetProperty(ref _isTrayDependencyCacheReady, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsInteractionEnabled));
            ExportSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    public bool IsInteractionEnabled => !IsDependencyCacheWarmupBlocking && IsTrayDependencyCacheReady;

    public int TrayDependencyCacheWarmupPercent
    {
        get => _trayDependencyCacheWarmupPercent;
        private set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref _trayDependencyCacheWarmupPercent, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(TrayDependencyCacheWarmupPercentText));
        }
    }

    public string TrayDependencyCacheWarmupPercentText => $"{TrayDependencyCacheWarmupPercent}%";

    public string TrayDependencyCacheWarmupStageText
    {
        get => _trayDependencyCacheWarmupStageText;
        private set => SetProperty(ref _trayDependencyCacheWarmupStageText, value);
    }

    public string TrayDependencyCacheWarmupDetail
    {
        get => _trayDependencyCacheWarmupDetail;
        private set => SetProperty(ref _trayDependencyCacheWarmupDetail, value);
    }

    public void ResetAfterCacheClear()
    {
        CancelWorkspaceLoad();
        _cacheWarmupController.Reset();
        IsTrayDependencyCacheReady = false;
        SetTrayDependencyCacheWarmupState(false, 0, string.Empty, string.Empty);
        Surface.ResetAfterCacheClear();
    }

    public Task EnsureLoadedAsync(bool forceReload = false)
    {
        var (operationId, cancellationToken) = BeginWorkspaceLoadOperation();
        return EnsureWorkspaceReadyAndLoadAsync(forceReload, operationId, cancellationToken);
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
            CancelWorkspaceLoad();
            SetTrayDependencyCacheWarmupState(
                false,
                TrayDependencyCacheWarmupPercent,
                TrayDependencyCacheWarmupStageText,
                TrayDependencyCacheWarmupDetail);
            Surface.PauseBackgroundLoading();
            return;
        }

        if (!string.IsNullOrWhiteSpace(Filter.TrayRoot) &&
            Directory.Exists(Filter.TrayRoot))
        {
            var forceReload = _hasPendingRefresh;
            _hasPendingRefresh = false;
            if (!forceReload && IsTrayDependencyCacheReady && Surface.HasItems)
            {
                _ = Surface.EnsureLoadedAsync(forceReload: false);
                return;
            }

            QueueWorkspaceLoad(forceReload);
        }
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

            Surface.SetFooter("Tray Preview Log", value);
            PersistTrayPreviewLog(value);
        }
    }

    private void OnFilterPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(TrayPreviewPanelViewModel.TrayRoot), StringComparison.Ordinal))
        {
            _hasPendingRefresh = true;
            if (_isActive)
            {
                _hasPendingRefresh = false;
                QueueWorkspaceLoad(forceReload: true);
            }
        }
    }

    private void OnTrayDependenciesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(TrayDependenciesPanelViewModel.ModsPath), StringComparison.Ordinal))
        {
            return;
        }

        _cacheWarmupController.Reset();
        if (_isActive)
        {
            QueueWorkspaceLoad(forceReload: false);
        }
    }

    private void OnSurfacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(TrayLikePreviewSurfaceViewModel.HasSelection), StringComparison.Ordinal))
        {
            return;
        }

        OpenSelectedCommand.NotifyCanExecuteChanged();
        ExportSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OpenSelected()
    {
        var selectedItems = Surface.GetSelectedItems();
        var sourcePaths = selectedItems
            .SelectMany(item => item.Item.SourceFilePaths)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sourcePaths.Length == 0)
        {
            return;
        }

        try
        {
            if (sourcePaths.Length == 1)
            {
                LaunchExplorer(sourcePaths[0], selectFile: true);
                LogText = $"Opened selected tray file location.{Environment.NewLine}{sourcePaths[0]}";
                return;
            }

            foreach (var directory in sourcePaths
                         .Select(Path.GetDirectoryName)
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                LaunchExplorer(directory!, selectFile: false);
            }

            LogText = $"Opened {sourcePaths.Length} selected tray source files.";
        }
        catch (Exception ex)
        {
            LogText = "Failed to open selected tray path." + Environment.NewLine + ex.Message;
        }
    }

    private (int OperationId, CancellationToken CancellationToken) BeginWorkspaceLoadOperation()
    {
        _workspaceLoadCts?.Cancel();
        _workspaceLoadCts?.Dispose();
        _workspaceLoadCts = new CancellationTokenSource();
        var operationId = Interlocked.Increment(ref _workspaceLoadOperationId);
        return (operationId, _workspaceLoadCts.Token);
    }

    private void QueueWorkspaceLoad(bool forceReload)
    {
        var (operationId, cancellationToken) = BeginWorkspaceLoadOperation();
        _ = EnsureWorkspaceReadyAndLoadAsync(forceReload, operationId, cancellationToken);
    }

    private void CancelWorkspaceLoad()
    {
        _workspaceLoadCts?.Cancel();
        _workspaceLoadCts?.Dispose();
        _workspaceLoadCts = null;
        Interlocked.Increment(ref _workspaceLoadOperationId);
    }

    private bool IsCurrentWorkspaceLoad(int operationId)
    {
        return operationId == Volatile.Read(ref _workspaceLoadOperationId);
    }

    private async Task EnsureWorkspaceReadyAndLoadAsync(
        bool forceReload,
        int operationId,
        CancellationToken cancellationToken)
    {
        if (!IsCurrentWorkspaceLoad(operationId))
        {
            return;
        }

        var modsPath = ResolveDirectoryPath(_trayDependencies.ModsPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(modsPath) || !Directory.Exists(modsPath))
        {
            await ExecuteOnUiAsync(() =>
            {
                if (!IsCurrentWorkspaceLoad(operationId))
                {
                    return;
                }

                IsTrayDependencyCacheReady = false;
                SetTrayDependencyCacheWarmupState(
                    false,
                    0,
                    "Missing Mods Path",
                    "Set a valid Mods Path in Settings before using Tray dependency export.");
                LogText = "Tray dependency cache is unavailable: Mods Path is missing.";
            }).ConfigureAwait(false);
            return;
        }

        await ExecuteOnUiAsync(() =>
        {
            if (!IsCurrentWorkspaceLoad(operationId))
            {
                return;
            }

            IsTrayDependencyCacheReady = false;
            SetTrayDependencyCacheWarmupState(true, 0, "Validate", "Preparing tray dependency cache...");
        }).ConfigureAwait(false);

        try
        {
            await _cacheWarmupController.EnsureTrayWorkspaceReadyAsync(
                modsPath,
                CreateCacheWarmupHost(operationId, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            await ExecuteOnUiAsync(() =>
            {
                if (!IsCurrentWorkspaceLoad(operationId))
                {
                    return;
                }

                IsTrayDependencyCacheReady = true;
                SetTrayDependencyCacheWarmupState(false, 100, string.Empty, string.Empty);
            }).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(Filter.TrayRoot) && Directory.Exists(Filter.TrayRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Surface.EnsureLoadedAsync(forceReload).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await ExecuteOnUiAsync(() =>
            {
                if (!IsCurrentWorkspaceLoad(operationId))
                {
                    return;
                }

                IsTrayDependencyCacheReady = false;
                SetTrayDependencyCacheWarmupState(
                    false,
                    0,
                    "Warmup Failed",
                    "Tray dependency cache warmup failed.");
                LogText = "Tray dependency cache warmup failed." + Environment.NewLine + ex.Message;
            }).ConfigureAwait(false);
        }
    }

    private async Task ExportSelectedAsync()
    {
        if (Interlocked.Exchange(ref _isExportRunning, 1) == 1)
        {
            LogText = "Export is already running. Please wait for completion.";
            return;
        }

        try
        {
            var selectedItems = Surface.GetSelectedItems();
            if (selectedItems.Count == 0)
            {
                return;
            }

            var modsPath = ResolveDirectoryPath(_trayDependencies.ModsPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(modsPath) || !Directory.Exists(modsPath))
            {
                LogText = "Mods Path is missing. Set a valid Mods Path before exporting referenced mods.";
                return;
            }

            if (!_cacheWarmupController.TryGetReadyTraySnapshot(modsPath, out var preloadedSnapshot))
            {
                LogText = "Tray dependency cache is not ready. Re-open the Tray page and wait for cache warmup to finish.";
                return;
            }

            var pickedFolders = await _fileDialogService.PickFolderPathsAsync("Select export folder", allowMultiple: false);
            var exportRoot = pickedFolders.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(exportRoot))
            {
                return;
            }

            var messages = new List<string>
            {
                $"Export root: {exportRoot}"
            };
            var exportedItemCount = 0;
            var exportedModsTotal = 0;

            foreach (var selectedItem in selectedItems)
            {
                var trayKey = selectedItem.Item.TrayItemKey?.Trim() ?? string.Empty;
                var sourceFiles = ResolveTraySourceFiles(selectedItem.Item);
                if (string.IsNullOrWhiteSpace(trayKey) || sourceFiles.Length == 0)
                {
                    messages.Add($"Skipped {selectedItem.Item.DisplayTitle}: invalid tray metadata.");
                    continue;
                }

                var trayPath = ResolveDirectoryPath(selectedItem.Item.TrayRootPath ?? string.Empty);
                if (string.IsNullOrWhiteSpace(trayPath) || !Directory.Exists(trayPath))
                {
                    messages.Add($"Skipped {selectedItem.Item.DisplayTitle}: tray path missing.");
                    continue;
                }

                var itemRoot = Path.Combine(exportRoot, BuildExportDirectoryName(selectedItem.Item));

                messages.Add(
                    $"{selectedItem.Item.DisplayTitle}: trayKey={trayKey}, sourceFiles={sourceFiles.Length}, trayPath={trayPath}, modsPath={modsPath}");
                foreach (var sourceFile in sourceFiles)
                {
                    messages.Add($"{selectedItem.Item.DisplayTitle}: source={sourceFile}");
                }

                var request = new TrayDependencyExportRequest
                {
                    ItemTitle = selectedItem.Item.DisplayTitle,
                    TrayItemKey = trayKey,
                    TrayRootPath = trayPath,
                    TraySourceFiles = sourceFiles,
                    ModsRootPath = modsPath,
                    TrayExportRoot = Path.Combine(itemRoot, "Tray"),
                    ModsExportRoot = Path.Combine(itemRoot, "Mods"),
                    PreloadedSnapshot = preloadedSnapshot
                };

                try
                {
                    var result = await _trayDependencyExportService.ExportAsync(request);
                    if (result.Success)
                    {
                        messages.Add($"{selectedItem.Item.DisplayTitle}: exported {result.CopiedTrayFileCount} tray files, {result.CopiedModFileCount} mods.");
                        if (result.Diagnostics is not null)
                        {
                            messages.Add(
                                $"{selectedItem.Item.DisplayTitle}: diag inputFiles={result.Diagnostics.InputSourceFileCount}, bundleTray={result.Diagnostics.BundleTrayItemFileCount}, bundleAux={result.Diagnostics.BundleAuxiliaryFileCount}, candidateIds={result.Diagnostics.CandidateIdCount}, resourceKeys={result.Diagnostics.CandidateResourceKeyCount}, packages={result.Diagnostics.SnapshotPackageCount}, direct={result.Diagnostics.DirectMatchCount}, expanded={result.Diagnostics.ExpandedMatchCount}");
                        }
                        if (result.CopiedModFileCount == 0)
                        {
                            messages.Add($"{selectedItem.Item.DisplayTitle}: no matched mod files were exported.");
                        }

                        exportedItemCount++;
                        exportedModsTotal += result.CopiedModFileCount;
                        AppendIssueMessages(messages, selectedItem.Item.DisplayTitle, result.Issues);
                    }
                    else
                    {
                        var issue = result.Issues.FirstOrDefault()?.Message ?? "Unknown export failure.";
                        messages.Add($"{selectedItem.Item.DisplayTitle}: export failed - {issue}");
                        if (result.Diagnostics is not null)
                        {
                            messages.Add(
                                $"{selectedItem.Item.DisplayTitle}: diag inputFiles={result.Diagnostics.InputSourceFileCount}, bundleTray={result.Diagnostics.BundleTrayItemFileCount}, bundleAux={result.Diagnostics.BundleAuxiliaryFileCount}, candidateIds={result.Diagnostics.CandidateIdCount}, resourceKeys={result.Diagnostics.CandidateResourceKeyCount}, packages={result.Diagnostics.SnapshotPackageCount}, direct={result.Diagnostics.DirectMatchCount}, expanded={result.Diagnostics.ExpandedMatchCount}");
                        }
                        AppendIssueMessages(messages, selectedItem.Item.DisplayTitle, result.Issues);
                    }
                }
                catch (Exception ex)
                {
                    messages.Add($"{selectedItem.Item.DisplayTitle}: export failed - {ex.Message}");
                    messages.Add($"{selectedItem.Item.DisplayTitle}: exception={ex}");
                }
            }

            messages.Add($"Summary: selected={selectedItems.Count}, exportedItems={exportedItemCount}, exportedMods={exportedModsTotal}");
            LogText = string.Join(Environment.NewLine, messages);
        }
        finally
        {
            Interlocked.Exchange(ref _isExportRunning, 0);
            ExportSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    private static string BuildExportDirectoryName(SimsTrayPreviewItem item)
    {
        var baseName = string.IsNullOrWhiteSpace(item.DisplayTitle) ? "TrayItem" : item.DisplayTitle.Trim();
        var sanitized = string.Concat(baseName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var keySuffix = string.IsNullOrWhiteSpace(item.TrayItemKey) ? "item" : item.TrayItemKey.Trim();
        return $"{sanitized}_{keySuffix}";
    }

    private static void LaunchExplorer(string path, bool selectFile)
    {
        var target = selectFile
            ? $"/select,\"{path}\""
            : $"\"{path}\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = target,
            UseShellExecute = true
        });
    }

    private static void AppendIssueMessages(
        ICollection<string> messages,
        string itemTitle,
        IReadOnlyList<TrayDependencyIssue> issues)
    {
        if (issues.Count == 0)
        {
            return;
        }

        foreach (var issue in issues)
        {
            var kind = issue.Kind.ToString();
            var severity = issue.Severity.ToString();
            var detail = string.IsNullOrWhiteSpace(issue.Message) ? "<no message>" : issue.Message.Trim();
            messages.Add($"{itemTitle}: [{severity}/{kind}] {detail}");
        }
    }

    private string[] ResolveTraySourceFiles(SimsTrayPreviewItem item)
    {
        var resolved = item.SourceFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Where(File.Exists)
            .Select(ResolveFilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var trayPath = ResolveDirectoryPath(item.TrayRootPath ?? string.Empty);
        var trayKey = item.TrayItemKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trayPath) || string.IsNullOrWhiteSpace(trayKey) || !Directory.Exists(trayPath))
        {
            return resolved.ToArray();
        }

        foreach (var extension in SupportedTrayExportExtensions)
        {
            var candidate = Path.Combine(trayPath, trayKey + extension);
            if (File.Exists(candidate))
            {
                resolved.Add(candidate);
            }
        }

        foreach (var candidate in Directory.EnumerateFiles(trayPath, trayKey + ".*", SearchOption.TopDirectoryOnly))
        {
            if (!IsSupportedTrayExportFile(candidate))
            {
                continue;
            }

            resolved.Add(ResolveFilePath(candidate));
        }

        return resolved
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSupportedTrayExportFile(string path)
    {
        var extension = Path.GetExtension(path);
        return SupportedTrayExportExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private void PersistTrayPreviewLog(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            _lastPersistedLogText = string.Empty;
            return;
        }

        if (_lastPersistedLogText.Length == 0)
        {
            _uiLogSink.Append(TrayPreviewLogSource, text);
            _lastPersistedLogText = text;
            return;
        }

        if (text.StartsWith(_lastPersistedLogText, StringComparison.Ordinal))
        {
            var delta = text[_lastPersistedLogText.Length..];
            if (!string.IsNullOrWhiteSpace(delta))
            {
                _uiLogSink.Append(TrayPreviewLogSource, delta);
            }

            _lastPersistedLogText = text;
            return;
        }

        _uiLogSink.Append(TrayPreviewLogSource, "[log-reset]");
        _uiLogSink.Append(TrayPreviewLogSource, text);
        _lastPersistedLogText = text;
    }

    private MainWindowCacheWarmupHost CreateCacheWarmupHost(int operationId, CancellationToken cancellationToken)
    {
        return new MainWindowCacheWarmupHost
        {
            ReportProgress = progress =>
            {
                if (cancellationToken.IsCancellationRequested || !IsCurrentWorkspaceLoad(operationId))
                {
                    return;
                }

                SetTrayDependencyCacheWarmupState(
                    progress.IsBlocking,
                    progress.Percent,
                    string.IsNullOrWhiteSpace(progress.Stage) ? "Warmup" : progress.Stage,
                    progress.Detail);
            },
            AppendLog = message =>
            {
                if (cancellationToken.IsCancellationRequested || !IsCurrentWorkspaceLoad(operationId))
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(message))
                {
                    _uiLogSink.Append(TrayCacheLogSource, message);
                }
            }
        };
    }

    private void SetTrayDependencyCacheWarmupState(bool blocking, int percent, string stageText, string detail)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetTrayDependencyCacheWarmupState(blocking, percent, stageText, detail));
            return;
        }

        IsDependencyCacheWarmupBlocking = blocking;
        TrayDependencyCacheWarmupPercent = percent;
        TrayDependencyCacheWarmupStageText = stageText;
        TrayDependencyCacheWarmupDetail = detail;
    }

    private static Task ExecuteOnUiAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        // Do not block waiting for UI thread pumping in headless test contexts.
        Dispatcher.UIThread.Post(action);
        return Task.CompletedTask;
    }

    private string ResolveDirectoryPath(string path)
    {
        var resolved = _pathIdentityResolver.ResolveDirectory(path);
        if (!string.IsNullOrWhiteSpace(resolved.CanonicalPath))
        {
            return resolved.CanonicalPath;
        }

        if (!string.IsNullOrWhiteSpace(resolved.FullPath))
        {
            return resolved.FullPath;
        }

        return path.Trim().Trim('"');
    }

    private string ResolveFilePath(string path)
    {
        var resolved = _pathIdentityResolver.ResolveFile(path);
        if (!string.IsNullOrWhiteSpace(resolved.CanonicalPath))
        {
            return resolved.CanonicalPath;
        }

        if (!string.IsNullOrWhiteSpace(resolved.FullPath))
        {
            return resolved.FullPath;
        }

        return path.Trim().Trim('"');
    }

    private static readonly string[] SupportedTrayExportExtensions =
    [
        ".trayitem",
        ".hhi",
        ".sgi",
        ".householdbinary",
        ".blueprint",
        ".bpi",
        ".room",
        ".rmi"
    ];
}
