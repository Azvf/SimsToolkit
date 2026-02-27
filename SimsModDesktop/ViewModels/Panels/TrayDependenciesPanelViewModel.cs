using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Panels;

public sealed class TrayDependenciesPanelViewModel : ObservableObject
{
    private string _trayPath = string.Empty;
    private string _modsPath = string.Empty;
    private string _trayItemKey = string.Empty;
    private string _analysisMode = "StrictS4TI";
    private string _s4tiPath = string.Empty;
    private string _minMatchCountText = "1";
    private string _topNText = "200";
    private string _maxPackageCountText = "0";
    private bool _exportUnusedPackages;
    private bool _exportMatchedPackages;
    private string _outputCsv = string.Empty;
    private string _unusedOutputCsv = string.Empty;
    private string _exportTargetPath = string.Empty;
    private string _exportMinConfidence = "Low";

    public IReadOnlyList<string> AvailableAnalysisModes { get; } = new[] { "StrictS4TI", "Legacy" };
    public IReadOnlyList<string> AvailableExportMinConfidence { get; } = new[] { "Low", "Medium", "High" };

    public string TrayPath
    {
        get => _trayPath;
        set => SetProperty(ref _trayPath, value);
    }

    public string ModsPath
    {
        get => _modsPath;
        set => SetProperty(ref _modsPath, value);
    }

    public string TrayItemKey
    {
        get => _trayItemKey;
        set => SetProperty(ref _trayItemKey, value);
    }

    public string AnalysisMode
    {
        get => _analysisMode;
        set => SetProperty(ref _analysisMode, value);
    }

    public string S4tiPath
    {
        get => _s4tiPath;
        set => SetProperty(ref _s4tiPath, value);
    }

    public string MinMatchCountText
    {
        get => _minMatchCountText;
        set => SetProperty(ref _minMatchCountText, value);
    }

    public string TopNText
    {
        get => _topNText;
        set => SetProperty(ref _topNText, value);
    }

    public string MaxPackageCountText
    {
        get => _maxPackageCountText;
        set => SetProperty(ref _maxPackageCountText, value);
    }

    public bool ExportUnusedPackages
    {
        get => _exportUnusedPackages;
        set => SetProperty(ref _exportUnusedPackages, value);
    }

    public bool ExportMatchedPackages
    {
        get => _exportMatchedPackages;
        set => SetProperty(ref _exportMatchedPackages, value);
    }

    public string OutputCsv
    {
        get => _outputCsv;
        set => SetProperty(ref _outputCsv, value);
    }

    public string UnusedOutputCsv
    {
        get => _unusedOutputCsv;
        set => SetProperty(ref _unusedOutputCsv, value);
    }

    public string ExportTargetPath
    {
        get => _exportTargetPath;
        set => SetProperty(ref _exportTargetPath, value);
    }

    public string ExportMinConfidence
    {
        get => _exportMinConfidence;
        set => SetProperty(ref _exportMinConfidence, value);
    }
}
