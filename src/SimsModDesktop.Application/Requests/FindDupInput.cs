
namespace SimsModDesktop.Application.Requests;

public sealed record FindDupInput : ISimsExecutionInput
{
    public SimsAction Action => SimsAction.FindDuplicates;
    public bool WhatIf { get; init; }
    public string? FindDupRootPath { get; init; }
    public string? FindDupOutputCsv { get; init; }
    public bool FindDupRecurse { get; init; }
    public bool FindDupCleanup { get; init; }
    public required SharedFileOpsInput Shared { get; init; }
}
