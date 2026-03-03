using SimsModDesktop.Application.Modules;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Presentation.ViewModels;

internal sealed class MainWindowValidationHost
{
    public required OrganizePanelViewModel Organize { get; init; }
    public required FlattenPanelViewModel Flatten { get; init; }
    public required NormalizePanelViewModel Normalize { get; init; }
    public required FindDupPanelViewModel FindDup { get; init; }
    public required TrayDependenciesPanelViewModel TrayDependencies { get; init; }
    public required ModPreviewPanelViewModel ModPreview { get; init; }
    public required TrayPreviewPanelViewModel TrayPreview { get; init; }
    public required SharedFileOpsPanelViewModel SharedFileOps { get; init; }
    public required MergePanelViewModel Merge { get; init; }
    public required Func<SimsAction> GetSelectedAction { get; init; }
    public required Func<AppWorkspace> GetWorkspace { get; init; }
    public required Func<bool> GetIsBusy { get; init; }
    public required Func<bool> GetIsInitialized { get; init; }
    public required Func<bool> GetHasValidModPreviewPath { get; init; }
    public required Func<bool> GetHasValidTrayPreviewPath { get; init; }
    public required Func<bool> GetIsBuildSizeFilterVisible { get; init; }
    public required Func<bool> GetIsHouseholdSizeFilterVisible { get; init; }
    public required Func<ToolkitPlanningState> CreatePlanBuilderState { get; init; }
    public required Action QueueSettingsPersist { get; init; }
    public required Action ClearTrayPreview { get; init; }
    public required Action ApplyTrayPreviewDebugVisibility { get; init; }
    public required Action NotifyTrayPreviewFilterVisibilityChanged { get; init; }
    public required Action<Action> ExecuteOnUi { get; init; }
    public required Action<string> SetValidationSummary { get; init; }
    public required Action<bool> SetHasValidationErrors { get; init; }
    public required Action<string> RaisePropertyChanged { get; init; }
    public required Func<string, string> Localize { get; init; }
    public required Func<string, object[], string> LocalizeFormat { get; init; }
}
