using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Panels;

public sealed class TrayPreviewPanelViewModel : ObservableObject
{
    private string _trayRoot = string.Empty;
    private string _trayItemKey = string.Empty;
    private string _topNText = string.Empty;
    private string _filesPerItemText = "12";

    public string TrayRoot
    {
        get => _trayRoot;
        set => SetProperty(ref _trayRoot, value);
    }

    public string TrayItemKey
    {
        get => _trayItemKey;
        set => SetProperty(ref _trayItemKey, value);
    }

    public string TopNText
    {
        get => _topNText;
        set => SetProperty(ref _topNText, value);
    }

    public string FilesPerItemText
    {
        get => _filesPerItemText;
        set => SetProperty(ref _filesPerItemText, value);
    }
}
