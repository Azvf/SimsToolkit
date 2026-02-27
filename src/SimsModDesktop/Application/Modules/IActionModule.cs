using System.Text.Json.Nodes;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Modules;

public interface IActionModule
{
    SimsAction Action { get; }
    string ModuleKey { get; }
    string DisplayName { get; }
    bool UsesSharedFileOps { get; }

    void LoadFromSettings(AppSettings settings);
    void SaveToSettings(AppSettings settings);

    bool TryBuildPlan(
        GlobalExecutionOptions options,
        out ModuleExecutionPlan plan,
        out string error);

    bool TryApplyActionPatch(JsonObject patch, out string error);
}
