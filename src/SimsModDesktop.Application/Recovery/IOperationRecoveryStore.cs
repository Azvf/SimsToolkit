namespace SimsModDesktop.Application.Recovery;

public interface IOperationRecoveryStore
{
    Task<string> CreatePendingAsync(RecoverableOperationPayload payload, CancellationToken ct = default);
    Task MarkStartedAsync(string operationId, CancellationToken ct = default);
    Task MarkHeartbeatAsync(string operationId, CancellationToken ct = default);
    Task MarkCompletedAsync(string operationId, RecoverableOperationCompletion completion, CancellationToken ct = default);
    Task<IReadOnlyList<RecoverableOperationRecord>> GetRecoverableAsync(CancellationToken ct = default);
    Task MarkAbandonedAsync(string operationId, CancellationToken ct = default);
    Task ClearAsync(string operationId, CancellationToken ct = default);
}
