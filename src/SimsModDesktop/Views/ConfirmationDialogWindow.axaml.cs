using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Presentation.Dialogs;

namespace SimsModDesktop.Views;

public partial class ConfirmationDialogWindow : Window
{
    private const string UiDialogResultEvent = "ui.dialog.result";
    private readonly ILogger<ConfirmationDialogWindow> _logger;

    public ConfirmationDialogWindow()
    {
        InitializeComponent();
        _logger = App.Services?.GetService<ILogger<ConfirmationDialogWindow>>() ?? NullLogger<ConfirmationDialogWindow>.Instance;
    }

    public ConfirmationDialogWindow(ConfirmationRequest request)
        : this()
    {
        DataContext = request;
    }

    private void ConfirmButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} result={Result}",
            UiDialogResultEvent,
            "done",
            "confirmation",
            "confirm");
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} result={Result}",
            UiDialogResultEvent,
            "cancel",
            "confirmation",
            "cancel");
        Close(false);
    }
}

