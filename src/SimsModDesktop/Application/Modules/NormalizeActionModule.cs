using System.Text.Json.Nodes;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.Application.Modules;

public sealed class NormalizeActionModule : IActionModule
{
    private readonly NormalizePanelViewModel _panel;

    public NormalizeActionModule(NormalizePanelViewModel panel)
    {
        _panel = panel;
    }

    public SimsAction Action => SimsAction.Normalize;
    public string ModuleKey => "normalize";
    public string DisplayName => "Normalize";
    public bool UsesSharedFileOps => false;

    public void LoadFromSettings(AppSettings settings)
    {
        _panel.RootPath = settings.Normalize.RootPath;
    }

    public void SaveToSettings(AppSettings settings)
    {
        settings.Normalize.RootPath = _panel.RootPath;
    }

    public bool TryBuildPlan(GlobalExecutionOptions options, out ModuleExecutionPlan plan, out string error)
    {
        plan = null!;
        if (!ModuleHelpers.TryResolveScriptPath(options.ScriptPath, out var scriptPath, out error))
        {
            return false;
        }

        plan = new CliExecutionPlan(new NormalizeInput
        {
            ScriptPath = scriptPath,
            WhatIf = options.WhatIf,
            NormalizeRootPath = ModuleHelpers.ToNullIfWhiteSpace(_panel.RootPath)
        });
        error = string.Empty;
        return true;
    }

    public bool TryApplyActionPatch(JsonObject patch, out string error)
    {
        error = string.Empty;

        if (!ModuleHelpers.TryGetString(patch, "rootPath", out var hasRootPath, out var rootPath, out error))
        {
            return false;
        }

        if (hasRootPath)
        {
            _panel.RootPath = rootPath ?? string.Empty;
        }

        return true;
    }
}
