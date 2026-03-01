using Avalonia.Controls;
using Avalonia.Platform;
using SimsModDesktop.Infrastructure.Windowing;
using SimsModDesktop.ViewModels.Shell;

namespace SimsModDesktop.Views;

public partial class MainWindow : Window
{
    private const double PreferredMinWindowWidth = 980;
    private const double PreferredMinWindowHeight = 580;
    private const double AbsoluteMinWindowWidth = 720;
    private const double AbsoluteMinWindowHeight = 460;
    private const double WorkAreaPadding = 40;
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
        PositionChanged += (_, _) => ApplyResponsiveWindowConstraints(resizeToFit: false);
        ScalingChanged += (_, _) => ApplyResponsiveWindowConstraints(resizeToFit: false);
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        ApplyResponsiveWindowConstraints(resizeToFit: true);
        _windowHostService.CurrentTopLevel = this;
        await _viewModel.InitializeAsync();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _windowHostService.CurrentTopLevel = null;
        await _viewModel.PersistSettingsAsync();
    }

    private void ApplyResponsiveWindowConstraints(bool resizeToFit)
    {
        var screen = Screens?.ScreenFromWindow(this) ?? Screens?.Primary;
        if (screen is null)
        {
            MinWidth = PreferredMinWindowWidth;
            MinHeight = PreferredMinWindowHeight;
            return;
        }

        var scale = screen.Scaling > 0 ? screen.Scaling : 1d;
        var workAreaWidth = screen.WorkingArea.Width / scale;
        var workAreaHeight = screen.WorkingArea.Height / scale;
        var maxUsableWidth = Math.Max(AbsoluteMinWindowWidth, Math.Floor(workAreaWidth - WorkAreaPadding));
        var maxUsableHeight = Math.Max(AbsoluteMinWindowHeight, Math.Floor(workAreaHeight - WorkAreaPadding));

        MinWidth = Math.Min(PreferredMinWindowWidth, maxUsableWidth);
        MinHeight = Math.Min(PreferredMinWindowHeight, maxUsableHeight);

        if (!resizeToFit || WindowState == WindowState.Maximized)
        {
            return;
        }

        if (Width > maxUsableWidth)
        {
            Width = maxUsableWidth;
        }

        if (Height > maxUsableHeight)
        {
            Height = maxUsableHeight;
        }
    }
}
