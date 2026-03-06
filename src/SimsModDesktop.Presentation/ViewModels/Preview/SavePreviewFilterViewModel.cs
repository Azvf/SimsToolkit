using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Presentation.ViewModels.Preview;

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

    public override TrayPreviewInput BuildInput(PreviewSourceRef previewSource)
    {
        var input = base.BuildInput(previewSource);
        return input with
        {
            PresetTypeFilter = "Household",
            BuildSizeFilter = "All",
            AuthorFilter = string.Empty,
            TimeFilter = "All"
        };
    }
}
