using SimsModDesktop.Application.Modules;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Execution;

public sealed class ToolkitExecutionRunner : IToolkitExecutionRunner
{
    private readonly IExecutionCoordinator _executionCoordinator;

    public ToolkitExecutionRunner(IExecutionCoordinator executionCoordinator)
    {
        _executionCoordinator = executionCoordinator;
    }

    public async Task<ToolkitRunResult> RunAsync(
        CliExecutionPlan plan,
        Action<string> onOutput,
        Action<SimsProgressUpdate>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(onOutput);

        try
        {
            var result = await _executionCoordinator.ExecuteAsync(
                plan.Input,
                onOutput,
                onProgress,
                cancellationToken);

            return new ToolkitRunResult
            {
                Status = ExecutionRunStatus.Success,
                ExecutionResult = result
            };
        }
        catch (OperationCanceledException)
        {
            return new ToolkitRunResult
            {
                Status = ExecutionRunStatus.Cancelled
            };
        }
        catch (Exception ex)
        {
            return new ToolkitRunResult
            {
                Status = ExecutionRunStatus.Failed,
                ErrorMessage = ex.Message
            };
        }
    }
}
