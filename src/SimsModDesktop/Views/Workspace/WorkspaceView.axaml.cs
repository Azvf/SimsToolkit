using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SimsModDesktop.ViewModels;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.Views.Workspace;

public partial class WorkspaceView : UserControl
{
    private MainWindowViewModel? _boundViewModel;

    public WorkspaceView()
    {
        InitializeComponent();
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

        if (_boundViewModel is not null)
        {
            _boundViewModel.PreviewItems.CollectionChanged -= OnPreviewItemsChanged;
        }

        _boundViewModel = viewModel;
        if (_boundViewModel is null)
        {
            return;
        }

        _boundViewModel.PreviewItems.CollectionChanged += OnPreviewItemsChanged;
    }

    private void OnPreviewItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not (NotifyCollectionChangedAction.Add or
                             NotifyCollectionChangedAction.Reset or
                             NotifyCollectionChangedAction.Replace))
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () => TrayPreviewScrollViewer?.ScrollToHome(),
            DispatcherPriority.Background);
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
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Back &&
                _boundViewModel.GoBackTrayPreviewDetailCommand.CanExecute(null))
            {
                _boundViewModel.GoBackTrayPreviewDetailCommand.Execute(null);
                FocusTrayPreviewDetailOverlay();
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
