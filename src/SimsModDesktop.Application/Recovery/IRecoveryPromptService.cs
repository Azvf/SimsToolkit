namespace SimsModDesktop.Application.Recovery;

public interface IRecoveryPromptService
{
    Task<RecoveryPromptDecision?> PromptAsync(
        IReadOnlyList<RecoverableOperationRecord> records,
        CancellationToken ct = default);
}
