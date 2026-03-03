using System.Diagnostics;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
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

        _trayPreviewSelectionController.SelectAllPage(PreviewItems);
    }

    private IReadOnlyList<TrayPreviewListItemViewModel> GetSelectedTrayPreviewItems()
    {
        return _trayPreviewSelectionController.GetSelectedItems(PreviewItems);
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
}
