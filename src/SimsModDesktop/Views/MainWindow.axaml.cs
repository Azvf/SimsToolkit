using Avalonia.Controls;
using SimsModDesktop.Infrastructure.Windowing;
using SimsModDesktop.ViewModels.Shell;

namespace SimsModDesktop.Views;

public partial class MainWindow : Window
{
    private readonly MainShellViewModel _viewModel;
    private readonly IWindowHostService _windowHostService;

    public MainWindow()
    {
        _viewModel = null!;
        _windowHostService = null!;
        InitializeComponent();
    }

    public MainWindow(MainShellViewModel viewModel, IWindowHostService windowHostService)
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
        await _viewModel.PersistSettingsAsync();
    }
}
