using System.ComponentModel;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Application.Warmup;
using SimsModDesktop.Presentation.Dialogs;
using SimsModDesktop.Presentation.Diagnostics;
using SimsModDesktop.Presentation.ViewModels.Infrastructure;
using SimsModDesktop.Presentation.ViewModels.Saves;
using SimsModDesktop.Presentation.Services;

namespace SimsModDesktop.Presentation.ViewModels.Shell;

public sealed class MainShellViewModel : ObservableObject
{
    private readonly MainWindowViewModel _workspaceVm;
    private readonly SaveWorkspaceViewModel _savesVm;
    private readonly INavigationService _navigation;
    private readonly ShellSettingsController _settingsController;
    private readonly ShellSystemOperationsController _systemOperationsController;
    private readonly IOperationRecoveryCoordinator? _operationRecoveryCoordinator;
    private readonly IStartupPrewarmService? _startupPrewarmService;
    private readonly ILogger<MainShellViewModel> _logger;

    private AppSection _selectedSection = AppSection.Toolkit;
    private bool _isInitialized;
    private bool _isSidebarExpanded = true;

    public event EventHandler? Ts4RootFocusRequested;

    public MainShellViewModel(
        MainWindowViewModel workspaceVm,
        SaveWorkspaceViewModel savesVm,
        INavigationService navigation,
        ShellSettingsController settingsController,
        ShellSystemOperationsController systemOperationsController,
        ILogger<MainShellViewModel> logger,
        IOperationRecoveryCoordinator? operationRecoveryCoordinator = null,
        IStartupPrewarmService? startupPrewarmService = null)
    {
        _workspaceVm = workspaceVm;
        _savesVm = savesVm;
        _navigation = navigation;
        _settingsController = settingsController;
        _systemOperationsController = systemOperationsController;
        _logger = logger;
        _operationRecoveryCoordinator = operationRecoveryCoordinator;
        _startupPrewarmService = startupPrewarmService;

        SelectSectionCommand = new RelayCommand<string>(SelectSection);
        LaunchGameCommand = new AsyncRelayCommand(LaunchGameAsync, () => CanLaunchGame);
        SetDarkThemeCommand = new RelayCommand(() => RequestedTheme = "Dark");
        SetLightThemeCommand = new RelayCommand(() => RequestedTheme = "Light");
        BrowseTs4RootCommand = new AsyncRelayCommand(
            () => _settingsController.BrowseTs4RootAsync(),
            () => !_workspaceVm.IsBusy,
            disableWhileRunning: false);
        ClearCacheCommand = new AsyncRelayCommand(ClearCacheAsync, () => !_workspaceVm.IsBusy, disableWhileRunning: false);
        NavigateToSettingsForPathFixCommand = new RelayCommand(
            NavigateToSettingsForPathFix,
            () => !HasAllCorePathsValid);
        ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
        ResetDebugConfigCommand = new RelayCommand(ResetDebugConfigToDefaults, () => DebugConfigItems.Count > 0);

        _workspaceVm.PropertyChanged += OnWorkspaceVmPropertyChanged;
        _navigation.PropertyChanged += OnNavigationPropertyChanged;
        _settingsController.PropertyChanged += OnSettingsControllerPropertyChanged;
        _systemOperationsController.PropertyChanged += OnSystemOperationsControllerPropertyChanged;
    }

    public MainWindowViewModel WorkspaceVm => _workspaceVm;
    public SaveWorkspaceViewModel SavesVm => _savesVm;

    public RelayCommand<string> SelectSectionCommand { get; }
    public AsyncRelayCommand LaunchGameCommand { get; }
    public RelayCommand SetDarkThemeCommand { get; }
    public RelayCommand SetLightThemeCommand { get; }
    public AsyncRelayCommand BrowseTs4RootCommand { get; }
    public AsyncRelayCommand ClearCacheCommand { get; }
    public RelayCommand NavigateToSettingsForPathFixCommand { get; }
    public RelayCommand ToggleSidebarCommand { get; }
    public RelayCommand ResetDebugConfigCommand { get; }

    public IReadOnlyList<NavigationItem> SectionItems => _navigation.SectionItems;
    public IReadOnlyList<DebugConfigToggleItemViewModel> DebugConfigItems => _settingsController.DebugConfigItems;

    public bool IsSidebarExpanded
    {
        get => _isSidebarExpanded;
        set
        {
            if (SetProperty(ref _isSidebarExpanded, value))
            {
                OnPropertyChanged(nameof(IsSidebarCollapsed));
                OnPropertyChanged(nameof(SidebarWidth));
                OnPropertyChanged(nameof(ShellColumnSpacing));
                OnPropertyChanged(nameof(ShowSidebarBranding));
                OnPropertyChanged(nameof(ShowCompactPathHealth));
                OnPropertyChanged(nameof(ShowDetailedPathHealth));
            }
        }
    }

    public bool IsSidebarCollapsed => !IsSidebarExpanded;
    public double SidebarWidth => IsSidebarExpanded ? 240 : 56;
    public double ShellColumnSpacing => IsSidebarExpanded ? 24 : 8;
    public bool ShowSidebarBranding => IsSidebarExpanded;

    public AppSection SelectedSection
    {
        get => _selectedSection;
        private set
        {
            if (!SetProperty(ref _selectedSection, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsToolkitSectionVisible));
            OnPropertyChanged(nameof(IsModsSectionVisible));
            OnPropertyChanged(nameof(IsTraySectionVisible));
            OnPropertyChanged(nameof(IsSavesVisible));
            OnPropertyChanged(nameof(IsSettingsVisible));
            OnPropertyChanged(nameof(IsToolkitSectionSelected));
            OnPropertyChanged(nameof(IsModsSectionSelected));
            OnPropertyChanged(nameof(IsTraySectionSelected));
            OnPropertyChanged(nameof(IsSavesSectionSelected));
            OnPropertyChanged(nameof(IsSettingsSectionSelected));
            OnPropertyChanged(nameof(IsWorkspaceSectionVisible));
            _workspaceVm.ModPreviewWorkspace.SetIsActive(_selectedSection == AppSection.Mods);
            _workspaceVm.TrayPreviewWorkspace.SetIsActive(_selectedSection == AppSection.Tray);
            _savesVm.SetIsActive(_selectedSection == AppSection.Saves);
        }
    }

    public bool EnableLaunchGame
    {
        get => _settingsController.EnableLaunchGame;
        set => _settingsController.EnableLaunchGame = value;
    }

    public string RequestedTheme
    {
        get => _settingsController.RequestedTheme;
        set => _settingsController.RequestedTheme = value;
    }

    public string Ts4RootPath
    {
        get => _settingsController.Ts4RootPath;
        set => _settingsController.Ts4RootPath = value;
    }

    public string GameExecutablePath
    {
        get => _settingsController.GameExecutablePath;
        set => _settingsController.GameExecutablePath = value;
    }

    public string ModsPath
    {
        get => _settingsController.ModsPath;
        set => _settingsController.ModsPath = value;
    }

    public string TrayPath
    {
        get => _settingsController.TrayPath;
        set => _settingsController.TrayPath = value;
    }

    public string SavesPath
    {
        get => _settingsController.SavesPath;
        set => _settingsController.SavesPath = value;
    }

    public string LaunchGameStatus
    {
        get => _systemOperationsController.LaunchGameStatus;
    }

    public string CacheMaintenanceStatus
    {
        get => _systemOperationsController.CacheMaintenanceStatus;
    }

    public bool IsToolkitSectionVisible => SelectedSection == AppSection.Toolkit;
    public bool IsModsSectionVisible => SelectedSection == AppSection.Mods;
    public bool IsTraySectionVisible => SelectedSection == AppSection.Tray;
    public bool IsSavesVisible => SelectedSection == AppSection.Saves;
    public bool IsSettingsVisible => SelectedSection == AppSection.Settings;
    public bool IsToolkitSectionSelected => SelectedSection == AppSection.Toolkit;
    public bool IsModsSectionSelected => SelectedSection == AppSection.Mods;
    public bool IsTraySectionSelected => SelectedSection == AppSection.Tray;
    public bool IsSavesSectionSelected => SelectedSection == AppSection.Saves;
    public bool IsSettingsSectionSelected => SelectedSection == AppSection.Settings;

    public bool HasGameExecutable => _settingsController.HasGameExecutable;
    public bool HasModsPath => _settingsController.HasModsPath;
    public bool HasTrayPath => _settingsController.HasTrayPath;
    public bool HasSavesPath => _settingsController.HasSavesPath;
    public bool HasAllCorePathsValid => _settingsController.HasAllCorePathsValid;
    public bool IsPathHealthExpanded => _settingsController.IsPathHealthExpanded;
    public bool ShowCompactPathHealth => IsSidebarCollapsed || !IsPathHealthExpanded;
    public bool ShowDetailedPathHealth => IsSidebarExpanded && IsPathHealthExpanded;
    public bool IsGameExecutableWarning => _settingsController.IsGameExecutableWarning;
    public bool IsModsPathWarning => _settingsController.IsModsPathWarning;
    public bool IsTrayPathWarning => _settingsController.IsTrayPathWarning;
    public bool IsSavesPathWarning => _settingsController.IsSavesPathWarning;
    public bool IsGameExecutableMissing => _settingsController.IsGameExecutableMissing;
    public bool IsModsPathMissing => _settingsController.IsModsPathMissing;
    public bool IsTrayPathMissing => _settingsController.IsTrayPathMissing;
    public bool IsSavesPathMissing => _settingsController.IsSavesPathMissing;
    public string GameStatusBadgeText => _settingsController.GameStatusBadgeText;
    public string ModsStatusBadgeText => _settingsController.ModsStatusBadgeText;
    public string TrayStatusBadgeText => _settingsController.TrayStatusBadgeText;
    public string SavesStatusBadgeText => _settingsController.SavesStatusBadgeText;
    public string PathHealthSummary => _settingsController.PathHealthSummary;
    public bool CanLaunchGame => _settingsController.CanLaunchGame;
    public string LaunchButtonText => _settingsController.LaunchButtonText;
    public bool IsWorkspaceSectionVisible => IsToolkitSectionVisible || IsModsSectionVisible || IsTraySectionVisible;
    public bool IsDerivedPathsReadOnly => _settingsController.IsDerivedPathsReadOnly;
    public bool IsDarkThemeSelected => _settingsController.IsDarkThemeSelected;
    public bool IsLightThemeSelected => _settingsController.IsLightThemeSelected;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        var timing = PerformanceLogScope.Begin(_logger, "shell.initialize");

        await _workspaceVm.InitializeAsync();
        timing.Mark("workspace.initialized");
        await _settingsController.InitializeAsync();
        timing.Mark("settings.initialized");

        _navigation.SelectSection(AppSection.Toolkit);
        SelectedSection = _navigation.SelectedSection;
        ApplyNavigationToWorkspace();
        timing.Mark("navigation.bound");
        _startupPrewarmService?.QueueTrayDependencyStartupPrewarm(
            ModsPath,
            () => _workspaceVm.IsBusy || _workspaceVm.HasRunningTrayExportTasks);
        _startupPrewarmService?.QueueModsQueryStartupPrewarm(
            new ModItemCatalogQuery
            {
                ModsRoot = ModsPath,
                SearchQuery = _workspaceVm.ModPreview.SearchQuery,
                EntityKindFilter = _workspaceVm.ModPreview.PackageTypeFilter,
                SubTypeFilter = _workspaceVm.ModPreview.ScopeFilter,
                SortBy = _workspaceVm.ModPreview.SortBy,
                PageIndex = 1,
                PageSize = 50
            },
            () => _workspaceVm.IsBusy || _workspaceVm.HasRunningTrayExportTasks);
        var saveSettings = _savesVm.ToSettings();
        _startupPrewarmService?.QueueSaveStartupPrewarm(
            saveSettings.SelectedSavePath,
            saveSettings.SelectedPreviewHouseholdKey,
            () => _workspaceVm.IsBusy || _workspaceVm.HasRunningTrayExportTasks || _savesVm.IsBusy);

        if (_operationRecoveryCoordinator is not null)
        {
            await _operationRecoveryCoordinator.InitializeAndPromptAsync(_workspaceVm.ResumeRecoverableOperationAsync);
            timing.Mark("recovery.initialized");
        }

        _isInitialized = true;
        timing.Success();
    }

    public async Task PersistSettingsAsync()
    {
        _startupPrewarmService?.Reset();
        await _workspaceVm.PersistSettingsAsync();
        await _settingsController.PersistAsync(SelectedSection);
    }

    private void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
    }

    private void ResetDebugConfigToDefaults()
    {
        _settingsController.ResetDebugConfigToDefaults();
        _workspaceVm.AppendSystemLog("[debug-config] reset-to-defaults");
    }

    private void SelectSection(string? sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            _logger.LogWarning(
                "{Event} status={Status} domain={Domain} section={Section} reason={Reason}",
                LogEvents.UiPageSwitchBlocked,
                "blocked",
                "shell",
                "<empty>",
                "section-key-empty");
            return;
        }

        if (!Enum.TryParse<AppSection>(sectionKey, ignoreCase: true, out var section))
        {
            _logger.LogWarning(
                "{Event} status={Status} domain={Domain} section={Section} reason={Reason}",
                LogEvents.UiPageSwitchBlocked,
                "blocked",
                "shell",
                sectionKey,
                "invalid-section-key");
            return;
        }

        var previousSection = SelectedSection;
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} fromSection={FromSection} toSection={ToSection}",
            LogEvents.UiPageSwitchStart,
            "start",
            "shell",
            previousSection,
            section);
        _navigation.SelectSection(section);
        SelectedSection = _navigation.SelectedSection;
        ApplyNavigationToWorkspace();
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} fromSection={FromSection} toSection={ToSection}",
            LogEvents.UiPageSwitchDone,
            "done",
            "shell",
            previousSection,
            SelectedSection);
    }

    private void ApplyNavigationToWorkspace()
    {
        switch (SelectedSection)
        {
            case AppSection.Toolkit:
                _workspaceVm.Workspace = AppWorkspace.Toolkit;
                break;
            case AppSection.Mods:
                _workspaceVm.Workspace = AppWorkspace.ModPreview;
                break;
            case AppSection.Tray:
                _workspaceVm.Workspace = AppWorkspace.TrayPreview;
                break;
        }

        _workspaceVm.ModPreview.ModsRoot = ModsPath;
        _workspaceVm.TrayPreview.TrayRoot = TrayPath;
        _workspaceVm.TrayDependencies.TrayPath = TrayPath;
        _workspaceVm.TrayDependencies.ModsPath = ModsPath;
    }

    private async Task LaunchGameAsync()
    {
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} command={Command}",
            LogEvents.UiCommandInvoke,
            "invoke",
            "shell",
            "LaunchGame");
        await _systemOperationsController.LaunchGameAsync(GameExecutablePath);
    }

    private void OnWorkspaceVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(MainWindowViewModel.IsBusy), StringComparison.Ordinal))
        {
            return;
        }

        BrowseTs4RootCommand.NotifyCanExecuteChanged();
        ClearCacheCommand.NotifyCanExecuteChanged();
    }

    private void NavigateToSettingsForPathFix()
    {
        if (HasAllCorePathsValid)
        {
            _logger.LogInformation(
                "{Event} status={Status} domain={Domain} command={Command} reason={Reason}",
                LogEvents.UiCommandBlocked,
                "blocked",
                "shell",
                "NavigateToSettingsForPathFix",
                "all-paths-valid");
            return;
        }

        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} command={Command}",
            LogEvents.UiCommandInvoke,
            "invoke",
            "shell",
            "NavigateToSettingsForPathFix");
        SelectSection(nameof(AppSection.Settings));
        Ts4RootFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task ClearCacheAsync()
    {
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} command={Command} section={Section}",
            LogEvents.UiCommandInvoke,
            "invoke",
            "shell",
            "ClearCache",
            SelectedSection);
        await _systemOperationsController.ClearCacheAsync(SelectedSection == AppSection.Tray);
    }

    private void OnNavigationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(INavigationService.SelectedSection), StringComparison.Ordinal))
        {
            SelectedSection = _navigation.SelectedSection;
            OnPropertyChanged(nameof(SectionItems));
        }
    }

    private void OnSettingsControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName))
        {
            return;
        }

        OnPropertyChanged(e.PropertyName);

        if (string.Equals(e.PropertyName, nameof(CanLaunchGame), StringComparison.Ordinal))
        {
            LaunchGameCommand.NotifyCanExecuteChanged();
        }

        if (string.Equals(e.PropertyName, nameof(IsPathHealthExpanded), StringComparison.Ordinal) ||
            string.Equals(e.PropertyName, nameof(HasAllCorePathsValid), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ShowCompactPathHealth));
            OnPropertyChanged(nameof(ShowDetailedPathHealth));
        }

        if (string.Equals(e.PropertyName, nameof(HasAllCorePathsValid), StringComparison.Ordinal))
        {
            NavigateToSettingsForPathFixCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnSystemOperationsControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.PropertyName))
        {
            OnPropertyChanged(e.PropertyName);
        }
    }
}
