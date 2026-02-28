using SimsModDesktop.Application.Cli;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;
using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class ExecutionCoordinatorTests
{
    [Fact]
    public void Constructor_NoStrategies_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ExecutionCoordinator(new FakeRunner(), Array.Empty<IActionExecutionStrategy>()));

        Assert.Contains("No execution strategies were registered", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_DuplicateStrategies_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ExecutionCoordinator(
                new FakeRunner(),
                new IActionExecutionStrategy[]
                {
                    new FakeStrategy(SimsAction.Organize),
                    new FakeStrategy(SimsAction.Organize)
                }));

        Assert.Contains("Duplicate execution strategies were registered", ex.Message, StringComparison.Ordinal);
    }

    private sealed class FakeRunner : ISimsPowerShellRunner
    {
        public Task<SimsExecutionResult> RunAsync(SimsProcessCommand command, Action<string> onOutput, Action<SimsProgressUpdate>? onProgress = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SimsExecutionResult
            {
                ExitCode = 0,
                Executable = "pwsh",
                Arguments = command.Arguments
            });
        }
    }

    private sealed class FakeStrategy : IActionExecutionStrategy
    {
        public FakeStrategy(SimsAction action)
        {
            Action = action;
        }

        public SimsAction Action { get; }

        public bool TryValidate(ISimsExecutionInput input, out string error)
        {
            error = string.Empty;
            return true;
        }

        public SimsProcessCommand BuildCommand(ISimsExecutionInput input)
        {
            return new SimsProcessCommand
            {
                Arguments = Array.Empty<string>()
            };
        }
    }
}
