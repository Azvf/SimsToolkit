using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Panels;

public sealed class OrganizePanelViewModel : ObservableObject
{
    private string _sourceDir = string.Empty;
    private string _zipNamePattern = "*";
    private string _modsRoot = string.Empty;
    private string _unifiedModsFolder = string.Empty;
    private string _trayRoot = string.Empty;
    private bool _keepZip;

    public string SourceDir
    {
        get => _sourceDir;
        set => SetProperty(ref _sourceDir, value);
    }

    public string ZipNamePattern
    {
        get => _zipNamePattern;
        set => SetProperty(ref _zipNamePattern, value);
    }

    public string ModsRoot
    {
        get => _modsRoot;
        set => SetProperty(ref _modsRoot, value);
    }

    public string UnifiedModsFolder
    {
        get => _unifiedModsFolder;
        set => SetProperty(ref _unifiedModsFolder, value);
    }

    public string TrayRoot
    {
        get => _trayRoot;
        set => SetProperty(ref _trayRoot, value);
    }

    public bool KeepZip
    {
        get => _keepZip;
        set => SetProperty(ref _keepZip, value);
    }
}
