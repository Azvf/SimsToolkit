namespace SimsModDesktop.Application.Recovery;

public sealed class OperationRecoveryCoordinator : IOperationRecoveryCoordinator
{
    private readonly IOperationRecoveryStore _store;
    private readonly IRecoveryPromptService _promptService;

    public OperationRecoveryCoordinator(
        IOperationRecoveryStore store,
        IRecoveryPromptService promptService)
    {
        _store = store;
        _promptService = promptService;
    }

    public async Task InitializeAndPromptAsync(
        Func<RecoverableOperationRecord, CancellationToken, Task> resumeAsync,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(resumeAsync);

        var records = await _store.GetRecoverableAsync(ct);
        if (records.Count == 0)
        {
            return;
        }

        var latest = records[0];
        var decision = await _promptService.PromptAsync([latest], ct);
        if (decision is null || !string.Equals(decision.OperationId, latest.OperationId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        switch (decision.Action)
        {
            case RecoveryPromptAction.Resume:
                await resumeAsync(latest, ct);
                break;
            case RecoveryPromptAction.Abandon:
                await _store.MarkAbandonedAsync(latest.OperationId, ct);
                break;
            case RecoveryPromptAction.Clear:
                await _store.ClearAsync(latest.OperationId, ct);
                break;
        }
    }

    public Task<string> RegisterPendingAsync(RecoverableOperationPayload payload, CancellationToken ct = default)
    {
        return _store.CreatePendingAsync(payload, ct);
    }

    public Task MarkStartedAsync(string operationId, CancellationToken ct = default)
    {
        return _store.MarkStartedAsync(operationId, ct);
    }

    public Task MarkHeartbeatAsync(string operationId, CancellationToken ct = default)
    {
        return _store.MarkHeartbeatAsync(operationId, ct);
    }

    public Task MarkCompletedAsync(string operationId, RecoverableOperationCompletion completion, CancellationToken ct = default)
    {
        return _store.MarkCompletedAsync(operationId, completion, ct);
    }
}
