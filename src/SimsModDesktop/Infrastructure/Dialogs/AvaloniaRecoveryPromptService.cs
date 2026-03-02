using Avalonia.Controls;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Infrastructure.Windowing;
using SimsModDesktop.Views;

namespace SimsModDesktop.Infrastructure.Dialogs;

public sealed class AvaloniaRecoveryPromptService : IRecoveryPromptService
{
    private readonly IWindowHostService _windowHostService;

    public AvaloniaRecoveryPromptService(IWindowHostService windowHostService)
    {
        _windowHostService = windowHostService;
    }

    public async Task<RecoveryPromptDecision?> PromptAsync(
        IReadOnlyList<RecoverableOperationRecord> records,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        ct.ThrowIfCancellationRequested();

        var owner = _windowHostService.CurrentTopLevel as Window;
        if (owner is null)
        {
            throw new InvalidOperationException("No active window is available for recovery dialogs.");
        }

        var dialog = new RecoveryPromptWindow(records);
        return await dialog.ShowDialog<RecoveryPromptDecision?>(owner);
    }
}
