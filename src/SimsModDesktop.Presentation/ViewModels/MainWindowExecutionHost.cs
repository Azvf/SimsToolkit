using SimsModDesktop.Application.Modules;
using SimsModDesktop.Presentation.ViewModels.Panels;

namespace SimsModDesktop.Presentation.ViewModels;

internal sealed class MainWindowExecutionHost
{
    public required SimsAction SelectedAction { get; init; }
    public required TextureCompressPanelViewModel TextureCompress { get; init; }
    public required TrayDependenciesPanelViewModel TrayDependencies { get; init; }
    public required Func<ToolkitPlanningState> CreatePlanBuilderState { get; init; }
    public required Func<Task<bool>> ConfirmDangerousFindDupCleanupAsync { get; init; }
    public required Func<CancellationTokenSource?> GetExecutionCts { get; init; }
    public required Action<CancellationTokenSource?> SetExecutionCts { get; init; }
    public required Action<bool> SetBusy { get; init; }
    public required Action<string> SetStatus { get; init; }
    public required Action<string> AppendLog { get; init; }
    public required Action ClearLog { get; init; }
    public required Action<bool, int, string> SetProgress { get; init; }
    public required Action RefreshValidation { get; init; }
    public required Func<string, Task> ShowErrorPopupAsync { get; init; }
    public required Func<string, string> Localize { get; init; }
    public required Func<string, object[], string> LocalizeFormat { get; init; }
}
