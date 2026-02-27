using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Panels;

public sealed class NormalizePanelViewModel : ObservableObject
{
    private string _rootPath = string.Empty;

    public string RootPath
    {
        get => _rootPath;
        set => SetProperty(ref _rootPath, value);
    }
}
