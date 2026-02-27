namespace SimsModDesktop.Infrastructure.Dialogs;

public interface IConfirmationDialogService
{
    Task<bool> ConfirmAsync(ConfirmationRequest request);
}

