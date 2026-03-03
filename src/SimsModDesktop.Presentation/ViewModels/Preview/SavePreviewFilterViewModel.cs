using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.ViewModels.Preview;

public sealed class SavePreviewFilterViewModel : TrayLikePreviewFilterViewModel
{
    public SavePreviewFilterViewModel()
    {
        ShowPresetTypeFilter = false;
        ShowBuildSizeFilter = false;
        ShowHouseholdSizeFilter = true;
        ShowAuthorFilter = false;
        ShowTimeFilter = false;
        ShowLayoutMode = true;
        ShowDebugPreview = false;
        SearchWatermark = "Household, member, or zone";
        PageSize = 50;
        PresetTypeFilter = "Household";
    }

    public override TrayPreviewInput BuildInput(string trayPath)
    {
        var input = base.BuildInput(trayPath);
        return input with
        {
            PresetTypeFilter = "Household",
            BuildSizeFilter = "All",
            AuthorFilter = string.Empty,
            TimeFilter = "All"
        };
    }
}
