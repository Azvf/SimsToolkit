using SimsModDesktop.Application.Modules;
using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Panels;

public sealed class FindDupPanelViewModel : ObservableObject, IFindDupModuleState
{
    private string _rootPath = string.Empty;
    private string _outputCsv = string.Empty;
    private bool _recurse = true;
    private bool _cleanup;

    public string RootPath
    {
        get => _rootPath;
        set => SetProperty(ref _rootPath, value);
    }

    public string OutputCsv
    {
        get => _outputCsv;
        set => SetProperty(ref _outputCsv, value);
    }

    public bool Recurse
    {
        get => _recurse;
        set => SetProperty(ref _recurse, value);
    }

    public bool Cleanup
    {
        get => _cleanup;
        set => SetProperty(ref _cleanup, value);
    }
}
