using SimsModDesktop.Presentation.ViewModels.Infrastructure;

namespace SimsModDesktop.Presentation.ViewModels.Preview;

public sealed class ModPreviewListItemViewModel : ObservableObject
{
    private bool _isSelected;

    public ModPreviewListItemViewModel(ModPreviewCatalogItem item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
    }

    public ModPreviewCatalogItem Item { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string TypeBadgeText => Item.PackageType;
    public string ScopeBadgeText => Item.Scope;
    public string MetaText => $"{Item.FileSizeText} · {Item.LastUpdatedText}";
    public string ConflictText => Item.ConflictHintText;
    public string PreviewLabelText => string.Equals(Item.PackageType, "Script Mod", StringComparison.OrdinalIgnoreCase)
        ? "TS4SCRIPT"
        : "PACKAGE";
}
