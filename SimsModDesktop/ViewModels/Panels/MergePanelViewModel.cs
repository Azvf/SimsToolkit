using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Panels;

public sealed class MergePanelViewModel : ObservableObject
{
    private string _sourcePathsText = string.Empty;
    private string _targetPath = string.Empty;

    public string SourcePathsText
    {
        get => _sourcePathsText;
        set => SetProperty(ref _sourcePathsText, value);
    }

    public string TargetPath
    {
        get => _targetPath;
        set => SetProperty(ref _targetPath, value);
    }
}
