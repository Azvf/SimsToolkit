using SimsModDesktop.Presentation.ViewModels.Infrastructure;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowStatusController : ObservableObject
{
    private const string MainWindowLogSource = "MainWindow";
    private readonly IUiLogSink _uiLogSink;
    private string _statusMessage = string.Empty;
    private bool _isProgressIndeterminate;
    private int _progressValue;
    private string _progressMessage = string.Empty;
    private string _logText = string.Empty;

    public MainWindowStatusController(IUiLogSink uiLogSink)
    {
        _uiLogSink = uiLogSink;
        _uiLogSink.ResetAll();
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

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public void SetProgress(bool isIndeterminate, int percent, string message)
    {
        IsProgressIndeterminate = isIndeterminate;
        ProgressValue = isIndeterminate ? 0 : Math.Clamp(percent, 0, 100);
        ProgressMessage = message;
    }

    public void ClearLog()
    {
        _uiLogSink.ClearSource(MainWindowLogSource, appendClearedMarker: true);
        LogText = _uiLogSink.GetSourceText(MainWindowLogSource);
    }

    public void AppendLog(string message)
    {
        _uiLogSink.Append(MainWindowLogSource, message);
        LogText = _uiLogSink.GetSourceText(MainWindowLogSource);
    }
}
