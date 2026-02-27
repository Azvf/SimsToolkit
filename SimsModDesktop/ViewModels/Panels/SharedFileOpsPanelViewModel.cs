using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Panels;

public sealed class SharedFileOpsPanelViewModel : ObservableObject
{
    private bool _skipPruneEmptyDirs;
    private bool _modFilesOnly;
    private bool _verifyContentOnNameConflict;
    private string _modExtensionsText = ".package,.ts4script";
    private string _prefixHashBytesText = "102400";
    private string _hashWorkerCountText = "8";

    public bool SkipPruneEmptyDirs
    {
        get => _skipPruneEmptyDirs;
        set => SetProperty(ref _skipPruneEmptyDirs, value);
    }

    public bool ModFilesOnly
    {
        get => _modFilesOnly;
        set => SetProperty(ref _modFilesOnly, value);
    }

    public bool VerifyContentOnNameConflict
    {
        get => _verifyContentOnNameConflict;
        set => SetProperty(ref _verifyContentOnNameConflict, value);
    }

    public string ModExtensionsText
    {
        get => _modExtensionsText;
        set => SetProperty(ref _modExtensionsText, value);
    }

    public string PrefixHashBytesText
    {
        get => _prefixHashBytesText;
        set => SetProperty(ref _prefixHashBytesText, value);
    }

    public string HashWorkerCountText
    {
        get => _hashWorkerCountText;
        set => SetProperty(ref _hashWorkerCountText, value);
    }
}
