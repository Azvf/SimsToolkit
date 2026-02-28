using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Execution;

public sealed record MainWindowPlanBuilderState
{
    public string ScriptPath { get; init; } = string.Empty;
    public bool WhatIf { get; init; }
    public SimsAction SelectedAction { get; init; } = SimsAction.Organize;
    public SharedFileOpsPlanState SharedFileOps { get; init; } = new();
}

public sealed record SharedFileOpsPlanState
{
    public bool SkipPruneEmptyDirs { get; init; }
    public bool ModFilesOnly { get; init; }
    public bool VerifyContentOnNameConflict { get; init; }
    public string ModExtensionsText { get; init; } = ".package,.ts4script";
    public string PrefixHashBytesText { get; init; } = "102400";
    public string HashWorkerCountText { get; init; } = "8";
}
