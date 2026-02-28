using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Execution;

public sealed class MainWindowPlanBuilder : IMainWindowPlanBuilder
{
    private readonly IActionModuleRegistry _moduleRegistry;

    public MainWindowPlanBuilder(IActionModuleRegistry moduleRegistry)
    {
        _moduleRegistry = moduleRegistry;
    }

    public bool TryBuildToolkitCliPlan(
        MainWindowPlanBuilderState state,
        out IActionModule module,
        out CliExecutionPlan plan,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(state);

        module = _moduleRegistry.Get(state.SelectedAction);
        plan = null!;
        error = string.Empty;

        if (!TryBuildGlobalExecutionOptions(state, requireScriptPath: true, includeShared: module.UsesSharedFileOps, out var options, out error))
        {
            return false;
        }

        if (!module.TryBuildPlan(options, out var builtPlan, out error))
        {
            return false;
        }

        if (builtPlan is not CliExecutionPlan cliPlan)
        {
            error = $"Action {state.SelectedAction} is not a CLI action.";
            return false;
        }

        plan = cliPlan;
        return true;
    }

    public bool TryBuildTrayPreviewInput(
        MainWindowPlanBuilderState state,
        out TrayPreviewInput input,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(state);

        input = null!;

        if (!TryBuildGlobalExecutionOptions(state, requireScriptPath: false, includeShared: false, out var options, out error))
        {
            return false;
        }

        var module = _moduleRegistry.Get(SimsAction.TrayPreview);
        if (!module.TryBuildPlan(options, out var plan, out error))
        {
            return false;
        }

        if (plan is not TrayPreviewExecutionPlan trayPreviewPlan)
        {
            error = "Tray preview module returned unsupported execution plan.";
            return false;
        }

        input = trayPreviewPlan.Input;
        return true;
    }

    private static bool TryBuildGlobalExecutionOptions(
        MainWindowPlanBuilderState state,
        bool requireScriptPath,
        bool includeShared,
        out GlobalExecutionOptions options,
        out string error)
    {
        options = null!;
        error = string.Empty;

        var scriptPath = state.ScriptPath.Trim();
        if (requireScriptPath && string.IsNullOrWhiteSpace(scriptPath))
        {
            error = "Script path is required.";
            return false;
        }

        SharedFileOpsInput shared;
        if (includeShared)
        {
            if (!TryBuildSharedFileOpsInput(state.SharedFileOps, out shared, out error))
            {
                return false;
            }
        }
        else
        {
            shared = new SharedFileOpsInput();
        }

        options = new GlobalExecutionOptions
        {
            ScriptPath = scriptPath,
            WhatIf = state.WhatIf,
            Shared = shared
        };
        return true;
    }

    private static bool TryBuildSharedFileOpsInput(SharedFileOpsPlanState state, out SharedFileOpsInput input, out string error)
    {
        input = null!;
        error = string.Empty;

        if (!InputParsing.TryParseOptionalInt(state.PrefixHashBytesText, 1024, 104857600, out var prefixHashBytes, out error))
        {
            return false;
        }

        if (!InputParsing.TryParseOptionalInt(state.HashWorkerCountText, 1, 64, out var hashWorkerCount, out error))
        {
            return false;
        }

        input = new SharedFileOpsInput
        {
            SkipPruneEmptyDirs = state.SkipPruneEmptyDirs,
            ModFilesOnly = state.ModFilesOnly,
            ModExtensions = InputParsing.ParseDelimitedList(state.ModExtensionsText),
            VerifyContentOnNameConflict = state.VerifyContentOnNameConflict,
            PrefixHashBytes = prefixHashBytes,
            HashWorkerCount = hashWorkerCount
        };

        return true;
    }
}
