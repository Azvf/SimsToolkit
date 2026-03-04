
namespace SimsModDesktop.Application.Requests;

public sealed record FlattenInput : ISimsExecutionInput
{
    public SimsAction Action => SimsAction.Flatten;
    public bool WhatIf { get; init; }
    public string? FlattenRootPath { get; init; }
    public bool FlattenToRoot { get; init; }
    public required SharedFileOpsInput Shared { get; init; }
}
