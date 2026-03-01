using Avalonia.Media.Imaging;
using SimsModDesktop.Models;
using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Panels;

public sealed class TrayPreviewListItemViewModel : ObservableObject, IDisposable
{
    private Bitmap? _thumbnail;
    private bool _isThumbnailLoading;
    private bool _hasThumbnailError;
    private bool _isExpanded;
    private bool _showDebugPreview;
    private readonly Action<TrayPreviewListItemViewModel>? _expandedCallback;
    private readonly Action<TrayPreviewListItemViewModel>? _openDetailsCallback;

    public TrayPreviewListItemViewModel(
        SimsTrayPreviewItem item,
        Action<TrayPreviewListItemViewModel>? expandedCallback = null,
        Action<TrayPreviewListItemViewModel>? openDetailsCallback = null)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _expandedCallback = expandedCallback;
        _openDetailsCallback = openDetailsCallback;
        ChildItems = item.ChildItems
            .Select(child => new TrayPreviewListItemViewModel(child, expandedCallback, openDetailsCallback))
            .ToArray();
        ToggleChildrenCommand = new RelayCommand(ToggleChildren, () => CanToggleChildren);
        OpenDetailsCommand = new RelayCommand(OpenDetails, () => CanOpenDetails);
    }

    public SimsTrayPreviewItem Item { get; }
    public IReadOnlyList<TrayPreviewListItemViewModel> ChildItems { get; }
    public RelayCommand ToggleChildrenCommand { get; }
    public RelayCommand OpenDetailsCommand { get; }

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (ReferenceEquals(_thumbnail, value))
            {
                return;
            }

            var previous = _thumbnail;
            _thumbnail = value;
            previous?.Dispose();
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasThumbnail));
            OnPropertyChanged(nameof(IsThumbnailPlaceholderVisible));
        }
    }

    public bool IsThumbnailLoading
    {
        get => _isThumbnailLoading;
        private set
        {
            if (!SetProperty(ref _isThumbnailLoading, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsThumbnailPlaceholderVisible));
        }
    }

    public bool HasThumbnail => Thumbnail is not null;

    public bool HasThumbnailError
    {
        get => _hasThumbnailError;
        private set => SetProperty(ref _hasThumbnailError, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        private set
        {
            if (!SetProperty(ref _isExpanded, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ToggleChildrenText));
        }
    }

    public bool CanToggleChildren => ChildItems.Count > 0;
    public bool CanOpenDetails => _openDetailsCallback is not null;
    public string ToggleChildrenText => IsExpanded
        ? $"Hide Members ({ChildItems.Count})"
        : $"Show Members ({ChildItems.Count})";
    public bool ShowDebugPreview
    {
        get => _showDebugPreview;
        private set => SetProperty(ref _showDebugPreview, value);
    }
    public bool IsThumbnailPlaceholderVisible => !HasThumbnail && !IsThumbnailLoading;

    public void SetThumbnail(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        HasThumbnailError = false;
        IsThumbnailLoading = false;
        Thumbnail = bitmap;
    }

    public void SetThumbnailUnavailable(bool isError)
    {
        Thumbnail = null;
        HasThumbnailError = isError;
        IsThumbnailLoading = false;
    }

    public void SetThumbnailLoading()
    {
        HasThumbnailError = false;
        IsThumbnailLoading = true;
        Thumbnail = null;
    }

    public IEnumerable<TrayPreviewListItemViewModel> EnumerateSelfAndDescendants()
    {
        yield return this;

        foreach (var child in ChildItems)
        {
            foreach (var descendant in child.EnumerateSelfAndDescendants())
            {
                yield return descendant;
            }
        }
    }

    public void SetDebugPreviewEnabled(bool enabled)
    {
        ShowDebugPreview = enabled;

        foreach (var child in ChildItems)
        {
            child.SetDebugPreviewEnabled(enabled);
        }
    }

    private void ToggleChildren()
    {
        if (!CanToggleChildren)
        {
            return;
        }

        IsExpanded = !IsExpanded;
        if (IsExpanded)
        {
            _expandedCallback?.Invoke(this);
        }
    }

    private void OpenDetails()
    {
        _openDetailsCallback?.Invoke(this);
    }

    public void Dispose()
    {
        foreach (var child in ChildItems)
        {
            child.Dispose();
        }

        var previous = _thumbnail;
        _thumbnail = null;
        previous?.Dispose();
    }
}
