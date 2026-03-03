using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowTrayExportController
{
    private readonly ITrayDependencyExportService _trayDependencyExportService;
    private MainWindowTrayExportHost? _hookedHost;

    public MainWindowTrayExportController(ITrayDependencyExportService trayDependencyExportService)
    {
        _trayDependencyExportService = trayDependencyExportService;
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
                host.SetStatus("Export failed: " + ex.Message);
                host.AppendLog($"[tray-selection][internal] export failed for trayKey={queueEntry.Request.TrayItemKey}: {ex.Message}");
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
                host.AppendLog($"[tray-selection][internal] {issue.Severity}: {issue.Message}");
            }

            if (!result.Success)
            {
                var failure = result.Issues.FirstOrDefault(issue => issue.Severity == TrayDependencyIssueSeverity.Error)?.Message
                              ?? "Unknown error.";
                exportTask.MarkFailed("Mods export failed: " + failure);
                MarkTrayExportBatchFailed(queueEntries.Select(entry => entry.Task).ToArray(), exportTask, "Rolled back after batch failure.");
                await Task.Run(() => RollbackTraySelectionExports(createdItemRoots));
                host.SetStatus("Export failed: " + failure);
                host.AppendLog($"[tray-selection][internal] export failed for trayKey={queueEntry.Request.TrayItemKey}: {failure}");
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

        host.SetStatus(warningCount == 0
            ? $"Exported {copiedTrayFileCount} tray files and {copiedModFileCount} referenced mod files."
            : $"Exported {copiedTrayFileCount} tray files and {copiedModFileCount} referenced mod files ({warningCount} warning item(s) ignored).");

        host.AppendLog($"[tray-selection] export tray={copiedTrayFileCount} mods={copiedModFileCount} items={dependencyRequests.Count} warnings={warningCount} target={exportRoot}");
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
}
