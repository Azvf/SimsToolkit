using Avalonia.Controls;
using SimsModDesktop.Infrastructure.Dialogs;

namespace SimsModDesktop.Views;

public partial class ConfirmationDialogWindow : Window
{
    public ConfirmationDialogWindow()
    {
        InitializeComponent();
    }

    public ConfirmationDialogWindow(ConfirmationRequest request)
        : this()
    {
        DataContext = request;
    }

    private void ConfirmButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(false);
    }
}

