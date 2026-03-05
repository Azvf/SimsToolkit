using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Presentation.ViewModels;
using SimsModDesktop.Presentation.ViewModels.Panels;

namespace SimsModDesktop.Views.Workspace;

public partial class WorkspaceView : UserControl
{
    private const string UiInteractionEvent = "ui.interaction.invoke";
    private const string UiShortcutEvent = "ui.shortcut.invoke";
    private MainWindowViewModel? _boundViewModel;
    private readonly ILogger<WorkspaceView> _logger;

    public WorkspaceView()
    {
        InitializeComponent();
        _logger = App.Services?.GetService<ILogger<WorkspaceView>>() ?? NullLogger<WorkspaceView>.Instance;
        AddHandler(KeyDownEvent, OnWorkspaceKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnWorkspacePointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        DataContextChanged += OnWorkspaceDataContextChanged;
        BindToViewModel(DataContext as MainWindowViewModel);
    }

    private void OnWorkspaceDataContextChanged(object? sender, EventArgs e)
    {
        BindToViewModel(DataContext as MainWindowViewModel);
    }

    private void BindToViewModel(MainWindowViewModel? viewModel)
    {
        if (ReferenceEquals(_boundViewModel, viewModel))
        {
            return;
        }

        _boundViewModel = viewModel;
    }

    private void OnTrayPreviewOpenDetailsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control ||
            control.DataContext is not TrayPreviewListItemViewModel item ||
            !item.OpenDetailsCommand.CanExecute(null))
        {
            return;
        }

        item.OpenDetailsCommand.Execute(null);
        _logger.LogDebug(
            "{Event} status={Status} domain={Domain} control={Control} action={Action} handled={Handled} hasDataContextKey={HasDataContextKey}",
            UiInteractionEvent,
            "invoke",
            "workspace",
            "TrayPreviewOpenDetails",
            "OpenDetails",
            true,
            !string.IsNullOrWhiteSpace(item.Item.TrayItemKey));
        FocusTrayPreviewDetailOverlay();
        e.Handled = true;
    }

    private void OnTrayPreviewSelectPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_boundViewModel is null ||
            sender is not Control control ||
            control.DataContext is not TrayPreviewListItemViewModel item)
        {
            return;
        }

        if (sender is not CheckBox && IsSelectionExcludedSource(e.Source))
        {
            return;
        }

        var modifiers = e.KeyModifiers;
        _boundViewModel.ApplyTrayPreviewSelection(
            item,
            modifiers.HasFlag(KeyModifiers.Control),
            modifiers.HasFlag(KeyModifiers.Shift));
        _logger.LogDebug(
            "{Event} status={Status} domain={Domain} control={Control} action={Action} handled={Handled} hasDataContextKey={HasDataContextKey}",
            UiInteractionEvent,
            "invoke",
            "workspace",
            sender is CheckBox ? "TrayPreviewSelectCheckbox" : "TrayPreviewSelectItem",
            "ApplySelection",
            true,
            !string.IsNullOrWhiteSpace(item.Item.TrayItemKey));
        e.Handled = true;
    }

    private void OnWorkspacePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_boundViewModel is null || !_boundViewModel.IsTrayPreviewDetailVisible)
        {
            return;
        }

        FocusTrayPreviewDetailOverlay();
    }

    private void OnTrayExportTaskPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_boundViewModel is null ||
            sender is not Control control ||
            control.DataContext is not TrayExportTaskItemViewModel task ||
            IsSelectionExcludedSource(e.Source))
        {
            return;
        }

        if (_boundViewModel.OpenTrayExportTaskPathCommand.CanExecute(task))
        {
            _boundViewModel.OpenTrayExportTaskPathCommand.Execute(task);
            _logger.LogDebug(
                "{Event} status={Status} domain={Domain} control={Control} action={Action} handled={Handled} hasDataContextKey={HasDataContextKey}",
                UiInteractionEvent,
                "invoke",
                "workspace",
                "TrayExportTaskItem",
                "OpenPath",
                true,
                task.HasExportRoot);
            e.Handled = true;
        }
    }

    private void OnTrayExportTaskDetailsClick(object? sender, RoutedEventArgs e)
    {
        if (_boundViewModel is null ||
            sender is not Control control ||
            control.DataContext is not TrayExportTaskItemViewModel task)
        {
            return;
        }

        if (_boundViewModel.ToggleTrayExportTaskDetailsCommand.CanExecute(task))
        {
            _boundViewModel.ToggleTrayExportTaskDetailsCommand.Execute(task);
            _logger.LogDebug(
                "{Event} status={Status} domain={Domain} control={Control} action={Action} handled={Handled} hasDataContextKey={HasDataContextKey}",
                UiInteractionEvent,
                "invoke",
                "workspace",
                "TrayExportTaskDetailsButton",
                "ToggleDetails",
                true,
                task.HasDetails);
            e.Handled = true;
        }
    }

    private void OnWorkspaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (_boundViewModel is null)
        {
            return;
        }

        if (_boundViewModel.IsTrayPreviewDetailVisible)
        {
            if (e.Key == Key.Escape &&
                _boundViewModel.CloseTrayPreviewDetailCommand.CanExecute(null))
            {
                _boundViewModel.CloseTrayPreviewDetailCommand.Execute(null);
                _logger.LogDebug(
                    "{Event} status={Status} domain={Domain} shortcut={Shortcut} action={Action} handled={Handled}",
                    UiShortcutEvent,
                    "invoke",
                    "workspace",
                    "Escape",
                    "CloseTrayPreviewDetail",
                    true);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Back &&
                _boundViewModel.GoBackTrayPreviewDetailCommand.CanExecute(null))
            {
                _boundViewModel.GoBackTrayPreviewDetailCommand.Execute(null);
                FocusTrayPreviewDetailOverlay();
                _logger.LogDebug(
                    "{Event} status={Status} domain={Domain} shortcut={Shortcut} action={Action} handled={Handled}",
                    UiShortcutEvent,
                    "invoke",
                    "workspace",
                    "Back",
                    "GoBackTrayPreviewDetail",
                    true);
                e.Handled = true;
            }

            return;
        }

        if (_boundViewModel.IsTrayPreviewWorkspace &&
            e.Key == Key.A &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            !IsTextInputSource(e.Source) &&
            _boundViewModel.SelectAllTrayPreviewPageCommand.CanExecute(null))
        {
            _boundViewModel.SelectAllTrayPreviewPageCommand.Execute(null);
            _logger.LogDebug(
                "{Event} status={Status} domain={Domain} shortcut={Shortcut} action={Action} handled={Handled}",
                UiShortcutEvent,
                "invoke",
                "workspace",
                "Ctrl+A",
                "SelectAllTrayPreviewPage",
                true);
            e.Handled = true;
        }
    }

    private void FocusTrayPreviewDetailOverlay()
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_boundViewModel?.IsTrayPreviewDetailVisible == true)
                {
                    TrayPreviewDetailOverlay?.Focus();
                }
            },
            DispatcherPriority.Background);
    }

    private static bool IsSelectionExcludedSource(object? source)
    {
        if (source is not StyledElement element)
        {
            return false;
        }

        StyledElement? current = element;
        while (current is not null)
        {
            if (current is Button || current is CheckBox)
            {
                return true;
            }

            current = current.Parent as StyledElement;
        }

        return false;
    }

    private static bool IsTextInputSource(object? source)
    {
        if (source is not StyledElement element)
        {
            return false;
        }

        StyledElement? current = element;
        while (current is not null)
        {
            if (current is TextBox)
            {
                return true;
            }

            current = current.Parent as StyledElement;
        }

        return false;
    }
}
