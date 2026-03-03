using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using SimsModDesktop.Presentation.ViewModels.Shell;

namespace SimsModDesktop.Views.Shell;

public partial class MainShellView : UserControl
{
    public static readonly IValueConverter BooleanToHorizontalAlignmentConverter =
        new FuncValueConverter<bool, HorizontalAlignment>(b => b ? HorizontalAlignment.Right : HorizontalAlignment.Center);

    private MainShellViewModel? _shellVm;

    public MainShellView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_shellVm, DataContext))
        {
            return;
        }

        if (_shellVm is not null)
        {
            _shellVm.Ts4RootFocusRequested -= OnTs4RootFocusRequested;
        }

        _shellVm = DataContext as MainShellViewModel;

        if (_shellVm is not null)
        {
            _shellVm.Ts4RootFocusRequested += OnTs4RootFocusRequested;
        }
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

    private void OnPathHealthPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var shellVm = _shellVm ?? DataContext as MainShellViewModel;
        if (shellVm is null || !shellVm.NavigateToSettingsForPathFixCommand.CanExecute(null))
        {
            return;
        }

        shellVm.NavigateToSettingsForPathFixCommand.Execute(null);
        e.Handled = true;
    }

    private void OnTs4RootFocusRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                Ts4RootPathTextBox.Focus();
                Ts4RootPathTextBox.SelectAll();
            },
            DispatcherPriority.Background);
    }
}
