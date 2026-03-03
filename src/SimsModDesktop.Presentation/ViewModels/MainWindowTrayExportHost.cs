using System.Collections.ObjectModel;
using System.ComponentModel;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Presentation.ViewModels;

internal sealed class MainWindowTrayExportHost
{
    public required TrayDependenciesPanelViewModel TrayDependencies { get; init; }
    public required ObservableCollection<TrayPreviewListItemViewModel> PreviewItems { get; init; }
    public required ObservableCollection<TrayExportTaskItemViewModel> TrayExportTasks { get; init; }
    public required Func<IReadOnlyList<TrayPreviewListItemViewModel>> GetSelectedTrayPreviewItems { get; init; }
    public required Action SelectAllTrayPreviewPage { get; init; }
    public required Func<bool> GetHasTrayExportTasks { get; init; }
    public required Func<bool> GetIsTrayExportQueueExpanded { get; init; }
    public required Action<bool> SetIsTrayExportQueueExpanded { get; init; }
    public required Action<string> SetStatus { get; init; }
    public required Action<string> AppendLog { get; init; }
    public required Action<string> RaisePropertyChanged { get; init; }
    public required Action NotifyTrayExportCommandsChanged { get; init; }
    public required Func<string, bool, Task<IReadOnlyList<string>>> PickFolderPathsAsync { get; init; }
    public required Action<TrayExportTaskItemViewModel, PropertyChangedEventHandler> SubscribeTaskPropertyChanged { get; init; }
    public required Action<TrayExportTaskItemViewModel, PropertyChangedEventHandler> UnsubscribeTaskPropertyChanged { get; init; }
}
