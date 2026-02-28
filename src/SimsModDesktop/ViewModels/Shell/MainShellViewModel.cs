using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Styling;
using SimsModDesktop.Application.Results;
using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.Models;
using SimsModDesktop.Services;
using SimsModDesktop.ViewModels.Infrastructure;
using SimsModDesktop.ViewModels.Inspector;

namespace SimsModDesktop.ViewModels.Shell;

public sealed class MainShellViewModel : ObservableObject
{
    private readonly MainWindowViewModel _legacy;
    private readonly INavigationService _navigation;
    private readonly ISettingsStore _settingsStore;
    private readonly ITS4PathDiscoveryService _pathDiscovery;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly IExecutionOutputParserRegistry _parserRegistry;
    private readonly IActionResultRepository _resultRepository;
    private readonly InspectorViewModel _inspector;

    private readonly ObservableCollection<ActionResultRow> _resultRows = [];
    private readonly ObservableCollection<string> _availableTypeFilters = ["All", "TrayPreview", "TrayDependency", "FindDuplicates"];
    private readonly ObservableCollection<string> _availableStatusFilters = ["All", "Conflict", "Review", "Safe", "Duplicate", "Lot", "Room", "Household", "Mixed", "GenericTray", "Unknown"];
    private readonly ObservableCollection<string> _availableDateFilters = ["All", "Last24h", "Last7d", "Last30d"];
    private readonly ObservableCollection<string> _availableConfidenceFilters = ["All", "High", "Medium", "Low", "n/a"];

    private AppSection _selectedSection = AppSection.Mods;
    private string _selectedModuleKey = "organize";
    private bool _enableGlobalSidebarShell = true;
    private bool _enableStructuredResults = true;
    private bool _enableInspectorPane = true;
    private bool _enableLaunchGame = true;
    private string _requestedTheme = "Dark";
    private string _gameExecutablePath = string.Empty;
    private string _modsPath = string.Empty;
    private string _trayPath = string.Empty;
    private string _savesPath = string.Empty;
    private string _launchGameStatus = string.Empty;
    private string _shellStatusMessage = "Ready.";
    private string _selectedTypeFilter = "All";
    private string _selectedStatusFilter = "All";
    private string _selectedDateFilter = "All";
    private string _selectedConfidenceFilter = "All";
    private ActionResultRow? _selectedResultRow;
    private SimsAction _latestAction = SimsAction.Organize;
    private bool _wasBusy;
    private bool _isInitialized;

    public MainShellViewModel(
        MainWindowViewModel legacy,
        INavigationService navigation,
        ISettingsStore settingsStore,
        ITS4PathDiscoveryService pathDiscovery,
        IGameLaunchService gameLaunchService,
        IExecutionOutputParserRegistry parserRegistry,
        IActionResultRepository resultRepository,
        InspectorViewModel inspector)
    {
        _legacy = legacy;
        _navigation = navigation;
        _settingsStore = settingsStore;
        _pathDiscovery = pathDiscovery;
        _gameLaunchService = gameLaunchService;
        _parserRegistry = parserRegistry;
        _resultRepository = resultRepository;
        _inspector = inspector;

        SelectSectionCommand = new RelayCommand<string>(SelectSection);
        SelectModuleCommand = new RelayCommand<string>(SelectModule);
        LaunchGameCommand = new AsyncRelayCommand(LaunchGameAsync, () => EnableLaunchGame);
        SetDarkThemeCommand = new RelayCommand(() => RequestedTheme = "Dark");
        SetLightThemeCommand = new RelayCommand(() => RequestedTheme = "Light");
        ClearFiltersCommand = new RelayCommand(ClearFilters);

        _legacy.PropertyChanged += OnLegacyPropertyChanged;
        _navigation.PropertyChanged += OnNavigationPropertyChanged;
        _resultRepository.PropertyChanged += OnResultRepositoryPropertyChanged;
    }

    public MainWindowViewModel Legacy => _legacy;
    public InspectorViewModel Inspector => _inspector;

    public RelayCommand<string> SelectSectionCommand { get; }
    public RelayCommand<string> SelectModuleCommand { get; }
    public AsyncRelayCommand LaunchGameCommand { get; }
    public RelayCommand SetDarkThemeCommand { get; }
    public RelayCommand SetLightThemeCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }

    public IReadOnlyList<NavigationItem> SectionItems => _navigation.SectionItems;
    public IReadOnlyList<NavigationItem> ModuleItems => _navigation.CurrentModules;
    public ObservableCollection<ActionResultRow> ResultRows => _resultRows;
    public ObservableCollection<string> AvailableTypeFilters => _availableTypeFilters;
    public ObservableCollection<string> AvailableStatusFilters => _availableStatusFilters;
    public ObservableCollection<string> AvailableDateFilters => _availableDateFilters;
    public ObservableCollection<string> AvailableConfidenceFilters => _availableConfidenceFilters;

    public AppSection SelectedSection
    {
        get => _selectedSection;
        private set
        {
            if (!SetProperty(ref _selectedSection, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsDashboardVisible));
            OnPropertyChanged(nameof(IsModsSectionVisible));
            OnPropertyChanged(nameof(IsTraySectionVisible));
            OnPropertyChanged(nameof(IsSavesVisible));
            OnPropertyChanged(nameof(IsSettingsVisible));
            OnPropertyChanged(nameof(ModuleItems));
        }
    }

    public string SelectedModuleKey
    {
        get => _selectedModuleKey;
        private set => SetProperty(ref _selectedModuleKey, value);
    }

    public bool EnableGlobalSidebarShell
    {
        get => _enableGlobalSidebarShell;
        set
        {
            if (!SetProperty(ref _enableGlobalSidebarShell, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsLegacyShellMode));
        }
    }

    public bool EnableStructuredResults
    {
        get => _enableStructuredResults;
        set => SetProperty(ref _enableStructuredResults, value);
    }

    public bool EnableInspectorPane
    {
        get => _enableInspectorPane;
        set
        {
            if (!SetProperty(ref _enableInspectorPane, value))
            {
                return;
            }

            Inspector.IsOpen = value;
            OnPropertyChanged(nameof(IsInspectorVisible));
        }
    }

    public bool EnableLaunchGame
    {
        get => _enableLaunchGame;
        set
        {
            if (!SetProperty(ref _enableLaunchGame, value))
            {
                return;
            }

            LaunchGameCommand.NotifyCanExecuteChanged();
        }
    }

    public string RequestedTheme
    {
        get => _requestedTheme;
        set
        {
            if (!SetProperty(ref _requestedTheme, value))
            {
                return;
            }

            ApplyTheme();
        }
    }

    public string GameExecutablePath
    {
        get => _gameExecutablePath;
        set => SetProperty(ref _gameExecutablePath, value);
    }

    public string ModsPath
    {
        get => _modsPath;
        set => SetProperty(ref _modsPath, value);
    }

    public string TrayPath
    {
        get => _trayPath;
        set => SetProperty(ref _trayPath, value);
    }

    public string SavesPath
    {
        get => _savesPath;
        set => SetProperty(ref _savesPath, value);
    }

    public string LaunchGameStatus
    {
        get => _launchGameStatus;
        private set => SetProperty(ref _launchGameStatus, value);
    }

    public string ShellStatusMessage
    {
        get => _shellStatusMessage;
        private set => SetProperty(ref _shellStatusMessage, value);
    }

    public string SelectedTypeFilter
    {
        get => _selectedTypeFilter;
        set
        {
            if (!SetProperty(ref _selectedTypeFilter, value))
            {
                return;
            }

            RebuildFilteredRows();
        }
    }

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (!SetProperty(ref _selectedStatusFilter, value))
            {
                return;
            }

            RebuildFilteredRows();
        }
    }

    public string SelectedDateFilter
    {
        get => _selectedDateFilter;
        set
        {
            if (!SetProperty(ref _selectedDateFilter, value))
            {
                return;
            }

            RebuildFilteredRows();
        }
    }

    public string SelectedConfidenceFilter
    {
        get => _selectedConfidenceFilter;
        set
        {
            if (!SetProperty(ref _selectedConfidenceFilter, value))
            {
                return;
            }

            RebuildFilteredRows();
        }
    }

    public ActionResultRow? SelectedResultRow
    {
        get => _selectedResultRow;
        set
        {
            if (!SetProperty(ref _selectedResultRow, value))
            {
                return;
            }

            _inspector.Update(_latestAction, value);
        }
    }

    public bool IsDashboardVisible => SelectedSection == AppSection.Dashboard;
    public bool IsModsSectionVisible => SelectedSection == AppSection.Mods;
    public bool IsTraySectionVisible => SelectedSection == AppSection.Tray;
    public bool IsSavesVisible => SelectedSection == AppSection.Saves;
    public bool IsSettingsVisible => SelectedSection == AppSection.Settings;
    public bool IsInspectorVisible => EnableInspectorPane && EnableStructuredResults;
    public bool IsLegacyVisible => IsModsSectionVisible || IsTraySectionVisible;
    public bool IsLegacyShellMode => !EnableGlobalSidebarShell;
    public bool HasResults => _resultRows.Count > 0;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await _legacy.InitializeAsync();
        await LoadShellSettingsAsync();

        if (string.IsNullOrWhiteSpace(GameExecutablePath) ||
            string.IsNullOrWhiteSpace(ModsPath) ||
            string.IsNullOrWhiteSpace(TrayPath) ||
            string.IsNullOrWhiteSpace(SavesPath))
        {
            var discovered = _pathDiscovery.Discover();
            if (string.IsNullOrWhiteSpace(GameExecutablePath))
            {
                GameExecutablePath = discovered.GameExecutablePath;
            }

            if (string.IsNullOrWhiteSpace(ModsPath))
            {
                ModsPath = discovered.ModsPath;
            }

            if (string.IsNullOrWhiteSpace(TrayPath))
            {
                TrayPath = discovered.TrayPath;
            }

            if (string.IsNullOrWhiteSpace(SavesPath))
            {
                SavesPath = discovered.SavesPath;
            }
        }

        _inspector.IsOpen = EnableInspectorPane;
        ApplyTheme();
        ApplyNavigationToLegacy();
        _isInitialized = true;
    }

    public async Task PersistSettingsAsync()
    {
        await _legacy.PersistSettingsAsync();

        var settings = await _settingsStore.LoadAsync();
        settings.Navigation = new AppSettings.NavigationSettings
        {
            SelectedSection = SelectedSection,
            SelectedModuleKey = SelectedModuleKey
        };
        settings.FeatureFlags = new AppSettings.FeatureFlagsSettings
        {
            EnableGlobalSidebarShell = EnableGlobalSidebarShell,
            EnableStructuredResults = EnableStructuredResults,
            EnableInspectorPane = EnableInspectorPane,
            EnableLaunchGame = EnableLaunchGame
        };
        settings.GameLaunch = new AppSettings.GameLaunchSettings
        {
            GameExecutablePath = GameExecutablePath,
            ModsPath = ModsPath,
            TrayPath = TrayPath,
            SavesPath = SavesPath
        };
        settings.Theme = new AppSettings.ThemeSettings
        {
            RequestedTheme = RequestedTheme
        };

        await _settingsStore.SaveAsync(settings);
    }

    private async Task LoadShellSettingsAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        EnableGlobalSidebarShell = settings.FeatureFlags.EnableGlobalSidebarShell;
        EnableStructuredResults = settings.FeatureFlags.EnableStructuredResults;
        EnableInspectorPane = settings.FeatureFlags.EnableInspectorPane;
        EnableLaunchGame = settings.FeatureFlags.EnableLaunchGame;
        RequestedTheme = string.IsNullOrWhiteSpace(settings.Theme.RequestedTheme)
            ? "Dark"
            : settings.Theme.RequestedTheme;
        GameExecutablePath = settings.GameLaunch.GameExecutablePath;
        ModsPath = settings.GameLaunch.ModsPath;
        TrayPath = settings.GameLaunch.TrayPath;
        SavesPath = settings.GameLaunch.SavesPath;

        _navigation.SelectSection(settings.Navigation.SelectedSection);
        _navigation.SelectModule(string.IsNullOrWhiteSpace(settings.Navigation.SelectedModuleKey)
            ? "organize"
            : settings.Navigation.SelectedModuleKey);

        SelectedSection = _navigation.SelectedSection;
        SelectedModuleKey = _navigation.SelectedModuleKey;
    }

    private void ApplyTheme()
    {
        if (Avalonia.Application.Current is null)
        {
            return;
        }

        Avalonia.Application.Current.RequestedThemeVariant =
            string.Equals(RequestedTheme, "Light", StringComparison.OrdinalIgnoreCase)
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
    }

    private void SelectSection(string? sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            return;
        }

        if (!Enum.TryParse<AppSection>(sectionKey, ignoreCase: true, out var section))
        {
            return;
        }

        _navigation.SelectSection(section);
        SelectedSection = _navigation.SelectedSection;
        SelectedModuleKey = _navigation.SelectedModuleKey;
        ApplyNavigationToLegacy();
    }

    private void SelectModule(string? moduleKey)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
        {
            return;
        }

        _navigation.SelectModule(moduleKey);
        SelectedModuleKey = _navigation.SelectedModuleKey;
        ApplyNavigationToLegacy();
    }

    private void ApplyNavigationToLegacy()
    {
        switch (SelectedModuleKey.ToLowerInvariant())
        {
            case "organize":
                _legacy.Workspace = AppWorkspace.Toolkit;
                _legacy.SelectedAction = SimsAction.Organize;
                break;
            case "flatten":
                _legacy.Workspace = AppWorkspace.Toolkit;
                _legacy.SelectedAction = SimsAction.Flatten;
                break;
            case "normalize":
                _legacy.Workspace = AppWorkspace.Toolkit;
                _legacy.SelectedAction = SimsAction.Normalize;
                break;
            case "merge":
                _legacy.Workspace = AppWorkspace.Toolkit;
                _legacy.SelectedAction = SimsAction.Merge;
                break;
            case "finddup":
                _legacy.Workspace = AppWorkspace.Toolkit;
                _legacy.SelectedAction = SimsAction.FindDuplicates;
                break;
            case "traydeps":
                _legacy.Workspace = AppWorkspace.Toolkit;
                _legacy.SelectedAction = SimsAction.TrayDependencies;
                break;
            case "traypreview":
                _legacy.Workspace = AppWorkspace.TrayPreview;
                break;
        }
    }

    private async Task LaunchGameAsync()
    {
        var request = new LaunchGameRequest
        {
            ExecutablePath = GameExecutablePath,
            WorkingDirectory = string.IsNullOrWhiteSpace(GameExecutablePath)
                ? null
                : Path.GetDirectoryName(GameExecutablePath)
        };

        var result = _gameLaunchService.Launch(request);
        LaunchGameStatus = result.Message;
        ShellStatusMessage = result.Message;
        await Task.CompletedTask;
    }

    private void ClearFilters()
    {
        SelectedTypeFilter = "All";
        SelectedStatusFilter = "All";
        SelectedDateFilter = "All";
        SelectedConfidenceFilter = "All";
    }

    private void OnLegacyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(MainWindowViewModel.IsBusy), StringComparison.Ordinal))
        {
            return;
        }

        if (_wasBusy && !_legacy.IsBusy)
        {
            CaptureStructuredResults();
        }

        _wasBusy = _legacy.IsBusy;
    }

    private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(INavigationService.SelectedSection), StringComparison.Ordinal))
        {
            SelectedSection = _navigation.SelectedSection;
            OnPropertyChanged(nameof(SectionItems));
        }

        if (string.Equals(e.PropertyName, nameof(INavigationService.SelectedModuleKey), StringComparison.Ordinal))
        {
            SelectedModuleKey = _navigation.SelectedModuleKey;
        }

        if (string.Equals(e.PropertyName, nameof(INavigationService.CurrentModules), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ModuleItems));
        }
    }

    private void OnResultRepositoryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(IActionResultRepository.Latest), StringComparison.Ordinal))
        {
            return;
        }

        RebuildFilteredRows();
    }

    private void CaptureStructuredResults()
    {
        if (!EnableStructuredResults)
        {
            return;
        }

        var action = _legacy.IsTrayPreviewWorkspace ? SimsAction.TrayPreview : _legacy.SelectedAction;
        var context = new ExecutionOutputParseContext
        {
            Action = action,
            LogText = _legacy.LogText,
            TrayPreviewItems = _legacy.PreviewItems.ToArray()
        };

        if (!_parserRegistry.TryParse(context, out var envelope, out var parseError))
        {
            ShellStatusMessage = $"Structured result parse skipped: {parseError}";
            return;
        }

        _latestAction = action;
        _resultRepository.Save(envelope);
        ShellStatusMessage = $"Structured results updated: {envelope.Rows.Count} rows.";
        RebuildFilteredRows();
    }

    private void RebuildFilteredRows()
    {
        var latest = _resultRepository.Latest;
        _resultRows.Clear();

        if (latest is null)
        {
            OnPropertyChanged(nameof(HasResults));
            return;
        }

        var rows = latest.Rows.AsEnumerable();

        if (!string.Equals(SelectedTypeFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(row => row.Category.Contains(SelectedTypeFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(SelectedStatusFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(row => string.Equals(row.Status, SelectedStatusFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.Equals(SelectedConfidenceFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(row => string.Equals(row.Confidence, SelectedConfidenceFilter, StringComparison.OrdinalIgnoreCase));
        }

        var minDate = DateTime.MinValue;
        if (string.Equals(SelectedDateFilter, "Last24h", StringComparison.OrdinalIgnoreCase))
        {
            minDate = DateTime.Now.AddHours(-24);
        }
        else if (string.Equals(SelectedDateFilter, "Last7d", StringComparison.OrdinalIgnoreCase))
        {
            minDate = DateTime.Now.AddDays(-7);
        }
        else if (string.Equals(SelectedDateFilter, "Last30d", StringComparison.OrdinalIgnoreCase))
        {
            minDate = DateTime.Now.AddDays(-30);
        }

        if (minDate > DateTime.MinValue)
        {
            rows = rows.Where(row => row.UpdatedLocal is null || row.UpdatedLocal >= minDate);
        }

        foreach (var row in rows.Take(500))
        {
            _resultRows.Add(row);
        }

        SelectedResultRow = _resultRows.FirstOrDefault();
        OnPropertyChanged(nameof(HasResults));
    }
}
