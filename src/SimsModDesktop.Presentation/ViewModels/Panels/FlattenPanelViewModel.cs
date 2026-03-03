using SimsModDesktop.Application.Modules;
using SimsModDesktop.Presentation.ViewModels.Infrastructure;

namespace SimsModDesktop.Presentation.ViewModels.Panels;

public sealed class FlattenPanelViewModel : ObservableObject, IFlattenModuleState
{
    private string _rootPath = string.Empty;
    private bool _flattenToRoot;

    public string RootPath
    {
        get => _rootPath;
        set => SetProperty(ref _rootPath, value);
    }

    public bool FlattenToRoot
    {
        get => _flattenToRoot;
        set => SetProperty(ref _flattenToRoot, value);
    }
}
