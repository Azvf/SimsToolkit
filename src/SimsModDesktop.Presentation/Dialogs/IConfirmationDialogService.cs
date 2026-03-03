namespace SimsModDesktop.Presentation.Dialogs;

public interface IConfirmationDialogService
{
    Task<bool> ConfirmAsync(ConfirmationRequest request);
}

