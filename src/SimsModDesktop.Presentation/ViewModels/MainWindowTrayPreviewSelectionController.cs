using SimsModDesktop.Presentation.ViewModels.Infrastructure;
using SimsModDesktop.Presentation.ViewModels.Panels;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowTrayPreviewSelectionController : ObservableObject
{
    private readonly Stack<TrayPreviewListItemViewModel> _detailHistory = new();
    private readonly HashSet<string> _selectedKeys = new(StringComparer.OrdinalIgnoreCase);
    private TrayPreviewListItemViewModel? _detailItem;
    private string? _selectionAnchorKey;

    public bool HasSelectedItems => _selectedKeys.Count > 0;
    public int SelectedCount => _selectedKeys.Count;
    public bool CanGoBackDetail => _detailHistory.Count > 0;

    public TrayPreviewListItemViewModel? DetailItem
    {
        get => _detailItem;
        private set => SetProperty(ref _detailItem, value);
    }

    public void ApplySelection(
        IReadOnlyList<TrayPreviewListItemViewModel> previewItems,
        TrayPreviewListItemViewModel selectedItem,
        bool controlPressed,
        bool shiftPressed)
    {
        ArgumentNullException.ThrowIfNull(previewItems);
        ArgumentNullException.ThrowIfNull(selectedItem);

        if (!previewItems.Contains(selectedItem))
        {
            return;
        }

        if (shiftPressed)
        {
            ApplyRangeSelection(previewItems, selectedItem, preserveExisting: controlPressed);
            _selectionAnchorKey = BuildSelectionKey(selectedItem.Item);
            return;
        }

        var targetSelected = !selectedItem.IsSelected;
        SetItemSelected(selectedItem, targetSelected);
        _selectionAnchorKey = BuildSelectionKey(selectedItem.Item);
    }

    public void SelectAllPage(IReadOnlyList<TrayPreviewListItemViewModel> previewItems)
    {
        ArgumentNullException.ThrowIfNull(previewItems);

        foreach (var item in previewItems)
        {
            SetItemSelected(item, true);
        }

        if (previewItems.Count > 0)
        {
            _selectionAnchorKey = BuildSelectionKey(previewItems[^1].Item);
        }
    }

    public void ClearSelection(IReadOnlyList<TrayPreviewListItemViewModel> previewItems)
    {
        ArgumentNullException.ThrowIfNull(previewItems);

        foreach (var item in previewItems.Where(item => item.IsSelected))
        {
            item.SetSelected(false);
        }

        if (_selectedKeys.Count == 0 && string.IsNullOrWhiteSpace(_selectionAnchorKey))
        {
            return;
        }

        _selectedKeys.Clear();
        _selectionAnchorKey = null;
        NotifySelectionChanged();
    }

    public IReadOnlyList<TrayPreviewListItemViewModel> GetSelectedItems(IReadOnlyList<TrayPreviewListItemViewModel> previewItems)
    {
        ArgumentNullException.ThrowIfNull(previewItems);
        return previewItems.Where(item => item.IsSelected).ToArray();
    }

    public bool IsItemSelected(SimsTrayPreviewItem item) => _selectedKeys.Contains(BuildSelectionKey(item));

    public void OpenDetails(TrayPreviewListItemViewModel selectedItem)
    {
        ArgumentNullException.ThrowIfNull(selectedItem);

        if (DetailItem is null)
        {
            _detailHistory.Clear();
        }
        else if (!ReferenceEquals(DetailItem, selectedItem))
        {
            _detailHistory.Push(DetailItem);
        }

        DetailItem = selectedItem;
        NotifyDetailChanged();
    }

    public TrayPreviewListItemViewModel? GoBackDetails()
    {
        if (_detailHistory.Count == 0)
        {
            return null;
        }

        var previous = _detailHistory.Pop();
        DetailItem = previous;
        NotifyDetailChanged();
        return previous;
    }

    public void CloseDetails()
    {
        _detailHistory.Clear();
        DetailItem = null;
        NotifyDetailChanged();
    }

    private void ApplyRangeSelection(
        IReadOnlyList<TrayPreviewListItemViewModel> previewItems,
        TrayPreviewListItemViewModel selectedItem,
        bool preserveExisting)
    {
        var targetIndex = FindItemIndex(previewItems, selectedItem);
        if (targetIndex < 0)
        {
            return;
        }

        var anchorIndex = -1;
        if (!string.IsNullOrWhiteSpace(_selectionAnchorKey))
        {
            anchorIndex = previewItems
                .Select((item, index) => new { item, index })
                .Where(entry => string.Equals(
                    BuildSelectionKey(entry.item.Item),
                    _selectionAnchorKey,
                    StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.index)
                .DefaultIfEmpty(-1)
                .First();
        }

        if (anchorIndex < 0)
        {
            if (!preserveExisting)
            {
                ClearSelection(previewItems);
            }

            SetItemSelected(selectedItem, true);
            return;
        }

        if (!preserveExisting)
        {
            ClearSelection(previewItems);
        }

        var startIndex = Math.Min(anchorIndex, targetIndex);
        var endIndex = Math.Max(anchorIndex, targetIndex);
        for (var index = startIndex; index <= endIndex; index++)
        {
            SetItemSelected(previewItems[index], true);
        }
    }

    private void SetItemSelected(TrayPreviewListItemViewModel item, bool selected)
    {
        var key = BuildSelectionKey(item.Item);
        var changed = selected
            ? _selectedKeys.Add(key)
            : _selectedKeys.Remove(key);

        item.SetSelected(selected);

        if (changed)
        {
            NotifySelectionChanged();
        }
    }

    private static string BuildSelectionKey(SimsTrayPreviewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return $"{item.TrayRootPath}|{item.TrayItemKey}";
    }

    private static int FindItemIndex(IReadOnlyList<TrayPreviewListItemViewModel> previewItems, TrayPreviewListItemViewModel selectedItem)
    {
        for (var index = 0; index < previewItems.Count; index++)
        {
            if (ReferenceEquals(previewItems[index], selectedItem))
            {
                return index;
            }
        }

        return -1;
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelectedItems));
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void NotifyDetailChanged()
    {
        OnPropertyChanged(nameof(DetailItem));
        OnPropertyChanged(nameof(CanGoBackDetail));
    }
}
