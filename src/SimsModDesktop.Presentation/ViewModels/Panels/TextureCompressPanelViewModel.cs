using SimsModDesktop.Application.Modules;
using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Panels;

public sealed class TextureCompressPanelViewModel : ObservableObject, ITextureCompressModuleState
{
    private string _sourcePath = string.Empty;
    private string _outputPath = string.Empty;
    private string _targetWidthText = string.Empty;
    private string _targetHeightText = string.Empty;
    private bool _hasAlphaHint = true;
    private bool _generateMipMaps = true;
    private string _preferredFormat = "Auto";
    private string _lastOutputPath = string.Empty;
    private string _lastRunSummary = string.Empty;

    public IReadOnlyList<string> AvailablePreferredFormats { get; } = ["Auto", "BC1", "BC3"];

    public string SourcePath
    {
        get => _sourcePath;
        set => SetProperty(ref _sourcePath, value);
    }

    public string OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value);
    }

    public string TargetWidthText
    {
        get => _targetWidthText;
        set => SetProperty(ref _targetWidthText, value);
    }

    public string TargetHeightText
    {
        get => _targetHeightText;
        set => SetProperty(ref _targetHeightText, value);
    }

    public bool HasAlphaHint
    {
        get => _hasAlphaHint;
        set => SetProperty(ref _hasAlphaHint, value);
    }

    public bool GenerateMipMaps
    {
        get => _generateMipMaps;
        set => SetProperty(ref _generateMipMaps, value);
    }

    public string PreferredFormat
    {
        get => _preferredFormat;
        set => SetProperty(ref _preferredFormat, value);
    }

    public string LastOutputPath
    {
        get => _lastOutputPath;
        set
        {
            if (SetProperty(ref _lastOutputPath, value))
            {
                OnPropertyChanged(nameof(HasLastOutputPath));
            }
        }
    }

    public string LastRunSummary
    {
        get => _lastRunSummary;
        set
        {
            if (SetProperty(ref _lastRunSummary, value))
            {
                OnPropertyChanged(nameof(HasLastRunSummary));
            }
        }
    }

    public bool HasLastOutputPath => !string.IsNullOrWhiteSpace(LastOutputPath);
    public bool HasLastRunSummary => !string.IsNullOrWhiteSpace(LastRunSummary);
}
