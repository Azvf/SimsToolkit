using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void OpenSelectedTrayPreviewPaths() =>
        _trayExportController.OpenSelectedTrayPreviewPaths(CreateTrayExportHost());

    private Task ExportSelectedTrayPreviewFilesAsync() =>
        _trayExportController.ExportSelectedTrayPreviewFilesAsync(CreateTrayExportHost());

    private void SelectAllTrayPreviewPage() =>
        _trayExportController.SelectAllTrayPreviewPage(CreateTrayExportHost());

    private IReadOnlyList<TrayPreviewListItemViewModel> GetSelectedTrayPreviewItems() =>
        _trayPreviewSelectionController.GetSelectedItems(PreviewItems);

    private void ClearCompletedTrayExportTasks() =>
        _trayExportController.ClearCompletedTrayExportTasks(CreateTrayExportHost());

    private void ToggleTrayExportQueue() =>
        _trayExportController.ToggleTrayExportQueue(CreateTrayExportHost());

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
            SubscribeTaskPropertyChanged = (task, handler) => task.PropertyChanged += handler,
            UnsubscribeTaskPropertyChanged = (task, handler) => task.PropertyChanged -= handler
        };
    }
}
