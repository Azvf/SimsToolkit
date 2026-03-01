using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels;

public sealed class TrayExportTaskItemViewModel : ObservableObject
{
    private string _statusText = "Queued";
    private string _progressText = "Queued";
    private double _progressPercent;
    private int _completedSteps;
    private int _totalSteps = 2;
    private bool _isRunning = true;
    private bool _isFailed;
    private string _exportRootPath = string.Empty;
    private string _detailsText = string.Empty;
    private bool _isDetailsExpanded;

    public TrayExportTaskItemViewModel(string title)
    {
        Title = string.IsNullOrWhiteSpace(title)
            ? "Tray Export"
            : title.Trim();
    }

    public string Title { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public int CompletedSteps
    {
        get => _completedSteps;
        private set
        {
            if (!SetProperty(ref _completedSteps, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(ProgressPercent));
        }
    }

    public int TotalSteps
    {
        get => _totalSteps;
        private set
        {
            if (!SetProperty(ref _totalSteps, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(ProgressPercent));
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsCompleted));
        }
    }

    public bool IsFailed
    {
        get => _isFailed;
        private set => SetProperty(ref _isFailed, value);
    }

    public string ExportRootPath
    {
        get => _exportRootPath;
        private set
        {
            if (!SetProperty(ref _exportRootPath, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasExportRoot));
        }
    }

    public string DetailsText
    {
        get => _detailsText;
        private set
        {
            if (!SetProperty(ref _detailsText, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasDetails));
            OnPropertyChanged(nameof(CanShowDetailsToggle));
        }
    }

    public bool IsDetailsExpanded
    {
        get => _isDetailsExpanded;
        private set => SetProperty(ref _isDetailsExpanded, value);
    }

    public bool IsCompleted => !IsRunning;
    public bool HasExportRoot => !string.IsNullOrWhiteSpace(ExportRootPath);
    public bool HasDetails => !string.IsNullOrWhiteSpace(DetailsText);
    public bool CanShowDetailsToggle => IsFailed && HasDetails;
    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, Math.Clamp(value, 0d, 100d));
    }

    public void SetExportRoot(string exportRootPath)
    {
        ExportRootPath = exportRootPath?.Trim() ?? string.Empty;
    }

    public void AppendDetailLine(string? detailLine)
    {
        if (string.IsNullOrWhiteSpace(detailLine))
        {
            return;
        }

        DetailsText = string.IsNullOrWhiteSpace(DetailsText)
            ? detailLine.Trim()
            : DetailsText + Environment.NewLine + detailLine.Trim();
    }

    public void ToggleDetails()
    {
        if (!HasDetails)
        {
            return;
        }

        IsDetailsExpanded = !IsDetailsExpanded;
    }

    public void MarkTrayRunning()
    {
        ProgressText = "Preparing";
        ProgressPercent = 5d;
        StatusText = "Exporting tray files...";
    }

    public void MarkTrayCompleted(int exportedCount, int skippedCount)
    {
        CompletedSteps = 1;
        ProgressText = "Tray ready";
        ProgressPercent = 20d;
        StatusText = skippedCount == 0
            ? $"Tray files exported ({exportedCount})"
            : $"Tray files exported ({exportedCount}), skipped {skippedCount}";
    }

    public void MarkModsRunning()
    {
        ProgressText = "0%";
        StatusText = "Exporting referenced mods...";
    }

    public void UpdateModsProgress(int percent, string? detail)
    {
        ProgressText = $"{Math.Clamp(percent, 0, 100)}%";
        ProgressPercent = Math.Clamp(percent, 0, 100);

        if (!string.IsNullOrWhiteSpace(detail))
        {
            StatusText = detail.Trim();
        }
    }

    public void MarkFailed(string statusText)
    {
        StatusText = statusText;
        IsFailed = true;
        OnPropertyChanged(nameof(CanShowDetailsToggle));
        IsRunning = false;
        ProgressText = ProgressPercent > 0d && ProgressPercent < 100d
            ? $"{Math.Clamp((int)Math.Round(ProgressPercent), 0, 99)}%"
            : "Failed";
    }

    public void MarkCompleted(string statusText, bool failed)
    {
        CompletedSteps = TotalSteps;
        ProgressText = "100%";
        ProgressPercent = 100d;
        StatusText = statusText;
        IsFailed = failed;
        OnPropertyChanged(nameof(CanShowDetailsToggle));
        IsRunning = false;
    }
}
