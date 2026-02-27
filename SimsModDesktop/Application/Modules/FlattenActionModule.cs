using System.Text.Json.Nodes;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.Application.Modules;

public sealed class FlattenActionModule : IActionModule
{
    private readonly FlattenPanelViewModel _panel;

    public FlattenActionModule(FlattenPanelViewModel panel)
    {
        _panel = panel;
    }

    public SimsAction Action => SimsAction.Flatten;
    public string ModuleKey => "flatten";
    public string DisplayName => "Flatten";
    public bool UsesSharedFileOps => true;

    public void LoadFromSettings(AppSettings settings)
    {
        _panel.RootPath = settings.Flatten.RootPath;
        _panel.FlattenToRoot = settings.Flatten.FlattenToRoot;
    }

    public void SaveToSettings(AppSettings settings)
    {
        settings.Flatten.RootPath = _panel.RootPath;
        settings.Flatten.FlattenToRoot = _panel.FlattenToRoot;
    }

    public bool TryBuildPlan(GlobalExecutionOptions options, out ModuleExecutionPlan plan, out string error)
    {
        plan = null!;
        if (!ModuleHelpers.TryResolveScriptPath(options.ScriptPath, out var scriptPath, out error))
        {
            return false;
        }

        plan = new CliExecutionPlan(new FlattenInput
        {
            ScriptPath = scriptPath,
            WhatIf = options.WhatIf,
            FlattenRootPath = ModuleHelpers.ToNullIfWhiteSpace(_panel.RootPath),
            FlattenToRoot = _panel.FlattenToRoot,
            Shared = options.Shared
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

        if (!ModuleHelpers.TryGetBoolean(patch, "flattenToRoot", out var hasFlattenToRoot, out var flattenToRoot, out error))
        {
            return false;
        }

        if (hasRootPath)
        {
            _panel.RootPath = rootPath ?? string.Empty;
        }

        if (hasFlattenToRoot)
        {
            _panel.FlattenToRoot = flattenToRoot;
        }

        return true;
    }
}
