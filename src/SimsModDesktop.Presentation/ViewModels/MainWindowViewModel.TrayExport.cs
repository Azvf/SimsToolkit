using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;
using SimsModDesktop.Presentation.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void OpenSelectedTrayPreviewPaths()
    {
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} command={Command} selectedCount={SelectedCount}",
            LogEvents.UiCommandInvoke,
            "invoke",
            "main-window",
            "OpenSelectedTrayPreviewPaths",
            SelectedTrayPreviewCount);
        _trayExportController.OpenSelectedTrayPreviewPaths(CreateTrayExportHost());
    }

    private Task ExportSelectedTrayPreviewFilesAsync()
    {
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} command={Command} selectedCount={SelectedCount} cacheReady={CacheReady}",
            LogEvents.UiCommandInvoke,
            "invoke",
            "main-window",
            "ExportSelectedTrayPreviewFiles",
            SelectedTrayPreviewCount,
            TrayPreviewWorkspace.IsTrayDependencyCacheReady);
        return _trayExportController.ExportSelectedTrayPreviewFilesAsync(CreateTrayExportHost());
    }

    private void SelectAllTrayPreviewPage()
    {
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} command={Command} pageItems={PageItems}",
            LogEvents.UiCommandInvoke,
            "invoke",
            "main-window",
            "SelectAllTrayPreviewPage",
            PreviewItems.Count);
        _trayExportController.SelectAllTrayPreviewPage(CreateTrayExportHost());
    }

    private IReadOnlyList<TrayPreviewListItemViewModel> GetSelectedTrayPreviewItems() =>
        _trayPreviewSelectionController.GetSelectedItems(PreviewItems);

    private void ClearCompletedTrayExportTasks()
    {
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} command={Command} taskCount={TaskCount}",
            LogEvents.UiCommandInvoke,
            "invoke",
            "main-window",
            "ClearCompletedTrayExportTasks",
            TrayExportTasks.Count);
        _trayExportController.ClearCompletedTrayExportTasks(CreateTrayExportHost());
    }

    private void ToggleTrayExportQueue()
    {
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} command={Command} expanded={Expanded}",
            LogEvents.UiCommandInvoke,
            "invoke",
            "main-window",
            "ToggleTrayExportQueue",
            _isTrayExportQueueExpanded);
        _trayExportController.ToggleTrayExportQueue(CreateTrayExportHost());
    }

    private void OpenTrayExportTaskPath(TrayExportTaskItemViewModel? task) =>
        _trayExportController.OpenTrayExportTaskPath(CreateTrayExportHost(), task);

    private void ToggleTrayExportTaskDetails(TrayExportTaskItemViewModel? task) =>
        _trayExportController.ToggleTrayExportTaskDetails(task);

    private void NotifyTrayExportCommandsChanged()
    {
        ClearCompletedTrayExportTasksCommand.NotifyCanExecuteChanged();
        ToggleTrayExportQueueCommand.NotifyCanExecuteChanged();
        OpenTrayExportTaskPathCommand.NotifyCanExecuteChanged();
        ToggleTrayExportTaskDetailsCommand.NotifyCanExecuteChanged();
    }

    private MainWindowTrayExportHost CreateTrayExportHost()
    {
        return new MainWindowTrayExportHost
        {
            TrayDependencies = TrayDependencies,
            PreviewItems = PreviewItems,
            TrayExportTasks = TrayExportTasks,
            GetSelectedTrayPreviewItems = GetSelectedTrayPreviewItems,
            SelectAllTrayPreviewPage = () => _trayPreviewSelectionController.SelectAllPage(PreviewItems),
            GetHasTrayExportTasks = () => HasTrayExportTasks,
            GetIsTrayExportQueueExpanded = () => _isTrayExportQueueExpanded,
            SetIsTrayExportQueueExpanded = value => _isTrayExportQueueExpanded = value,
            SetStatus = value => StatusMessage = value,
            AppendLog = AppendLog,
            RaisePropertyChanged = propertyName => OnPropertyChanged(propertyName),
            NotifyTrayExportCommandsChanged = NotifyTrayExportCommandsChanged,
            PickFolderPathsAsync = async (title, allowMultiple) =>
                await _fileDialogService.PickFolderPathsAsync(title, allowMultiple),
            RunOnUiAsync = action =>
            {
                ExecuteOnUi(action);
                return Task.CompletedTask;
            },
            SubscribeTaskPropertyChanged = (task, handler) => task.PropertyChanged += handler,
            UnsubscribeTaskPropertyChanged = (task, handler) => task.PropertyChanged -= handler
        };
    }
}
