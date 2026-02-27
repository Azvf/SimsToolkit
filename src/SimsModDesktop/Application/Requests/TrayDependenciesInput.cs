using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Requests;

public sealed record TrayDependenciesInput : ISimsExecutionInput
{
    public SimsAction Action => SimsAction.TrayDependencies;
    public required string ScriptPath { get; init; }
    public bool WhatIf { get; init; }
    public string? TrayPath { get; init; }
    public string? ModsPath { get; init; }
    public string? TrayItemKey { get; init; }
    public string AnalysisMode { get; init; } = "StrictS4TI";
    public string? S4tiPath { get; init; }
    public int? MinMatchCount { get; init; }
    public int? TopN { get; init; }
    public int? MaxPackageCount { get; init; }
    public bool ExportUnusedPackages { get; init; }
    public bool ExportMatchedPackages { get; init; }
    public string? OutputCsv { get; init; }
    public string? UnusedOutputCsv { get; init; }
    public string? ExportTargetPath { get; init; }
    public string ExportMinConfidence { get; init; } = "Low";
}
