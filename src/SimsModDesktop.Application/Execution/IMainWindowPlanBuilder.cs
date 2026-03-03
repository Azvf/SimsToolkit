using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Execution;

public interface IMainWindowPlanBuilder
{
    bool TryBuildToolkitCliPlan(
        MainWindowPlanBuilderState state,
        out IActionModule module,
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
