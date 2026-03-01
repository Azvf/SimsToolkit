using SimsModDesktop.Application.Saves;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.Models;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Saves;

public sealed class SaveHouseholdsViewModel : ObservableObject
{
    private readonly ISaveHouseholdCoordinator _coordinator;
    private readonly IFileDialogService _fileDialogService;

    private string _savesPath = string.Empty;
    private string _exportRootPath = string.Empty;
    private bool _generateThumbnails = true;
    private bool _isBusy;
    private string _statusText = "Set a valid Saves path to scan save slots.";
    private string _exportLogText = string.Empty;
    private IReadOnlyList<SaveFileEntry> _saveFiles = Array.Empty<SaveFileEntry>();
    private IReadOnlyList<SaveHouseholdItem> _households = Array.Empty<SaveHouseholdItem>();
    private SaveFileEntry? _selectedSave;
    private SaveHouseholdItem? _selectedHousehold;

    public SaveHouseholdsViewModel(
        ISaveHouseholdCoordinator coordinator,
        IFileDialogService fileDialogService)
    {
        _coordinator = coordinator;
        _fileDialogService = fileDialogService;

        RefreshSavesCommand = new AsyncRelayCommand(RefreshSavesAsync, () => !IsBusy);
        LoadHouseholdsCommand = new AsyncRelayCommand(LoadHouseholdsAsync, () => !IsBusy && SelectedSave is not null);
        BrowseExportRootCommand = new AsyncRelayCommand(BrowseExportRootAsync, () => !IsBusy);
        ExportCommand = new AsyncRelayCommand(ExportAsync, CanExport);
    }

    public AsyncRelayCommand RefreshSavesCommand { get; }
    public AsyncRelayCommand LoadHouseholdsCommand { get; }
    public AsyncRelayCommand BrowseExportRootCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }

    public string SavesPath
    {
        get => _savesPath;
        set
        {
            var normalized = NormalizePath(value);
            if (!SetProperty(ref _savesPath, normalized))
            {
                return;
            }

            if (!HasValidSavesPath)
            {
                SaveFiles = Array.Empty<SaveFileEntry>();
                Households = Array.Empty<SaveHouseholdItem>();
                SelectedSave = null;
                SelectedHousehold = null;
                StatusText = "Set a valid Saves path to scan save slots.";
            }

            NotifyStateChanged();
        }
    }

    public string ExportRootPath
    {
        get => _exportRootPath;
        set
        {
            var normalized = NormalizePath(value);
            if (!SetProperty(ref _exportRootPath, normalized))
            {
                return;
            }

            NotifyStateChanged();
        }
    }

    public bool GenerateThumbnails
    {
        get => _generateThumbnails;
        set
        {
            if (!SetProperty(ref _generateThumbnails, value))
            {
                return;
            }

            NotifyStateChanged();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            NotifyStateChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string ExportLogText
    {
        get => _exportLogText;
        private set => SetProperty(ref _exportLogText, value);
    }

    public IReadOnlyList<SaveFileEntry> SaveFiles
    {
        get => _saveFiles;
        private set => SetProperty(ref _saveFiles, value);
    }

    public IReadOnlyList<SaveHouseholdItem> Households
    {
        get => _households;
        private set => SetProperty(ref _households, value);
    }

    public SaveFileEntry? SelectedSave
    {
        get => _selectedSave;
        set
        {
            if (!SetProperty(ref _selectedSave, value))
            {
                return;
            }

            Households = Array.Empty<SaveHouseholdItem>();
            SelectedHousehold = null;
            NotifyStateChanged();
        }
    }

    public SaveHouseholdItem? SelectedHousehold
    {
        get => _selectedHousehold;
        set
        {
            if (!SetProperty(ref _selectedHousehold, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedHouseholdMembers));
            OnPropertyChanged(nameof(SelectedHouseholdTitle));
            OnPropertyChanged(nameof(SelectedHouseholdLocation));
            OnPropertyChanged(nameof(SelectedHouseholdBlockReason));
            NotifyStateChanged();
        }
    }

    public IReadOnlyList<SaveMemberItem> SelectedHouseholdMembers =>
        SelectedHousehold?.Members ?? Array.Empty<SaveMemberItem>();

    public string SelectedHouseholdTitle =>
        SelectedHousehold?.DisplayLabel ?? "No household selected";

    public string SelectedHouseholdLocation =>
        SelectedHousehold?.LocationLabel ?? string.Empty;

    public string SelectedHouseholdBlockReason =>
        SelectedHousehold?.ExportBlockReason ?? string.Empty;

    public bool HasValidSavesPath =>
        !string.IsNullOrWhiteSpace(SavesPath) && Directory.Exists(SavesPath);

    public bool HasExportRootPath =>
        !string.IsNullOrWhiteSpace(ExportRootPath) && Directory.Exists(ExportRootPath);

    public void LoadFromSettings(AppSettings.SavesSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ExportRootPath = settings.LastExportRoot;
        GenerateThumbnails = settings.GenerateThumbnails;
    }

    public AppSettings.SavesSettings ToSettings()
    {
        return new AppSettings.SavesSettings
        {
            LastExportRoot = ExportRootPath,
            GenerateThumbnails = GenerateThumbnails
        };
    }

    private async Task RefreshSavesAsync()
    {
        await RunBusyAsync(() =>
        {
            if (!HasValidSavesPath)
            {
                SaveFiles = Array.Empty<SaveFileEntry>();
                SelectedSave = null;
                StatusText = "Saves path is missing or does not exist.";
                return Task.CompletedTask;
            }

            var saveFiles = _coordinator.GetSaveFiles(SavesPath);
            SaveFiles = saveFiles;
            SelectedSave = saveFiles.FirstOrDefault();
            StatusText = saveFiles.Count == 0
                ? "No primary .save files were found."
                : $"Found {saveFiles.Count} save file(s).";
            ExportLogText = string.Empty;
            return Task.CompletedTask;
        });
    }

    private async Task LoadHouseholdsAsync()
    {
        await RunBusyAsync(() =>
        {
            if (SelectedSave is null)
            {
                StatusText = "Select a save file first.";
                return Task.CompletedTask;
            }

            if (!_coordinator.TryLoadHouseholds(SelectedSave.FilePath, out var snapshot, out var error))
            {
                Households = Array.Empty<SaveHouseholdItem>();
                SelectedHousehold = null;
                StatusText = string.IsNullOrWhiteSpace(error)
                    ? "Failed to load households."
                    : error;
                return Task.CompletedTask;
            }

            Households = snapshot!.Households;
            SelectedHousehold = snapshot.Households.FirstOrDefault();
            StatusText = $"Loaded {snapshot.Households.Count} household(s) from {SelectedSave.FileName}.";
            ExportLogText = string.Empty;
            return Task.CompletedTask;
        });
    }

    private async Task BrowseExportRootAsync()
    {
        await RunBusyAsync(async () =>
        {
            var selectedPaths = await _fileDialogService.PickFolderPathsAsync("Select Export Root", allowMultiple: false);
            var selectedPath = selectedPaths.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            ExportRootPath = selectedPath;
        });
    }

    private async Task ExportAsync()
    {
        await RunBusyAsync(() =>
        {
            if (SelectedSave is null || SelectedHousehold is null)
            {
                StatusText = "Select a save and household before exporting.";
                return Task.CompletedTask;
            }

            if (!HasExportRootPath)
            {
                StatusText = "Export root path is missing or does not exist.";
                return Task.CompletedTask;
            }

            var request = new SaveHouseholdExportRequest
            {
                SourceSavePath = SelectedSave.FilePath,
                HouseholdId = SelectedHousehold.HouseholdId,
                ExportRootPath = ExportRootPath,
                CreatorName = Environment.UserName,
                CreatorId = ComputeCreatorId(Environment.UserName),
                GenerateThumbnails = GenerateThumbnails
            };

            var result = _coordinator.Export(request);
            if (!result.Succeeded)
            {
                StatusText = string.IsNullOrWhiteSpace(result.Error)
                    ? "Export failed."
                    : result.Error;
                ExportLogText = string.IsNullOrWhiteSpace(result.Error)
                    ? "Export failed."
                    : result.Error;
                return Task.CompletedTask;
            }

            StatusText = $"Exported to {result.ExportDirectory}";
            var lines = new List<string>
            {
                $"Instance: {result.InstanceIdHex}",
                $"Directory: {result.ExportDirectory}",
                "Files:"
            };
            lines.AddRange(result.WrittenFiles
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>());
            if (result.Warnings.Count > 0)
            {
                lines.Add("Warnings:");
                lines.AddRange(result.Warnings);
            }

            ExportLogText = string.Join(Environment.NewLine, lines);
            return Task.CompletedTask;
        });
    }

    private bool CanExport()
    {
        return !IsBusy &&
               SelectedSave is not null &&
               SelectedHousehold is not null &&
               SelectedHousehold.CanExport &&
               HasExportRootPath;
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasValidSavesPath));
        OnPropertyChanged(nameof(HasExportRootPath));
        OnPropertyChanged(nameof(SelectedHouseholdTitle));
        OnPropertyChanged(nameof(SelectedHouseholdLocation));
        OnPropertyChanged(nameof(SelectedHouseholdBlockReason));
        RefreshSavesCommand.NotifyCanExecuteChanged();
        LoadHouseholdsCommand.NotifyCanExecuteChanged();
        BrowseExportRootCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
    }

    private static string NormalizePath(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Trim('"');
    }

    private static ulong ComputeCreatorId(string value)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        var hash = offsetBasis;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash == 0 ? 1UL : hash;
    }
}
