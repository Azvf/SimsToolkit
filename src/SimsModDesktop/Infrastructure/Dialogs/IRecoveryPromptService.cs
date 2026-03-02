using SimsModDesktop.Application.Recovery;

namespace SimsModDesktop.Infrastructure.Dialogs;

public interface IRecoveryPromptService
{
    Task<RecoveryPromptDecision?> PromptAsync(
        IReadOnlyList<RecoverableOperationRecord> records,
        CancellationToken ct = default);
}
