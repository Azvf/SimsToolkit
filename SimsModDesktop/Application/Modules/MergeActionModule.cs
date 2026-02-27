using System.Text.Json.Nodes;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.Application.Modules;

public sealed class MergeActionModule : IActionModule
{
    private readonly MergePanelViewModel _panel;

    public MergeActionModule(MergePanelViewModel panel)
    {
        _panel = panel;
    }

    public SimsAction Action => SimsAction.Merge;
    public string ModuleKey => "merge";
    public string DisplayName => "Merge";
    public bool UsesSharedFileOps => true;

    public void LoadFromSettings(AppSettings settings)
    {
        _panel.ApplySourcePathsText(settings.Merge.SourcePathsText);
        _panel.TargetPath = settings.Merge.TargetPath;
    }

    public void SaveToSettings(AppSettings settings)
    {
        settings.Merge.SourcePathsText = _panel.SerializeSourcePaths();
        settings.Merge.TargetPath = _panel.TargetPath;
    }

    public bool TryBuildPlan(GlobalExecutionOptions options, out ModuleExecutionPlan plan, out string error)
    {
        plan = null!;
        if (!ModuleHelpers.TryResolveScriptPath(options.ScriptPath, out var scriptPath, out error))
        {
            return false;
        }

        plan = new CliExecutionPlan(new MergeInput
        {
            ScriptPath = scriptPath,
            WhatIf = options.WhatIf,
            MergeSourcePaths = _panel.CollectSourcePaths(),
            MergeTargetPath = ModuleHelpers.ToNullIfWhiteSpace(_panel.TargetPath),
            Shared = options.Shared
        });
        error = string.Empty;
        return true;
    }

    public bool TryApplyActionPatch(JsonObject patch, out string error)
    {
        error = string.Empty;

        if (!ModuleHelpers.TryGetStringList(patch, "sourcePaths", out var hasSourcePaths, out var sourcePaths, out error))
        {
            return false;
        }

        if (!ModuleHelpers.TryGetString(patch, "targetPath", out var hasTargetPath, out var targetPath, out error))
        {
            return false;
        }

        if (hasSourcePaths)
        {
            _panel.ReplaceSourcePaths(sourcePaths);
        }

        if (hasTargetPath)
        {
            _panel.TargetPath = targetPath ?? string.Empty;
        }

        return true;
    }
}
