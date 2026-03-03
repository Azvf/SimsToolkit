using SimsModDesktop.Application.Modules;
using SimsModDesktop.Presentation.ViewModels.Preview;
using SimsModDesktop.Presentation.ViewModels.Infrastructure;

namespace SimsModDesktop.Presentation.ViewModels.Panels;

public sealed class TrayPreviewPanelViewModel : TrayLikePreviewFilterViewModel, ITrayPreviewModuleState
{
    private string _trayRoot = string.Empty;

    public TrayPreviewPanelViewModel()
    {
        ShowPresetTypeFilter = true;
        ShowBuildSizeFilter = false;
        ShowHouseholdSizeFilter = true;
        ShowAuthorFilter = true;
        ShowTimeFilter = true;
        ShowLayoutMode = true;
        ShowDebugPreview = true;
        SearchWatermark = "Item Name or Author ID";
        PropertyChanged += OnSelfPropertyChanged;
        UpdateSizeFilterVisibility();
    }

    public string TrayRoot
    {
        get => _trayRoot;
        set => SetProperty(ref _trayRoot, value);
    }

    private void OnSelfPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(PresetTypeFilter), StringComparison.Ordinal))
        {
            UpdateSizeFilterVisibility();
        }
    }

    private void UpdateSizeFilterVisibility()
    {
        ShowBuildSizeFilter =
            string.Equals(PresetTypeFilter, "Lot", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(PresetTypeFilter, "Room", StringComparison.OrdinalIgnoreCase);
        ShowHouseholdSizeFilter = string.Equals(PresetTypeFilter, "Household", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(PresetTypeFilter, "All", StringComparison.OrdinalIgnoreCase);
    }
}
