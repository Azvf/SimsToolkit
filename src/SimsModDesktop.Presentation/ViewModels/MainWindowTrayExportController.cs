using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Presentation.Diagnostics;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowTrayExportController
{
    private readonly ITrayDependencyExportService _trayDependencyExportService;
    private readonly MainWindowCacheWarmupController _cacheWarmupController;
    private readonly ILogger<MainWindowTrayExportController> _logger;
    private MainWindowTrayExportHost? _hookedHost;

    public MainWindowTrayExportController(
        ITrayDependencyExportService trayDependencyExportService,
        MainWindowCacheWarmupController cacheWarmupController,
        ILogger<MainWindowTrayExportController> logger)
    {
        _trayDependencyExportService = trayDependencyExportService;
        _cacheWarmupController = cacheWarmupController;
        _logger = logger;
    }

    internal void OpenSelectedTrayPreviewPaths(MainWindowTrayExportHost host)
    {
        var sourcePaths = GetSelectedTrayPreviewSourceFilePaths(host);
        if (sourcePaths.Count == 0)
        {
            return;
        }

        try
        {
            if (sourcePaths.Count == 1)
            {
                LaunchExplorer(sourcePaths[0], selectFile: true);
                host.SetStatus("Opened selected tray file location.");
                host.AppendLog($"[tray-selection] opened path={sourcePaths[0]}");
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

            host.SetStatus(directories.Count == 1
                ? "Opened selected tray directory."
                : $"Opened {directories.Count} directories for selected tray files.");
            host.AppendLog($"[tray-selection] opened-directories count={directories.Count}");
        }
        catch (Exception ex)
        {
            host.SetStatus("Failed to open selected tray path.");
            host.AppendLog("[tray-selection] open failed: " + ex.Message);
        }
    }

    internal async Task ExportSelectedTrayPreviewFilesAsync(MainWindowTrayExportHost host)
    {
        var selectedItems = host.GetSelectedTrayPreviewItems();
        if (selectedItems.Count == 0)
        {
            return;
        }

        var pickedFolders = await host.PickFolderPathsAsync("Select export folder", false);
        var exportRoot = pickedFolders.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(exportRoot))
        {
            return;
        }

        if (!TryBuildTrayDependencyExportRequests(host, selectedItems, exportRoot, out var dependencyRequests, out var error))
        {
            var setupTask = EnqueueTrayExportTask(host, "Export setup");
            setupTask.SetExportRoot(exportRoot);
            setupTask.MarkFailed(error);
            host.SetStatus(error);
            host.AppendLog("[tray-selection] export blocked: " + error);
            return;
        }

        var queueEntries = dependencyRequests
            .Select(request => (
                request.Item,
                request.ItemExportRoot,
                request.Request,
                Task: EnqueueTrayExportTask(host, request.Item.Item.DisplayTitle)))
            .ToArray();
        using var batchTiming = PerformanceLogScope.Begin(
            _logger,
            "trayexport.batch",
            host.AppendLog,
            ("items", queueEntries.Length),
            ("target", exportRoot),
            ("modsPath", queueEntries[0].Request.ModsRootPath));
        host.SetStatus($"Preparing tray export for {queueEntries.Length} selected item(s)...");
        host.AppendLog(
            $"[tray-selection] export-start items={queueEntries.Length} target={exportRoot} modsPath={queueEntries[0].Request.ModsRootPath}");
        foreach (var queueEntry in queueEntries)
        {
            queueEntry.Task.UpdatePendingProgress(1, "Preparing tray export...");
        }

        foreach (var queueEntry in queueEntries)
        {
            LogTraySelectionItemContext(host, queueEntry.Request);
        }

        if (!_cacheWarmupController.TryGetReadyTraySnapshot(queueEntries[0].Request.ModsRootPath, out var preloadedSnapshot))
        {
            for (var index = 0; index < queueEntries.Length; index++)
            {
                var exportTask = queueEntries[index].Task;
                exportTask.SetExportRoot(queueEntries[index].ItemExportRoot);
                exportTask.MarkFailed(index == 0
                    ? "Tray dependency cache is not ready. Open the Tray page and wait for cache warmup to finish."
                    : "Cancelled after batch failure.");
            }

            host.SetStatus("Export blocked: tray dependency cache is not ready.");
            host.AppendLog("[trayexport.blocked.cache-not-ready]");
            batchTiming.Cancel("cache-not-ready");
            return;
        }
        host.AppendLog(
            $"[tray-selection] using-ready-snapshot modsPath={queueEntries[0].Request.ModsRootPath} packages={preloadedSnapshot.Packages.Count}");

        var copiedTrayFileCount = 0;
        var copiedModFileCount = 0;
        var warningCount = 0;
        var createdItemRoots = new List<string>(dependencyRequests.Count);

        foreach (var queueEntry in queueEntries)
        {
            var exportTask = queueEntry.Task;
            exportTask.SetExportRoot(queueEntry.ItemExportRoot);
            var exportRequest = queueEntry.Request with { PreloadedSnapshot = preloadedSnapshot };
            var lastStage = (TrayDependencyExportStage?)null;
            using var itemTiming = PerformanceLogScope.Begin(
                _logger,
                "trayexport.item",
                host.AppendLog,
                ("trayKey", queueEntry.Request.TrayItemKey),
                ("title", queueEntry.Request.ItemTitle));

            createdItemRoots.Add(queueEntry.ItemExportRoot);

            host.AppendLog(
                $"[tray-selection][item] trayKey={queueEntry.Request.TrayItemKey} export-begin title={queueEntry.Request.ItemTitle}");
            TrayDependencyExportResult result;
            try
            {
                result = await _trayDependencyExportService.ExportAsync(
                    exportRequest,
                    new Progress<TrayDependencyExportProgress>(progress =>
                    {
                        var detail = string.IsNullOrWhiteSpace(progress.Detail)
                            ? progress.Stage.ToString()
                            : progress.Detail.Trim();
                        if (lastStage != progress.Stage)
                        {
                            lastStage = progress.Stage;
                            host.AppendLog(
                                $"[tray-selection][stage] trayKey={queueEntry.Request.TrayItemKey} stage={progress.Stage} percent={progress.Percent} detail={detail}");
                            itemTiming.Mark(
                                "stage",
                                ("trayKey", queueEntry.Request.TrayItemKey),
                                ("stage", progress.Stage.ToString()),
                                ("percent", progress.Percent));
                        }

                        if (progress.Stage == TrayDependencyExportStage.Preparing)
                        {
                            exportTask.MarkTrayRunning(detail);
                            host.SetStatus(detail);
                            return;
                        }

                        if (progress.Stage == TrayDependencyExportStage.Completed)
                        {
                            exportTask.UpdateModsProgress(99, detail);
                            host.SetStatus(detail);
                            return;
                        }

                        exportTask.UpdateModsProgress(progress.Percent, detail);
                        host.SetStatus(detail);
                    }),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                exportTask.MarkFailed("Mods export failed: " + ex.Message);
                exportTask.AppendDetailLine(ex.ToString());
                MarkTrayExportBatchFailed(queueEntries.Select(entry => entry.Task).ToArray(), exportTask, "Rolled back after batch failure.");
                host.AppendLog($"[tray-selection][rollback] start roots={createdItemRoots.Count}");
                await Task.Run(() => RollbackTraySelectionExports(createdItemRoots));
                host.AppendLog($"[tray-selection][rollback] done roots={createdItemRoots.Count}");
                host.SetStatus("Export failed: " + ex.Message);
                host.AppendLog($"[tray-selection][internal] export failed for trayKey={queueEntry.Request.TrayItemKey}: {ex.Message}");
                itemTiming.Fail(ex, "export crashed");
                batchTiming.Fail(ex, "batch aborted after item failure");
                return;
            }

            foreach (var issue in result.Issues)
            {
                exportTask.AppendDetailLine(issue.Message);
                host.AppendLog($"[tray-selection][internal] {issue.Severity}: {issue.Message}");
            }

            host.AppendLog(
                $"[tray-selection][item] trayKey={queueEntry.Request.TrayItemKey} success={result.Success} tray={result.CopiedTrayFileCount} mods={result.CopiedModFileCount} issues={result.Issues.Count}");
            if (result.Diagnostics is not null)
            {
                host.AppendLog(
                    $"[tray-selection][diag] trayKey={queueEntry.Request.TrayItemKey} inputFiles={result.Diagnostics.InputSourceFileCount} bundleTray={result.Diagnostics.BundleTrayItemFileCount} bundleAux={result.Diagnostics.BundleAuxiliaryFileCount} candidateIds={result.Diagnostics.CandidateIdCount} resourceKeys={result.Diagnostics.CandidateResourceKeyCount} packages={result.Diagnostics.SnapshotPackageCount} direct={result.Diagnostics.DirectMatchCount} expanded={result.Diagnostics.ExpandedMatchCount}");
            }
            if (result.CopiedModFileCount == 0)
            {
                host.AppendLog($"[tray-selection][item] trayKey={queueEntry.Request.TrayItemKey} no matched mod files exported");
            }

            if (!result.Success)
            {
                var failure = result.Issues.FirstOrDefault(issue => issue.Severity == TrayDependencyIssueSeverity.Error)?.Message
                              ?? "Unknown error.";
                exportTask.MarkFailed("Mods export failed: " + failure);
                MarkTrayExportBatchFailed(queueEntries.Select(entry => entry.Task).ToArray(), exportTask, "Rolled back after batch failure.");
                host.AppendLog($"[tray-selection][rollback] start roots={createdItemRoots.Count}");
                await Task.Run(() => RollbackTraySelectionExports(createdItemRoots));
                host.AppendLog($"[tray-selection][rollback] done roots={createdItemRoots.Count}");
                host.SetStatus("Export failed: " + failure);
                host.AppendLog($"[tray-selection][internal] export failed for trayKey={queueEntry.Request.TrayItemKey}: {failure}");
                itemTiming.Fail(new InvalidOperationException(failure), "export completed with errors");
                batchTiming.Fail(new InvalidOperationException(failure), "batch aborted after item error");
                return;
            }

            copiedTrayFileCount += result.CopiedTrayFileCount;
            copiedModFileCount += result.CopiedModFileCount;
            if (result.HasMissingReferenceWarnings)
            {
                warningCount++;
                exportTask.MarkCompleted("Completed (missing references ignored).", failed: false);
                itemTiming.Success(
                    "completed with warnings",
                    ("trayFiles", result.CopiedTrayFileCount),
                    ("modFiles", result.CopiedModFileCount),
                    ("issues", result.Issues.Count));
                continue;
            }

            exportTask.MarkCompleted("Completed.", failed: false);
            itemTiming.Success(
                "completed",
                ("trayFiles", result.CopiedTrayFileCount),
                ("modFiles", result.CopiedModFileCount),
                ("issues", result.Issues.Count));
        }

        host.SetStatus(warningCount == 0
            ? $"Exported {copiedTrayFileCount} tray files and {copiedModFileCount} referenced mod files."
            : $"Exported {copiedTrayFileCount} tray files and {copiedModFileCount} referenced mod files ({warningCount} warning item(s) ignored).");

        host.AppendLog($"[tray-selection] export tray={copiedTrayFileCount} mods={copiedModFileCount} items={dependencyRequests.Count} warnings={warningCount} target={exportRoot}");
        batchTiming.Success(
            "batch completed",
            ("trayFiles", copiedTrayFileCount),
            ("modFiles", copiedModFileCount),
            ("warnings", warningCount));
    }

    internal void SelectAllTrayPreviewPage(MainWindowTrayExportHost host)
    {
        if (host.PreviewItems.Count == 0)
        {
            return;
        }

        host.SelectAllTrayPreviewPage();
    }

    internal void ClearCompletedTrayExportTasks(MainWindowTrayExportHost host)
    {
        for (var index = host.TrayExportTasks.Count - 1; index >= 0; index--)
        {
            if (host.TrayExportTasks[index].IsCompleted)
            {
                host.TrayExportTasks.RemoveAt(index);
            }
        }
    }

    internal void ToggleTrayExportQueue(MainWindowTrayExportHost host)
    {
        if (!host.GetHasTrayExportTasks())
        {
            return;
        }

        SetTrayExportQueueExpanded(host, !host.GetIsTrayExportQueueExpanded());
    }

    internal void OpenTrayExportTaskPath(MainWindowTrayExportHost host, TrayExportTaskItemViewModel? task)
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
            host.SetStatus("Failed to open export folder.");
        }
    }

    internal void ToggleTrayExportTaskDetails(TrayExportTaskItemViewModel? task)
    {
        task?.ToggleDetails();
    }

    internal void OnTrayExportTasksChanged(MainWindowTrayExportHost host, object? sender, NotifyCollectionChangedEventArgs e)
    {
        _hookedHost = host;

        if (e.NewItems is { Count: > 0 })
        {
            SetTrayExportQueueExpanded(host, true);
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<TrayExportTaskItemViewModel>())
            {
                host.UnsubscribeTaskPropertyChanged(item, OnTrayExportTaskPropertyChanged);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<TrayExportTaskItemViewModel>())
            {
                host.SubscribeTaskPropertyChanged(item, OnTrayExportTaskPropertyChanged);
            }
        }

        NotifyTrayExportQueueChanged(host);
    }

    private IReadOnlyList<string> GetSelectedTrayPreviewSourceFilePaths(
        MainWindowTrayExportHost host,
        IReadOnlyCollection<TrayPreviewListItemViewModel>? selectedItems = null)
    {
        var source = selectedItems ?? host.GetSelectedTrayPreviewItems();
        return source
            .SelectMany(item => item.Item.SourceFilePaths)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private TrayExportTaskItemViewModel EnqueueTrayExportTask(MainWindowTrayExportHost host, string? title)
    {
        var task = new TrayExportTaskItemViewModel(
            string.IsNullOrWhiteSpace(title)
                ? "Tray Export"
                : title.Trim());
        host.TrayExportTasks.Add(task);
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
            }
        }
    }

    private bool TryBuildTrayDependencyExportRequests(
        MainWindowTrayExportHost host,
        IReadOnlyList<TrayPreviewListItemViewModel> selectedItems,
        string exportRoot,
        out List<(TrayPreviewListItemViewModel Item, string ItemExportRoot, TrayDependencyExportRequest Request)> requests,
        out string error)
    {
        requests = new List<(TrayPreviewListItemViewModel Item, string ItemExportRoot, TrayDependencyExportRequest Request)>();
        error = string.Empty;

        var modsPath = host.TrayDependencies.ModsPath?.Trim() ?? string.Empty;
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

            var traySourceFiles = ResolveTraySourceFiles(selectedItem.Item);
            if (traySourceFiles.Count == 0)
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
                TraySourceFiles = traySourceFiles,
                ModsRootPath = modsPath,
                TrayExportRoot = trayExportRoot,
                ModsExportRoot = modsExportRoot
            }));
        }

        return true;
    }

    private static IReadOnlyList<string> ResolveTraySourceFiles(SimsTrayPreviewItem item)
    {
        var resolved = item.SourceFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var trayPath = item.TrayRootPath?.Trim() ?? string.Empty;
        var trayKey = item.TrayItemKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trayPath) || string.IsNullOrWhiteSpace(trayKey) || !Directory.Exists(trayPath))
        {
            return resolved;
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

    private static void LogTraySelectionItemContext(MainWindowTrayExportHost host, TrayDependencyExportRequest request)
    {
        var sourceFiles = request.TraySourceFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        host.AppendLog(
            $"[tray-selection][item] trayKey={request.TrayItemKey} trayPath={request.TrayRootPath} sourceFiles={sourceFiles.Length} trayOut={request.TrayExportRoot} modsOut={request.ModsExportRoot}");
        foreach (var sourceFile in sourceFiles)
        {
            host.AppendLog($"[tray-selection][item-source] trayKey={request.TrayItemKey} path={sourceFile}");
        }
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

    private void SetTrayExportQueueExpanded(MainWindowTrayExportHost host, bool expanded)
    {
        if (host.GetIsTrayExportQueueExpanded() == expanded)
        {
            return;
        }

        host.SetIsTrayExportQueueExpanded(expanded);
        host.RaisePropertyChanged(nameof(MainWindowViewModel.IsTrayExportQueueVisible));
        host.RaisePropertyChanged(nameof(MainWindowViewModel.TrayExportQueueToggleText));
    }

    private void NotifyTrayExportQueueChanged(MainWindowTrayExportHost host)
    {
        host.RaisePropertyChanged(nameof(MainWindowViewModel.HasTrayExportTasks));
        host.RaisePropertyChanged(nameof(MainWindowViewModel.HasCompletedTrayExportTasks));
        host.RaisePropertyChanged(nameof(MainWindowViewModel.HasRunningTrayExportTasks));
        host.RaisePropertyChanged(nameof(MainWindowViewModel.IsTrayExportQueueDockVisible));
        host.RaisePropertyChanged(nameof(MainWindowViewModel.IsTrayExportQueueVisible));
        host.RaisePropertyChanged(nameof(MainWindowViewModel.TrayExportQueueSummaryText));
        host.RaisePropertyChanged(nameof(MainWindowViewModel.TrayExportQueueToggleText));
        host.NotifyTrayExportCommandsChanged();
    }

    private void OnTrayExportTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_hookedHost is not null)
        {
            NotifyTrayExportQueueChanged(_hookedHost);
        }
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
