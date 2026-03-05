using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Presentation.ViewModels;

internal sealed class MainWindowLifecycleHost
{
    public required SharedFileOpsPanelViewModel SharedFileOps { get; init; }
    public required ModPreviewPanelViewModel ModPreview { get; init; }
    public required Func<bool> GetIsInitialized { get; init; }
    public required Action<bool> SetIsInitialized { get; init; }
    public required Func<string> GetSelectedLanguageCode { get; init; }
    public required Action<string> SetSelectedLanguageCode { get; init; }
    public required Func<bool> GetWhatIf { get; init; }
    public required Action<bool> SetWhatIf { get; init; }
    public required Func<SimsAction> GetSelectedAction { get; init; }
    public required Action<SimsAction> SetSelectedAction { get; init; }
    public required Func<AppWorkspace> GetWorkspace { get; init; }
    public required Action<AppWorkspace> SetWorkspace { get; init; }
    public required Func<bool> GetIsToolkitAdvancedOpen { get; init; }
    public required Action<bool> SetIsToolkitAdvancedOpen { get; init; }
    public required IReadOnlyList<SimsAction> AvailableToolkitActions { get; init; }
    public required Action ClearTrayPreview { get; init; }
    public required Action CancelTrayPreviewThumbnailLoading { get; init; }
    public required Action RefreshValidation { get; init; }
    public required Action CancelPendingValidation { get; init; }
    public required Func<CliExecutionPlan, string?, Task> RunToolkitPlanAsync { get; init; }
    public required Func<TrayDependenciesExecutionPlan, string?, Task> RunTrayDependenciesPlanAsync { get; init; }
    public required Func<TrayPreviewInput?, string?, Task> RunTrayPreviewCoreAsync { get; init; }
    public required Action<string> AppendLog { get; init; }
    public required Action<string> SetStatus { get; init; }
    public required Func<string, Task> ShowErrorPopupAsync { get; init; }
    public required Func<string, string> Localize { get; init; }
    public required Func<string, object[], string> LocalizeFormat { get; init; }
}
