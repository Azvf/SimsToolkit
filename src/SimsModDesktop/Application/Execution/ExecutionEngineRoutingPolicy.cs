using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Execution;

public interface IExecutionEngineRoutingPolicy
{
    Task<EngineRoutingDecision> DecideAsync(SimsAction action, CancellationToken cancellationToken = default);
}

public sealed record EngineRoutingDecision
{
    public required bool UseUnifiedEngine { get; init; }
    public required bool EnableFallbackToPowerShell { get; init; }
    public required bool FallbackOnValidationFailure { get; init; }
}

public sealed class ExecutionEngineRoutingPolicy : IExecutionEngineRoutingPolicy
{
    private readonly IConfigurationProvider _configurationProvider;

    public ExecutionEngineRoutingPolicy(IConfigurationProvider configurationProvider)
    {
        _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
    }

    public async Task<EngineRoutingDecision> DecideAsync(SimsAction action, CancellationToken cancellationToken = default)
    {
        var candidateAction = action is SimsAction.Flatten or SimsAction.Normalize or SimsAction.Merge or SimsAction.Organize;
        if (!candidateAction)
        {
            return new EngineRoutingDecision
            {
                UseUnifiedEngine = false,
                EnableFallbackToPowerShell = true,
                FallbackOnValidationFailure = false
            };
        }

        var useUnified = await GetBoolAsync("Execution.UseUnifiedEngine", true, cancellationToken);
        var fallback = await GetBoolAsync("Execution.EnableFallbackToPowerShell", true, cancellationToken);
        var fallbackOnValidation = await GetBoolAsync("Execution.FallbackOnValidationFailure", false, cancellationToken);

        return new EngineRoutingDecision
        {
            UseUnifiedEngine = useUnified,
            EnableFallbackToPowerShell = fallback,
            FallbackOnValidationFailure = fallbackOnValidation
        };
    }

    private async Task<bool> GetBoolAsync(string key, bool defaultValue, CancellationToken cancellationToken)
    {
        var configured = await _configurationProvider.GetConfigurationAsync<bool?>(key, cancellationToken);
        return configured ?? defaultValue;
    }
}
