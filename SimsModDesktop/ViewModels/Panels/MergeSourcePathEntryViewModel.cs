using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Panels;

public sealed class MergeSourcePathEntryViewModel : ObservableObject
{
    private string _path = string.Empty;

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }
}
