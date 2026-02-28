using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;
using SimsModDesktop.Services;

namespace SimsModDesktop.Application.Execution;

public sealed class ExecutionCoordinator : IExecutionCoordinator
{
    private readonly ISimsPowerShellRunner _runner;
    private readonly IReadOnlyDictionary<SimsAction, IActionExecutionStrategy> _strategies;

    public ExecutionCoordinator(
        ISimsPowerShellRunner runner,
        IEnumerable<IActionExecutionStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(strategies);

        var allStrategies = strategies.ToList();
        if (allStrategies.Count == 0)
        {
            throw new InvalidOperationException("No execution strategies were registered.");
        }

        var duplicateActions = allStrategies
            .GroupBy(strategy => strategy.Action)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.ToString())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (duplicateActions.Length > 0)
        {
            throw new InvalidOperationException(
                "Duplicate execution strategies were registered: " + string.Join(", ", duplicateActions));
        }

        _runner = runner;
        _strategies = allStrategies.ToDictionary(strategy => strategy.Action);
    }

    public async Task<SimsExecutionResult> ExecuteAsync(
        ISimsExecutionInput input,
        Action<string> onOutput,
        Action<SimsProgressUpdate>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(onOutput);

        var strategy = GetStrategy(input.Action);
        if (!strategy.TryValidate(input, out var validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        var command = strategy.BuildCommand(input);
        return await _runner.RunAsync(command, onOutput, onProgress, cancellationToken);
    }

    private IActionExecutionStrategy GetStrategy(SimsAction action)
    {
        if (_strategies.TryGetValue(action, out var strategy))
        {
            return strategy;
        }

        throw new InvalidOperationException($"Execution strategy is not registered: {action}.");
    }
}
