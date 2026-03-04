using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Modules;

public sealed record GlobalExecutionOptions
{
    public bool WhatIf { get; init; }
    public required SharedFileOpsInput Shared { get; init; }
}
