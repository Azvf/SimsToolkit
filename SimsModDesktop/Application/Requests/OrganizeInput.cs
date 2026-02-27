using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Requests;

public sealed record OrganizeInput : ISimsExecutionInput
{
    public SimsAction Action => SimsAction.Organize;
    public required string ScriptPath { get; init; }
    public bool WhatIf { get; init; }
    public string? SourceDir { get; init; }
    public string? ZipNamePattern { get; init; }
    public string? ModsRoot { get; init; }
    public string? UnifiedModsFolder { get; init; }
    public string? TrayRoot { get; init; }
    public bool KeepZip { get; init; }
}
