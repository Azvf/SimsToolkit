namespace SimsModDesktop.Application.Recovery;

public interface IOperationRecoveryCoordinator
{
    Task InitializeAndPromptAsync(
        Func<RecoverableOperationRecord, CancellationToken, Task> resumeAsync,
        CancellationToken ct = default);

    Task<string> RegisterPendingAsync(RecoverableOperationPayload payload, CancellationToken ct = default);
    Task MarkStartedAsync(string operationId, CancellationToken ct = default);
    Task MarkHeartbeatAsync(string operationId, CancellationToken ct = default);
    Task MarkCompletedAsync(string operationId, RecoverableOperationCompletion completion, CancellationToken ct = default);
}
