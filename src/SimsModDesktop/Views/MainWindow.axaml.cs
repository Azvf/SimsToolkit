using Avalonia.Controls;
using SimsModDesktop.Infrastructure.Windowing;
using SimsModDesktop.ViewModels;

namespace SimsModDesktop.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IWindowHostService _windowHostService;
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        _viewModel = null!;
        _windowHostService = null!;
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel, IWindowHostService windowHostService)
        : this()
    {
        _viewModel = viewModel;
        _windowHostService = windowHostService;

        DataContext = _viewModel;

        Opened += OnOpened;
        Closed += OnClosed;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        _windowHostService.CurrentTopLevel = this;
        await _viewModel.InitializeAsync();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _windowHostService.CurrentTopLevel = null;
        _settingsWindow?.Close();
        _settingsWindow = null;
        await _viewModel.PersistSettingsAsync();
    }

    private async void SettingsButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow
        {
            DataContext = _viewModel
        };

        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        await _settingsWindow.ShowDialog(this);
    }
}
