using System.Diagnostics;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Presentation.Dialogs;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.Presentation.ViewModels.Infrastructure;
using SimsModDesktop.Presentation.ViewModels.Panels;

namespace SimsModDesktop.Presentation.ViewModels.Preview;

public sealed class TrayPreviewWorkspaceViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;
    private readonly ITrayDependencyExportService _trayDependencyExportService;
    private readonly TrayDependenciesPanelViewModel _trayDependencies;
    private bool _isActive;
    private bool _hasPendingRefresh = true;
    private int _isExportRunning;
    private string _lastPersistedLogText = string.Empty;
    private string _logText = "Tray preview ready.";

    public TrayPreviewWorkspaceViewModel(
        TrayPreviewPanelViewModel filter,
        ITrayPreviewCoordinator trayPreviewCoordinator,
        ITrayThumbnailService trayThumbnailService,
        IFileDialogService fileDialogService,
        ITrayDependencyExportService trayDependencyExportService,
        TrayDependenciesPanelViewModel trayDependencies)
    {
        Filter = filter;
        Surface = new TrayLikePreviewSurfaceViewModel(trayPreviewCoordinator, trayThumbnailService);
        _fileDialogService = fileDialogService;
        _trayDependencyExportService = trayDependencyExportService;
        _trayDependencies = trayDependencies;

        OpenSelectedCommand = new RelayCommand(OpenSelected, () => Surface.HasSelection);
        ExportSelectedCommand = new AsyncRelayCommand(
            ExportSelectedAsync,
            () => Surface.HasSelection,
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
        Surface.PropertyChanged += OnSurfacePropertyChanged;
    }

    public TrayPreviewPanelViewModel Filter { get; }
    public TrayLikePreviewSurfaceViewModel Surface { get; }
    public RelayCommand OpenSelectedCommand { get; }
    public AsyncRelayCommand ExportSelectedCommand { get; }

    public void ResetAfterCacheClear()
    {
        Surface.ResetAfterCacheClear();
    }

    public Task EnsureLoadedAsync(bool forceReload = false)
    {
        return Surface.EnsureLoadedAsync(forceReload);
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
            Surface.PauseBackgroundLoading();
            return;
        }

        if (!string.IsNullOrWhiteSpace(Filter.TrayRoot) &&
            Directory.Exists(Filter.TrayRoot))
        {
            var forceReload = _hasPendingRefresh;
            _hasPendingRefresh = false;
            _ = Surface.EnsureLoadedAsync(forceReload);
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
                Surface.NotifyTrayPathChanged();
            }
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

            var modsPath = _trayDependencies.ModsPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(modsPath) || !Directory.Exists(modsPath))
            {
                LogText = "Mods Path is missing. Set a valid Mods Path before exporting referenced mods.";
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

                var trayPath = selectedItem.Item.TrayRootPath?.Trim() ?? string.Empty;
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
                    ModsExportRoot = Path.Combine(itemRoot, "Mods")
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

    private static string[] ResolveTraySourceFiles(SimsTrayPreviewItem item)
    {
        var resolved = item.SourceFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var trayPath = item.TrayRootPath?.Trim() ?? string.Empty;
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

            resolved.Add(candidate);
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
            PersistentUiLog.Append("TrayPreview", text);
            _lastPersistedLogText = text;
            return;
        }

        if (text.StartsWith(_lastPersistedLogText, StringComparison.Ordinal))
        {
            var delta = text[_lastPersistedLogText.Length..];
            if (!string.IsNullOrWhiteSpace(delta))
            {
                PersistentUiLog.Append("TrayPreview", delta);
            }

            _lastPersistedLogText = text;
            return;
        }

        PersistentUiLog.Append("TrayPreview", "[log-reset]");
        PersistentUiLog.Append("TrayPreview", text);
        _lastPersistedLogText = text;
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
