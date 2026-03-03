using System.Collections.ObjectModel;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Presentation.ViewModels;

internal sealed class MainWindowTrayPreviewHost
{
    public required TrayPreviewPanelViewModel TrayPreview { get; init; }
    public required ObservableCollection<TrayPreviewListItemViewModel> PreviewItems { get; init; }
    public required bool IsTrayPreviewWorkspace { get; init; }
    public required Func<bool> GetIsBusy { get; init; }
    public required Func<ToolkitPlanningState> CreatePlanBuilderState { get; init; }
    public required Func<CancellationTokenSource?> GetExecutionCts { get; init; }
    public required Action<CancellationTokenSource?> SetExecutionCts { get; init; }
    public required Func<bool> GetIsTrayPreviewPageLoading { get; init; }
    public required Action<bool> SetTrayPreviewPageLoading { get; init; }
    public required Action<bool> SetBusy { get; init; }
    public required Action<string> SetStatus { get; init; }
    public required Action<string> AppendLog { get; init; }
    public required Action ClearLog { get; init; }
    public required Action<bool, int, string> SetProgress { get; init; }
    public required Action RefreshValidation { get; init; }
    public required Action NotifyCommandStates { get; init; }
    public required Action NotifyTrayPreviewViewStateChanged { get; init; }
    public required Func<string, Task> ShowErrorPopupAsync { get; init; }
    public required Func<string, string> Localize { get; init; }
    public required Func<string, object[], string> LocalizeFormat { get; init; }
    public required Action<Action> ExecuteOnUi { get; init; }
    public required Func<Action, Task> ExecuteOnUiAsync { get; init; }
}
