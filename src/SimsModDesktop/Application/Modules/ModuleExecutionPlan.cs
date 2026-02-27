using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Modules;

public abstract record ModuleExecutionPlan(ModuleExecutionKind Kind);

public sealed record CliExecutionPlan(ISimsExecutionInput Input)
    : ModuleExecutionPlan(ModuleExecutionKind.Cli);

public sealed record TrayPreviewExecutionPlan(TrayPreviewInput Input)
    : ModuleExecutionPlan(ModuleExecutionKind.Client);
