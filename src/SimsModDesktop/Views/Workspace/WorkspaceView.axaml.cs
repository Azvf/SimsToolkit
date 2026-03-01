using System.Collections.Specialized;
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
        Dispatcher.UIThread.Post(
            () => TrayPreviewDetailOverlay?.Focus(),
            DispatcherPriority.Input);
        e.Handled = true;
    }

    private void OnWorkspaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (_boundViewModel is null || !_boundViewModel.IsTrayPreviewDetailVisible)
        {
            return;
        }

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
            Dispatcher.UIThread.Post(
                () => TrayPreviewDetailOverlay?.Focus(),
                DispatcherPriority.Input);
            e.Handled = true;
        }
    }
}
