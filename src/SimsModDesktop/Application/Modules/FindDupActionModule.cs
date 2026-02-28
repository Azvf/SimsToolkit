using System.Text.Json.Nodes;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Modules;

public sealed class FindDupActionModule : IActionModule
{
    private static readonly IReadOnlyList<string> ActionPatchKeys =
    [
        "rootPath",
        "outputCsv",
        "recurse",
        "cleanup"
    ];

    private readonly IFindDupModuleState _panel;

    public FindDupActionModule(IFindDupModuleState panel)
    {
        _panel = panel;
    }

    public SimsAction Action => SimsAction.FindDuplicates;
    public string ModuleKey => "finddup";
    public string DisplayName => "FindDuplicates";
    public bool UsesSharedFileOps => true;
    public IReadOnlyCollection<string> SupportedActionPatchKeys => ActionPatchKeys;

    public void LoadFromSettings(AppSettings settings)
    {
        _panel.RootPath = settings.FindDup.RootPath;
        _panel.OutputCsv = settings.FindDup.OutputCsv;
        _panel.Recurse = settings.FindDup.Recurse;
        _panel.Cleanup = settings.FindDup.Cleanup;
    }

    public void SaveToSettings(AppSettings settings)
    {
        settings.FindDup.RootPath = _panel.RootPath;
        settings.FindDup.OutputCsv = _panel.OutputCsv;
        settings.FindDup.Recurse = _panel.Recurse;
        settings.FindDup.Cleanup = _panel.Cleanup;
    }

    public bool TryBuildPlan(GlobalExecutionOptions options, out ModuleExecutionPlan plan, out string error)
    {
        plan = null!;
        if (!ModuleHelpers.TryResolveScriptPath(options.ScriptPath, out var scriptPath, out error))
        {
            return false;
        }

        plan = new CliExecutionPlan(new FindDupInput
        {
            ScriptPath = scriptPath,
            WhatIf = options.WhatIf,
            FindDupRootPath = ModuleHelpers.ToNullIfWhiteSpace(_panel.RootPath),
            FindDupOutputCsv = ModuleHelpers.ToNullIfWhiteSpace(_panel.OutputCsv),
            FindDupRecurse = _panel.Recurse,
            FindDupCleanup = _panel.Cleanup,
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

        if (!ModuleHelpers.TryGetString(patch, "outputCsv", out var hasOutputCsv, out var outputCsv, out error))
        {
            return false;
        }

        if (!ModuleHelpers.TryGetBoolean(patch, "recurse", out var hasRecurse, out var recurse, out error))
        {
            return false;
        }

        if (!ModuleHelpers.TryGetBoolean(patch, "cleanup", out var hasCleanup, out var cleanup, out error))
        {
            return false;
        }

        if (hasRootPath)
        {
            _panel.RootPath = rootPath ?? string.Empty;
        }

        if (hasOutputCsv)
        {
            _panel.OutputCsv = outputCsv ?? string.Empty;
        }

        if (hasRecurse)
        {
            _panel.Recurse = recurse;
        }

        if (hasCleanup)
        {
            _panel.Cleanup = cleanup;
        }

        return true;
    }
}
