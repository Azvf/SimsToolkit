using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Settings;

public sealed record MainWindowSettingsSnapshot
{
    public string UiLanguageCode { get; init; } = "en-US";
    public string ScriptPath { get; init; } = string.Empty;
    public AppWorkspace Workspace { get; init; } = AppWorkspace.Toolkit;
    public SimsAction SelectedAction { get; init; } = SimsAction.Organize;
    public bool WhatIf { get; init; }
    public AppSettings.SharedFileOpsSettings SharedFileOps { get; init; } = new();
    public AppSettings.UiStateSettings UiState { get; init; } = new();
}

public sealed record MainWindowResolvedSettings
{
    public string UiLanguageCode { get; init; } = "en-US";
    public string ScriptPath { get; init; } = string.Empty;
    public bool WhatIf { get; init; }
    public AppSettings.SharedFileOpsSettings SharedFileOps { get; init; } = new();
    public AppSettings.UiStateSettings UiState { get; init; } = new();
    public SimsAction SelectedAction { get; init; } = SimsAction.Organize;
    public AppWorkspace Workspace { get; init; } = AppWorkspace.Toolkit;
}
