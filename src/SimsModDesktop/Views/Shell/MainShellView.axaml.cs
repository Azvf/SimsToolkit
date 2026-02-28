using Avalonia.Input;
using Avalonia.Controls;

namespace SimsModDesktop.Views.Shell;

public partial class MainShellView : UserControl
{
    public MainShellView()
    {
        InitializeComponent();
    }

    private void OnShellPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Control sourceControl)
        {
            ShellRoot.Focus();
            return;
        }

        // Keep focus behavior unchanged when interacting with editable controls.
        var current = sourceControl;
        while (current is not null)
        {
            if (current is TextBox or ComboBox)
            {
                return;
            }

            current = current.Parent as Control;
        }

        ShellRoot.Focus();
    }
}
