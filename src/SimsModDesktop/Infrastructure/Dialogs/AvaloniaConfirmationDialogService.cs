using Avalonia.Controls;
using SimsModDesktop.Infrastructure.Windowing;
using SimsModDesktop.Views;

namespace SimsModDesktop.Infrastructure.Dialogs;

public sealed class AvaloniaConfirmationDialogService : IConfirmationDialogService
{
    private readonly IWindowHostService _windowHostService;

    public AvaloniaConfirmationDialogService(IWindowHostService windowHostService)
    {
        _windowHostService = windowHostService;
    }

    public async Task<bool> ConfirmAsync(ConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owner = _windowHostService.CurrentTopLevel as Window;
        if (owner is null)
        {
            throw new InvalidOperationException("No active window is available for confirmation dialogs.");
        }

        var dialog = new ConfirmationDialogWindow(request);
        return await dialog.ShowDialog<bool>(owner);
    }
}

