
namespace SimsModDesktop.Application.Requests;

public sealed record NormalizeInput : ISimsExecutionInput
{
    public SimsAction Action => SimsAction.Normalize;
    public bool WhatIf { get; init; }
    public string? NormalizeRootPath { get; init; }
}
