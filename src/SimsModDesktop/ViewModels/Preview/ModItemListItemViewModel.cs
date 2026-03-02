using SimsModDesktop.Application.Mods;
using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Preview;

public sealed class ModItemListItemViewModel : ObservableObject
{
    private ModItemListRow _item;
    private bool _isSelected;

    public ModItemListItemViewModel(ModItemListRow item)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
    }

    public ModItemListRow Item
    {
        get => _item;
        private set
        {
            if (ReferenceEquals(_item, value))
            {
                return;
            }

            _item = value;
            OnPropertyChanged();
            NotifyRowChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string ItemKey => Item.ItemKey;
    public bool IsPlaceholder => Item.IsPlaceholder;
    public string TypeBadgeText => Item.EntityKind;
    public string ScopeBadgeText => Item.EntitySubType;
    public string DisplayName => Item.DisplayName;
    public string PackageName => Item.PackageName;
    public string PackagePath => Item.PackagePath;
    public string AgeGenderText => Item.AgeGenderText ?? string.Empty;
    public string StableSubtitleText => Item.StableSubtitleText;
    public bool ShowThumbnailPlaceholder => Item.ShowThumbnailPlaceholder || Item.IsPlaceholder;
    public bool ShowMetadataPlaceholder => Item.ShowMetadataPlaceholder || Item.IsPlaceholder;
    public string ThumbnailDetailText => Item.IsPlaceholder ? string.Empty : Item.EntitySubType;
    public string StatusBadgeText => Item.IsPlaceholder ? "Loading..." : Item.TextureSummaryText;
    public string MetaText
    {
        get
        {
            if (Item.IsPlaceholder)
            {
                return "Basic metadata loaded";
            }

            return Item.TextureSummaryText;
        }
    }

    public string PreviewLabelText => Item.IsPlaceholder
        ? "..."
        : Item.EntityKind.Equals("Cas", StringComparison.OrdinalIgnoreCase) ? "CAS" : "BB";

    public void UpdateFrom(ModItemListRow item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
    }

    private void NotifyRowChanged()
    {
        OnPropertyChanged(nameof(ItemKey));
        OnPropertyChanged(nameof(IsPlaceholder));
        OnPropertyChanged(nameof(TypeBadgeText));
        OnPropertyChanged(nameof(ScopeBadgeText));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(PackageName));
        OnPropertyChanged(nameof(PackagePath));
        OnPropertyChanged(nameof(AgeGenderText));
        OnPropertyChanged(nameof(StableSubtitleText));
        OnPropertyChanged(nameof(ShowThumbnailPlaceholder));
        OnPropertyChanged(nameof(ShowMetadataPlaceholder));
        OnPropertyChanged(nameof(ThumbnailDetailText));
        OnPropertyChanged(nameof(StatusBadgeText));
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(PreviewLabelText));
    }
}
