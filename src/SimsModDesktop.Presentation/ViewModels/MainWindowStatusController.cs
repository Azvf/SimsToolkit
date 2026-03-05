using SimsModDesktop.Presentation.ViewModels.Infrastructure;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowStatusController : ObservableObject
{
    private string _statusMessage = string.Empty;
    private bool _isProgressIndeterminate;
    private int _progressValue;
    private string _progressMessage = string.Empty;
    public MainWindowStatusController()
    {
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        set => SetProperty(ref _isProgressIndeterminate, value);
    }

    public int ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        set => SetProperty(ref _progressMessage, value);
    }

    public void SetProgress(bool isIndeterminate, int percent, string message)
    {
        IsProgressIndeterminate = isIndeterminate;
        ProgressValue = isIndeterminate ? 0 : Math.Clamp(percent, 0, 100);
        ProgressMessage = message;
    }
}
