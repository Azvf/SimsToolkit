using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Requests;

public sealed record MergeInput : ISimsExecutionInput
{
    public SimsAction Action => SimsAction.Merge;
    public required string ScriptPath { get; init; }
    public bool WhatIf { get; init; }
    public IReadOnlyList<string> MergeSourcePaths { get; init; } = Array.Empty<string>();
    public string? MergeTargetPath { get; init; }
    public required SharedFileOpsInput Shared { get; init; }
}
