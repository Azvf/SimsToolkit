using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Views.Preview;

public partial class TrayLikePreviewSurfaceView : UserControl
{
    private TrayLikePreviewSurfaceViewModel? _boundViewModel;

    public TrayLikePreviewSurfaceView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnSurfaceKeyDown, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        DataContextChanged += OnDataContextChanged;
        BindToViewModel(DataContext as TrayLikePreviewSurfaceViewModel);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        BindToViewModel(DataContext as TrayLikePreviewSurfaceViewModel);
    }

    private void BindToViewModel(TrayLikePreviewSurfaceViewModel? viewModel)
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
        if (_boundViewModel is not null)
        {
            _boundViewModel.PreviewItems.CollectionChanged += OnPreviewItemsChanged;
        }
    }

    private void OnPreviewItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not (NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Replace))
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () => PreviewScrollViewer?.ScrollToHome(),
            DispatcherPriority.Background);
    }

    private void OnPreviewItemPointerPressed(object? sender, PointerPressedEventArgs e)
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
        _boundViewModel.ApplySelection(
            item,
            modifiers.HasFlag(KeyModifiers.Control),
            modifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
    }

    private void OnPreviewOpenDetailsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_boundViewModel is null ||
            sender is not Control control ||
            control.DataContext is not TrayPreviewListItemViewModel item)
        {
            return;
        }

        _boundViewModel.OpenDetail(item);
        FocusDetailOverlay();
        e.Handled = true;
    }

    private void OnSurfaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (_boundViewModel is null)
        {
            return;
        }

        if (_boundViewModel.IsDetailVisible)
        {
            if (_boundViewModel.CloseDetailCommand.CanExecute(null) && e.Key == Key.Escape)
            {
                _boundViewModel.CloseDetailCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (_boundViewModel.IsMultipleSelection &&
            e.Key == Key.A &&
            e.KeyModifiers.HasFlag(KeyModifiers.Control) &&
            !IsTextInputSource(e.Source) &&
            _boundViewModel.SelectAllPageCommand.CanExecute(null))
        {
            _boundViewModel.SelectAllPageCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void FocusDetailOverlay()
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (_boundViewModel?.IsDetailVisible == true)
                {
                    DetailOverlay?.Focus();
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
