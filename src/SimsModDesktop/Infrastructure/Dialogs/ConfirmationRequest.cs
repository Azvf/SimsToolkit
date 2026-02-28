namespace SimsModDesktop.Infrastructure.Dialogs;

public sealed class ConfirmationRequest
{
    public string Title { get; init; } = "Confirm";
    public string Message { get; init; } = string.Empty;
    public string ConfirmText { get; init; } = "Confirm";
    public string CancelText { get; init; } = "Cancel";
    public bool ShowCancel { get; init; } = true;
    public bool IsDangerous { get; init; }
}

