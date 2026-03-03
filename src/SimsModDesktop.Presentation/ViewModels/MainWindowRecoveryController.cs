using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.Results;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowRecoveryController
{
    private readonly IOperationRecoveryCoordinator? _operationRecoveryCoordinator;
    private readonly IActionResultRepository? _actionResultRepository;

    public MainWindowRecoveryController(
        IOperationRecoveryCoordinator? operationRecoveryCoordinator = null,
        IActionResultRepository? actionResultRepository = null)
    {
        _operationRecoveryCoordinator = operationRecoveryCoordinator;
        _actionResultRepository = actionResultRepository;
    }

    public Task<string?> RegisterRecoveryAsync(RecoverableOperationPayload payload)
    {
        if (_operationRecoveryCoordinator is null)
        {
            return Task.FromResult<string?>(null);
        }

        return _operationRecoveryCoordinator.RegisterPendingAsync(payload)!;
    }

    public Task MarkRecoveryStartedAsync(string? operationId)
    {
        if (_operationRecoveryCoordinator is null || string.IsNullOrWhiteSpace(operationId))
        {
            return Task.CompletedTask;
        }

        return _operationRecoveryCoordinator.MarkStartedAsync(operationId);
    }

    public Task MarkRecoveryCompletedAsync(string? operationId, RecoverableOperationCompletion completion)
    {
        if (_operationRecoveryCoordinator is null || string.IsNullOrWhiteSpace(operationId))
        {
            return Task.CompletedTask;
        }

        return _operationRecoveryCoordinator.MarkCompletedAsync(operationId, completion);
    }

    public Task SaveResultHistoryAsync(SimsAction action, string source, string summary, string? relatedOperationId)
    {
        if (_actionResultRepository is null)
        {
            return Task.CompletedTask;
        }

        return _actionResultRepository.SaveAsync(
            new ActionResultEnvelope
            {
                Action = action,
                Source = source,
                GeneratedAtLocal = DateTime.Now,
                Rows =
                [
                    new ActionResultRow
                    {
                        Name = action.ToString(),
                        Status = summary,
                        RawSummary = summary
                    }
                ]
            },
            relatedOperationId);
    }

    public Task InitializeAsync()
    {
        if (_actionResultRepository is null)
        {
            return Task.CompletedTask;
        }

        return _actionResultRepository.InitializeAsync();
    }
}
