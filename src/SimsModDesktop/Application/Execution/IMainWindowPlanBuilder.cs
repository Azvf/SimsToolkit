using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Execution;

public interface IMainWindowPlanBuilder
{
    bool TryBuildToolkitCliPlan(
        MainWindowPlanBuilderState state,
        out IActionModule module,
        out CliExecutionPlan plan,
        out string error);

    bool TryBuildTrayPreviewInput(
        MainWindowPlanBuilderState state,
        out TrayPreviewInput input,
        out string error);
}
