using System.ComponentModel;
using Avalonia;
using Avalonia.Styling;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.Models;
using SimsModDesktop.Services;
using SimsModDesktop.ViewModels.Infrastructure;

namespace SimsModDesktop.ViewModels.Shell;

public sealed class MainShellViewModel : ObservableObject
{
    private readonly MainWindowViewModel _workspaceVm;
    private readonly INavigationService _navigation;
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsStore _settingsStore;
    private readonly ITS4PathDiscoveryService _pathDiscovery;
    private readonly IGameLaunchService _gameLaunchService;

    private AppSection _selectedSection = AppSection.Mods;
    private string _selectedModuleKey = "organize";
    private bool _enableLaunchGame = true;
    private string _requestedTheme = "Dark";
    private string _ts4RootPath = string.Empty;
    private string _gameExecutablePath = string.Empty;
    private string _modsPath = string.Empty;
    private string _trayPath = string.Empty;
    private string _savesPath = string.Empty;
    private string _launchGameStatus = string.Empty;
    private bool _isApplyingDerivedPaths;
    private string _lastDerivedModsPath = string.Empty;
    private string _lastDerivedTrayPath = string.Empty;
    private string _lastDerivedSavesPath = string.Empty;
    private bool _isInitialized;

    public event EventHandler? Ts4RootFocusRequested;

    public MainShellViewModel(
        MainWindowViewModel workspaceVm,
        INavigationService navigation,
        IFileDialogService fileDialogService,
        ISettingsStore settingsStore,
        ITS4PathDiscoveryService pathDiscovery,
        IGameLaunchService gameLaunchService)
    {
        _workspaceVm = workspaceVm;
        _navigation = navigation;
        _fileDialogService = fileDialogService;
        _settingsStore = settingsStore;
        _pathDiscovery = pathDiscovery;
        _gameLaunchService = gameLaunchService;

        SelectSectionCommand = new RelayCommand<string>(SelectSection);
        SelectModuleCommand = new RelayCommand<string>(SelectModule);
        LaunchGameCommand = new AsyncRelayCommand(LaunchGameAsync, () => CanLaunchGame);
        SetDarkThemeCommand = new RelayCommand(() => RequestedTheme = "Dark");
        SetLightThemeCommand = new RelayCommand(() => RequestedTheme = "Light");
        BrowseTs4RootCommand = new AsyncRelayCommand(BrowseTs4RootAsync, () => !_workspaceVm.IsBusy, disableWhileRunning: false);
        NavigateToSettingsForPathFixCommand = new RelayCommand(
            NavigateToSettingsForPathFix,
            () => !HasAllCorePathsValid);

        _workspaceVm.PropertyChanged += OnWorkspaceVmPropertyChanged;
        _navigation.PropertyChanged += OnNavigationPropertyChanged;
    }

    public MainWindowViewModel WorkspaceVm => _workspaceVm;

    public RelayCommand<string> SelectSectionCommand { get; }
    public RelayCommand<string> SelectModuleCommand { get; }
    public AsyncRelayCommand LaunchGameCommand { get; }
    public RelayCommand SetDarkThemeCommand { get; }
    public RelayCommand SetLightThemeCommand { get; }
    public AsyncRelayCommand BrowseTs4RootCommand { get; }
    public RelayCommand NavigateToSettingsForPathFixCommand { get; }

    public IReadOnlyList<NavigationItem> SectionItems => _navigation.SectionItems;
    public IReadOnlyList<NavigationItem> ModuleItems => _navigation.CurrentModules;

    public AppSection SelectedSection
    {
        get => _selectedSection;
        private set
        {
            if (!SetProperty(ref _selectedSection, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsModsSectionVisible));
            OnPropertyChanged(nameof(IsTraySectionVisible));
            OnPropertyChanged(nameof(IsSavesVisible));
            OnPropertyChanged(nameof(IsSettingsVisible));
            OnPropertyChanged(nameof(ModuleItems));
            OnPropertyChanged(nameof(IsModsSectionSelected));
            OnPropertyChanged(nameof(IsTraySectionSelected));
            OnPropertyChanged(nameof(IsSavesSectionSelected));
            OnPropertyChanged(nameof(IsSettingsSectionSelected));
            OnPropertyChanged(nameof(IsWorkspaceSectionVisible));
        }
    }

    public string SelectedModuleKey
    {
        get => _selectedModuleKey;
        private set
        {
            if (!SetProperty(ref _selectedModuleKey, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsOrganizeModuleSelected));
            OnPropertyChanged(nameof(IsFlattenModuleSelected));
            OnPropertyChanged(nameof(IsNormalizeModuleSelected));
            OnPropertyChanged(nameof(IsMergeModuleSelected));
            OnPropertyChanged(nameof(IsFindDupModuleSelected));
            OnPropertyChanged(nameof(IsTrayPreviewModuleSelected));
            OnPropertyChanged(nameof(IsTrayDepsModuleSelected));
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

            OnPropertyChanged(nameof(CanLaunchGame));
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

            OnPropertyChanged(nameof(IsDarkThemeSelected));
            OnPropertyChanged(nameof(IsLightThemeSelected));
            ApplyTheme();
        }
    }

    public string Ts4RootPath
    {
        get => _ts4RootPath;
        set
        {
            var normalized = NormalizePath(value);
            if (!SetProperty(ref _ts4RootPath, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(IsDerivedPathsReadOnly));
            ApplyDerivedPathsFromRoot();
        }
    }

    public string GameExecutablePath
    {
        get => _gameExecutablePath;
        set
        {
            if (!SetProperty(ref _gameExecutablePath, value))
            {
                return;
            }

            NotifyPathHealthChanged();
        }
    }

    public string ModsPath
    {
        get => _modsPath;
        set
        {
            if (!SetProperty(ref _modsPath, value))
            {
                return;
            }

            NotifyPathHealthChanged();
            SyncTrayPathsToWorkspace();
        }
    }

    public string TrayPath
    {
        get => _trayPath;
        set
        {
            if (!SetProperty(ref _trayPath, value))
            {
                return;
            }

            NotifyPathHealthChanged();
            SyncTrayPathsToWorkspace();
        }
    }

    public string SavesPath
    {
        get => _savesPath;
        set
        {
            if (!SetProperty(ref _savesPath, value))
            {
                return;
            }

            NotifyPathHealthChanged();
        }
    }

    public string LaunchGameStatus
    {
        get => _launchGameStatus;
        private set => SetProperty(ref _launchGameStatus, value);
    }

    public bool IsModsSectionVisible => SelectedSection == AppSection.Mods;
    public bool IsTraySectionVisible => SelectedSection == AppSection.Tray;
    public bool IsSavesVisible => SelectedSection == AppSection.Saves;
    public bool IsSettingsVisible => SelectedSection == AppSection.Settings;
    public bool IsModsSectionSelected => SelectedSection == AppSection.Mods;
    public bool IsTraySectionSelected => SelectedSection == AppSection.Tray;
    public bool IsSavesSectionSelected => SelectedSection == AppSection.Saves;
    public bool IsSettingsSectionSelected => SelectedSection == AppSection.Settings;

    public bool IsOrganizeModuleSelected => string.Equals(SelectedModuleKey, "organize", StringComparison.OrdinalIgnoreCase);
    public bool IsFlattenModuleSelected => string.Equals(SelectedModuleKey, "flatten", StringComparison.OrdinalIgnoreCase);
    public bool IsNormalizeModuleSelected => string.Equals(SelectedModuleKey, "normalize", StringComparison.OrdinalIgnoreCase);
    public bool IsMergeModuleSelected => string.Equals(SelectedModuleKey, "merge", StringComparison.OrdinalIgnoreCase);
    public bool IsFindDupModuleSelected => string.Equals(SelectedModuleKey, "finddup", StringComparison.OrdinalIgnoreCase);
    public bool IsTrayPreviewModuleSelected => string.Equals(SelectedModuleKey, "traypreview", StringComparison.OrdinalIgnoreCase);
    public bool IsTrayDepsModuleSelected => string.Equals(SelectedModuleKey, "traydeps", StringComparison.OrdinalIgnoreCase);

    public bool HasGameExecutable => !string.IsNullOrWhiteSpace(GameExecutablePath) && File.Exists(GameExecutablePath);
    public bool HasModsPath => !string.IsNullOrWhiteSpace(ModsPath) && Directory.Exists(ModsPath);
    public bool HasTrayPath => !string.IsNullOrWhiteSpace(TrayPath) && Directory.Exists(TrayPath);
    public bool HasSavesPath => !string.IsNullOrWhiteSpace(SavesPath) && Directory.Exists(SavesPath);
    public bool HasAllCorePathsValid => HasGameExecutable && HasModsPath && HasTrayPath && HasSavesPath;
    public bool IsPathHealthExpanded => !HasAllCorePathsValid;

    public bool IsGameExecutableWarning => !string.IsNullOrWhiteSpace(GameExecutablePath) && !File.Exists(GameExecutablePath);
    public bool IsModsPathWarning => !string.IsNullOrWhiteSpace(ModsPath) && !Directory.Exists(ModsPath);
    public bool IsTrayPathWarning => !string.IsNullOrWhiteSpace(TrayPath) && !Directory.Exists(TrayPath);
    public bool IsSavesPathWarning => !string.IsNullOrWhiteSpace(SavesPath) && !Directory.Exists(SavesPath);

    public bool IsGameExecutableMissing => string.IsNullOrWhiteSpace(GameExecutablePath);
    public bool IsModsPathMissing => string.IsNullOrWhiteSpace(ModsPath);
    public bool IsTrayPathMissing => string.IsNullOrWhiteSpace(TrayPath);
    public bool IsSavesPathMissing => string.IsNullOrWhiteSpace(SavesPath);

    public string GameStatusBadgeText => ResolveStatusBadgeText(HasGameExecutable, IsGameExecutableWarning);
    public string ModsStatusBadgeText => ResolveStatusBadgeText(HasModsPath, IsModsPathWarning);
    public string TrayStatusBadgeText => ResolveStatusBadgeText(HasTrayPath, IsTrayPathWarning);
    public string SavesStatusBadgeText => ResolveStatusBadgeText(HasSavesPath, IsSavesPathWarning);

    public string PathHealthSummary
    {
        get
        {
            var ready = 0;
            if (HasGameExecutable) { ready++; }
            if (HasModsPath) { ready++; }
            if (HasTrayPath) { ready++; }
            if (HasSavesPath) { ready++; }

            return $"{ready}/4 core paths ready";
        }
    }

    public bool CanLaunchGame => EnableLaunchGame && HasGameExecutable;
    public string LaunchButtonText => HasGameExecutable ? "Launch The Sims 4" : "Set Game Path First";
    public bool IsWorkspaceSectionVisible => IsModsSectionVisible || IsTraySectionVisible;
    public bool IsDerivedPathsReadOnly => !string.IsNullOrWhiteSpace(Ts4RootPath);
    public bool IsDarkThemeSelected => string.Equals(RequestedTheme, "Dark", StringComparison.OrdinalIgnoreCase);
    public bool IsLightThemeSelected => string.Equals(RequestedTheme, "Light", StringComparison.OrdinalIgnoreCase);

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await _workspaceVm.InitializeAsync();
        await LoadShellSettingsAsync();

        var discovered = _pathDiscovery.Discover();

        if (string.IsNullOrWhiteSpace(Ts4RootPath) &&
            !string.IsNullOrWhiteSpace(discovered.Ts4RootPath))
        {
            Ts4RootPath = discovered.Ts4RootPath;
        }

        if (string.IsNullOrWhiteSpace(GameExecutablePath))
        {
            GameExecutablePath = discovered.GameExecutablePath;
        }

        if (string.IsNullOrWhiteSpace(Ts4RootPath))
        {
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

        ApplyTheme();
        ApplyNavigationToWorkspace();
        SyncTrayPathsToWorkspace();
        _isInitialized = true;
    }

    public async Task PersistSettingsAsync()
    {
        await _workspaceVm.PersistSettingsAsync();

        var settings = await _settingsStore.LoadAsync();
        settings.Navigation = new AppSettings.NavigationSettings
        {
            SelectedSection = SelectedSection,
            SelectedModuleKey = SelectedModuleKey
        };
        settings.FeatureFlags = new AppSettings.FeatureFlagsSettings
        {
            EnableLaunchGame = EnableLaunchGame
        };
        settings.GameLaunch = new AppSettings.GameLaunchSettings
        {
            Ts4RootPath = Ts4RootPath,
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
        EnableLaunchGame = settings.FeatureFlags.EnableLaunchGame;
        RequestedTheme = string.IsNullOrWhiteSpace(settings.Theme.RequestedTheme)
            ? "Dark"
            : settings.Theme.RequestedTheme;
        GameExecutablePath = settings.GameLaunch.GameExecutablePath;
        ModsPath = settings.GameLaunch.ModsPath;
        TrayPath = settings.GameLaunch.TrayPath;
        SavesPath = settings.GameLaunch.SavesPath;
        Ts4RootPath = settings.GameLaunch.Ts4RootPath;

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
        ApplyNavigationToWorkspace();
    }

    private void SelectModule(string? moduleKey)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
        {
            return;
        }

        _navigation.SelectModule(moduleKey);
        SelectedModuleKey = _navigation.SelectedModuleKey;
        ApplyNavigationToWorkspace();
    }

    private void ApplyNavigationToWorkspace()
    {
        switch (SelectedModuleKey.ToLowerInvariant())
        {
            case "organize":
                _workspaceVm.Workspace = AppWorkspace.Toolkit;
                _workspaceVm.SelectedAction = SimsAction.Organize;
                break;
            case "flatten":
                _workspaceVm.Workspace = AppWorkspace.Toolkit;
                _workspaceVm.SelectedAction = SimsAction.Flatten;
                break;
            case "normalize":
                _workspaceVm.Workspace = AppWorkspace.Toolkit;
                _workspaceVm.SelectedAction = SimsAction.Normalize;
                break;
            case "merge":
                _workspaceVm.Workspace = AppWorkspace.Toolkit;
                _workspaceVm.SelectedAction = SimsAction.Merge;
                break;
            case "finddup":
                _workspaceVm.Workspace = AppWorkspace.Toolkit;
                _workspaceVm.SelectedAction = SimsAction.FindDuplicates;
                break;
            case "traydeps":
                _workspaceVm.Workspace = AppWorkspace.Toolkit;
                _workspaceVm.SelectedAction = SimsAction.TrayDependencies;
                break;
            case "traypreview":
                _workspaceVm.Workspace = AppWorkspace.TrayPreview;
                break;
        }

        SyncTrayPathsToWorkspace();
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
        await Task.CompletedTask;
    }

    private void OnWorkspaceVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(MainWindowViewModel.IsBusy), StringComparison.Ordinal))
        {
            return;
        }

        BrowseTs4RootCommand.NotifyCanExecuteChanged();
    }

    private async Task BrowseTs4RootAsync()
    {
        var selectedPaths = await _fileDialogService.PickFolderPathsAsync("Select The Sims 4 Root", allowMultiple: false);
        var selectedPath = selectedPaths.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        Ts4RootPath = selectedPath;
    }

    private void NavigateToSettingsForPathFix()
    {
        if (HasAllCorePathsValid)
        {
            return;
        }

        SelectSection(nameof(AppSection.Settings));
        Ts4RootFocusRequested?.Invoke(this, EventArgs.Empty);
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


    private void ApplyDerivedPathsFromRoot()
    {
        if (_isApplyingDerivedPaths)
        {
            return;
        }

        _isApplyingDerivedPaths = true;
        try
        {
            if (string.IsNullOrWhiteSpace(Ts4RootPath))
            {
                if (PathEquals(ModsPath, _lastDerivedModsPath))
                {
                    ModsPath = string.Empty;
                }

                if (PathEquals(TrayPath, _lastDerivedTrayPath))
                {
                    TrayPath = string.Empty;
                }

                if (PathEquals(SavesPath, _lastDerivedSavesPath))
                {
                    SavesPath = string.Empty;
                }

                _lastDerivedModsPath = string.Empty;
                _lastDerivedTrayPath = string.Empty;
                _lastDerivedSavesPath = string.Empty;
                return;
            }

            _lastDerivedModsPath = Path.Combine(Ts4RootPath, "Mods");
            _lastDerivedTrayPath = Path.Combine(Ts4RootPath, "Tray");
            _lastDerivedSavesPath = Path.Combine(Ts4RootPath, "saves");

            ModsPath = _lastDerivedModsPath;
            TrayPath = _lastDerivedTrayPath;
            SavesPath = _lastDerivedSavesPath;
        }
        finally
        {
            _isApplyingDerivedPaths = false;
        }
    }

    private void NotifyPathHealthChanged()
    {
        OnPropertyChanged(nameof(HasGameExecutable));
        OnPropertyChanged(nameof(HasModsPath));
        OnPropertyChanged(nameof(HasTrayPath));
        OnPropertyChanged(nameof(HasSavesPath));
        OnPropertyChanged(nameof(HasAllCorePathsValid));
        OnPropertyChanged(nameof(IsPathHealthExpanded));
        OnPropertyChanged(nameof(IsGameExecutableWarning));
        OnPropertyChanged(nameof(IsModsPathWarning));
        OnPropertyChanged(nameof(IsTrayPathWarning));
        OnPropertyChanged(nameof(IsSavesPathWarning));
        OnPropertyChanged(nameof(IsGameExecutableMissing));
        OnPropertyChanged(nameof(IsModsPathMissing));
        OnPropertyChanged(nameof(IsTrayPathMissing));
        OnPropertyChanged(nameof(IsSavesPathMissing));
        OnPropertyChanged(nameof(GameStatusBadgeText));
        OnPropertyChanged(nameof(ModsStatusBadgeText));
        OnPropertyChanged(nameof(TrayStatusBadgeText));
        OnPropertyChanged(nameof(SavesStatusBadgeText));
        OnPropertyChanged(nameof(PathHealthSummary));
        OnPropertyChanged(nameof(CanLaunchGame));
        OnPropertyChanged(nameof(LaunchButtonText));
        LaunchGameCommand.NotifyCanExecuteChanged();
        NavigateToSettingsForPathFixCommand.NotifyCanExecuteChanged();
    }

    private void SyncTrayPathsToWorkspace()
    {
        _workspaceVm.TrayPreview.TrayRoot = TrayPath;
        _workspaceVm.TrayDependencies.TrayPath = TrayPath;
        _workspaceVm.TrayDependencies.ModsPath = ModsPath;
    }

    private static string NormalizePath(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Trim('"');
    }

    private static string ResolveStatusBadgeText(bool isReady, bool isWarning)
    {
        if (isReady)
        {
            return "Ready";
        }

        return isWarning ? "Check" : "Missing";
    }

    private static bool PathEquals(string? left, string? right)
    {
        return string.Equals(
            NormalizePath(left),
            NormalizePath(right),
            StringComparison.OrdinalIgnoreCase);
    }
}

