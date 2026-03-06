using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Diagnostics;
using SimsModDesktop.Presentation.Services;
using SimsModDesktop.Presentation.ViewModels.Shell;

namespace SimsModDesktop.Views.Shell;

public partial class MainShellView : UserControl
{
    private const string UiInteractionEvent = "ui.interaction.invoke";
    private MainShellViewModel? _shellVm;
    private bool _hasQueuedFirstContentVisible;
    private readonly ILogger<MainShellView> _logger;
    private readonly IUiActivityMonitor? _uiActivityMonitor;

    public MainShellView()
    {
        InitializeComponent();
        _logger = App.Services?.GetService<ILogger<MainShellView>>() ?? NullLogger<MainShellView>.Instance;
        _uiActivityMonitor = App.Services?.GetService<IUiActivityMonitor>();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => QueueFirstContentVisible();
        AddHandler(PointerPressedEvent, OnUiInteractionObserved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnUiInteractionObserved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
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
            _logger.LogDebug(
                "{Event} status={Status} domain={Domain} control={Control} action={Action} handled={Handled} hasDataContextKey={HasDataContextKey}",
                UiInteractionEvent,
                "invoke",
                "shell",
                "ShellRoot",
                "FocusRoot",
                false,
                _shellVm is not null);
            ShellRoot.Focus();
            return;
        }

        // Keep focus behavior unchanged when interacting with editable controls.
        var current = sourceControl;
        while (current is not null)
        {
            if (current is TextBox or ComboBox)
            {
                _logger.LogDebug(
                    "{Event} status={Status} domain={Domain} control={Control} action={Action} handled={Handled} hasDataContextKey={HasDataContextKey}",
                    UiInteractionEvent,
                    "invoke",
                    "shell",
                    current.GetType().Name,
                    "KeepInputFocus",
                    false,
                    _shellVm is not null);
                return;
            }

            current = current.Parent as Control;
        }

        ShellRoot.Focus();
        _logger.LogDebug(
            "{Event} status={Status} domain={Domain} control={Control} action={Action} handled={Handled} hasDataContextKey={HasDataContextKey}",
            UiInteractionEvent,
            "invoke",
            "shell",
            sourceControl.GetType().Name,
            "FocusRoot",
            false,
            _shellVm is not null);
    }

    private void OnPathHealthPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var shellVm = _shellVm ?? DataContext as MainShellViewModel;
        if (shellVm is null || !shellVm.NavigateToSettingsForPathFixCommand.CanExecute(null))
        {
            return;
        }

        shellVm.NavigateToSettingsForPathFixCommand.Execute(null);
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} control={Control} action={Action} handled={Handled} hasDataContextKey={HasDataContextKey}",
            UiInteractionEvent,
            "invoke",
            "shell",
            "PathHealthBanner",
            "NavigateToSettingsForPathFix",
            true,
            true);
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

    private void QueueFirstContentVisible()
    {
        if (_hasQueuedFirstContentVisible)
        {
            return;
        }

        _hasQueuedFirstContentVisible = true;
        Dispatcher.UIThread.Post(
            () => AppStartupTelemetry.MarkFirstContentVisible(),
            DispatcherPriority.Background);
    }

    private void OnUiInteractionObserved(object? sender, RoutedEventArgs e)
    {
        _uiActivityMonitor?.RecordInteraction();
    }
}
