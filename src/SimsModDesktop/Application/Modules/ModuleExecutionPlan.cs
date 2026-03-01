using SimsModDesktop.Application.Requests;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Application.Modules;

public abstract record ModuleExecutionPlan(ModuleExecutionKind Kind);

public sealed record CliExecutionPlan(ISimsExecutionInput Input)
    : ModuleExecutionPlan(ModuleExecutionKind.Cli);

public sealed record TrayPreviewExecutionPlan(TrayPreviewInput Input)
    : ModuleExecutionPlan(ModuleExecutionKind.Client);

public sealed record TrayDependenciesExecutionPlan(TrayDependencyAnalysisRequest Request)
    : ModuleExecutionPlan(ModuleExecutionKind.Client);
