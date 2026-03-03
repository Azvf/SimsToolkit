using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Requests;

public sealed record NormalizeInput : ISimsExecutionInput
{
    public SimsAction Action => SimsAction.Normalize;
    public required string ScriptPath { get; init; }
    public bool WhatIf { get; init; }
    public string? NormalizeRootPath { get; init; }
}
