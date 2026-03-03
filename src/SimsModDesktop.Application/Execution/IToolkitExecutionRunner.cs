using SimsModDesktop.Application.Modules;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Execution;

public interface IToolkitExecutionRunner
{
    Task<ToolkitRunResult> RunAsync(
        CliExecutionPlan plan,
        Action<string> onOutput,
        Action<SimsProgressUpdate>? onProgress = null,
        CancellationToken cancellationToken = default);
}

public sealed record ToolkitRunResult
{
    public ExecutionRunStatus Status { get; init; }
    public SimsExecutionResult? ExecutionResult { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
}
