using SimsModDesktop.Presentation.ViewModels.Infrastructure;

namespace SimsModDesktop.Presentation.ViewModels.Panels;

public sealed class MergeSourcePathEntryViewModel : ObservableObject
{
    private string _path = string.Empty;

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }
}
