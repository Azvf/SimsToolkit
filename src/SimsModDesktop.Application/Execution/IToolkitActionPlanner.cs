using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Application.TextureCompression;

namespace SimsModDesktop.Application.Execution;

public interface IToolkitActionPlanner
{
    IReadOnlyList<SimsAction> AvailableToolkitActions { get; }

    bool UsesSharedFileOps(SimsAction action);

    string GetDisplayName(SimsAction action);

    void LoadModuleSettings(AppSettings settings);

    void SaveModuleSettings(AppSettings settings);

    bool TryBuildToolkitCliPlan(
        MainWindowPlanBuilderState state,
        out CliExecutionPlan plan,
        out string error);

    bool TryBuildTrayDependenciesPlan(
        MainWindowPlanBuilderState state,
        out TrayDependenciesExecutionPlan plan,
        out string error);

    bool TryBuildTrayPreviewInput(
        MainWindowPlanBuilderState state,
        out TrayPreviewInput input,
        out string error);

    bool TryBuildTextureCompressionPlan(
        MainWindowPlanBuilderState state,
        out TextureCompressionExecutionPlan plan,
        out string error);
}
