using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Application.TextureCompression;

namespace SimsModDesktop.Application.Execution;

public sealed class ToolkitActionPlanner : IToolkitActionPlanner
{
    private readonly IActionModuleRegistry _moduleRegistry;
    private readonly IMainWindowPlanBuilder _planBuilder;

    public ToolkitActionPlanner(IActionModuleRegistry moduleRegistry, IMainWindowPlanBuilder planBuilder)
    {
        _moduleRegistry = moduleRegistry;
        _planBuilder = planBuilder;

        AvailableToolkitActions = _moduleRegistry.All
            .Select(module => module.Action)
            .Where(action => action != SimsAction.TrayPreview)
            .Distinct()
            .ToArray();
    }

    public IReadOnlyList<SimsAction> AvailableToolkitActions { get; }

    public bool UsesSharedFileOps(SimsAction action)
    {
        return _moduleRegistry.All.Any(module => module.Action == action && module.UsesSharedFileOps);
    }

    public string GetDisplayName(SimsAction action)
    {
        return _moduleRegistry.Get(action).DisplayName;
    }

    public void LoadModuleSettings(AppSettings settings)
    {
        foreach (var module in _moduleRegistry.All)
        {
            module.LoadFromSettings(settings);
        }
    }

    public void SaveModuleSettings(AppSettings settings)
    {
        foreach (var module in _moduleRegistry.All)
        {
            module.SaveToSettings(settings);
        }
    }

    public bool TryBuildToolkitCliPlan(
        MainWindowPlanBuilderState state,
        out CliExecutionPlan plan,
        out string error)
    {
        return _planBuilder.TryBuildToolkitCliPlan(state, out _, out plan, out error);
    }

    public bool TryBuildTrayDependenciesPlan(
        MainWindowPlanBuilderState state,
        out TrayDependenciesExecutionPlan plan,
        out string error)
    {
        return _planBuilder.TryBuildTrayDependenciesPlan(state, out plan, out error);
    }

    public bool TryBuildTrayPreviewInput(
        MainWindowPlanBuilderState state,
        out TrayPreviewInput input,
        out string error)
    {
        return _planBuilder.TryBuildTrayPreviewInput(state, out input, out error);
    }

    public bool TryBuildTextureCompressionPlan(
        MainWindowPlanBuilderState state,
        out TextureCompressionExecutionPlan plan,
        out string error)
    {
        return _planBuilder.TryBuildTextureCompressionPlan(state, out plan, out error);
    }
}
