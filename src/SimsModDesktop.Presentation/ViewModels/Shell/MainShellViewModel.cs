using System.ComponentModel;
using Avalonia.Threading;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Presentation.Dialogs;
using SimsModDesktop.Presentation.ViewModels.Infrastructure;
using SimsModDesktop.Presentation.ViewModels.Saves;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Presentation.ViewModels.Shell;

public sealed class MainShellViewModel : ObservableObject
{
    private readonly MainWindowViewModel _workspaceVm;
    private readonly SaveWorkspaceViewModel _savesVm;
    private readonly INavigationService _navigation;
    private readonly ShellSettingsController _settingsController;
    private readonly ShellSystemOperationsController _systemOperationsController;
    private readonly IOperationRecoveryCoordinator? _operationRecoveryCoordinator;
    private readonly ITrayDependencyCacheWarmupService? _trayDependencyCacheWarmupService;

    private AppSection _selectedSection = AppSection.Toolkit;
    private bool _isInitialized;
    private bool _isSidebarExpanded = true;
    private bool _trayDependencyCacheWarmupQueued;
    private bool _isTrayDependencyCacheWarmupVisible;
    private bool _isTrayDependencyCacheWarmupRunning;
    private bool _isTrayDependencyCacheWarmupIndeterminate = true;
    private int _trayDependencyCacheWarmupPercent;
    private string _trayDependencyCacheWarmupDetail = string.Empty;
    private int _lastWarmupProgressLogPercent = -1;

    public event EventHandler? Ts4RootFocusRequested;

    public MainShellViewModel(
        MainWindowViewModel workspaceVm,
        SaveWorkspaceViewModel savesVm,
        INavigationService navigation,
        ShellSettingsController settingsController,
        ShellSystemOperationsController systemOperationsController,
        IOperationRecoveryCoordinator? operationRecoveryCoordinator = null,
        ITrayDependencyCacheWarmupService? trayDependencyCacheWarmupService = null)
    {
        _workspaceVm = workspaceVm;
        _savesVm = savesVm;
        _navigation = navigation;
        _settingsController = settingsController;
        _systemOperationsController = systemOperationsController;
        _operationRecoveryCoordinator = operationRecoveryCoordinator;
        _trayDependencyCacheWarmupService = trayDependencyCacheWarmupService;

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
    public bool IsTrayDependencyCacheWarmupVisible
    {
        get => _isTrayDependencyCacheWarmupVisible;
        private set => SetProperty(ref _isTrayDependencyCacheWarmupVisible, value);
    }

    public bool IsTrayDependencyCacheWarmupRunning
    {
        get => _isTrayDependencyCacheWarmupRunning;
        private set => SetProperty(ref _isTrayDependencyCacheWarmupRunning, value);
    }

    public bool IsTrayDependencyCacheWarmupIndeterminate
    {
        get => _isTrayDependencyCacheWarmupIndeterminate;
        private set => SetProperty(ref _isTrayDependencyCacheWarmupIndeterminate, value);
    }

    public int TrayDependencyCacheWarmupPercent
    {
        get => _trayDependencyCacheWarmupPercent;
        private set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref _trayDependencyCacheWarmupPercent, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(TrayDependencyCacheWarmupPercentText));
        }
    }

    public string TrayDependencyCacheWarmupPercentText => $"{TrayDependencyCacheWarmupPercent}%";

    public string TrayDependencyCacheWarmupDetail
    {
        get => _trayDependencyCacheWarmupDetail;
        private set => SetProperty(ref _trayDependencyCacheWarmupDetail, value);
    }

    public string TrayDependencyCacheWarmupTitle => "Preparing startup dependency cache";
    public string TrayDependencyCacheWarmupHint =>
        "First-time indexing on very large Mods libraries can take several minutes.";

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

        await _workspaceVm.InitializeAsync();
        await _settingsController.InitializeAsync();

        _navigation.SelectSection(AppSection.Toolkit);
        SelectedSection = _navigation.SelectedSection;
        ApplyNavigationToWorkspace();
        QueueTrayDependencyCacheWarmup();

        if (_operationRecoveryCoordinator is not null)
        {
            await _operationRecoveryCoordinator.InitializeAndPromptAsync(_workspaceVm.ResumeRecoverableOperationAsync);
        }

        _isInitialized = true;
    }

    public async Task PersistSettingsAsync()
    {
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
            return;
        }

        if (!Enum.TryParse<AppSection>(sectionKey, ignoreCase: true, out var section))
        {
            return;
        }

        _navigation.SelectSection(section);
        SelectedSection = _navigation.SelectedSection;
        ApplyNavigationToWorkspace();
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

    private void QueueTrayDependencyCacheWarmup()
    {
        if (_trayDependencyCacheWarmupService is null || _trayDependencyCacheWarmupQueued)
        {
            return;
        }

        if (!_settingsController.EnableStartupTrayCacheWarmup)
        {
            _workspaceVm.AppendSystemLog("[startup][tray-cache] warmup-disabled-by-config");
            return;
        }

        var modsPath = ModsPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(modsPath) || !Directory.Exists(modsPath))
        {
            return;
        }

        _trayDependencyCacheWarmupQueued = true;
        _lastWarmupProgressLogPercent = -1;
        SetTrayDependencyCacheWarmupState(
            visible: _settingsController.ShowStartupTrayCacheWarmupBanner,
            running: true,
            indeterminate: true,
            percent: 0,
            detail: "Checking package index cache...");
        _workspaceVm.AppendSystemLog($"[startup][tray-cache] warmup-start modsPath={modsPath}");

        var progress = new Progress<TrayDependencyExportProgress>(HandleTrayDependencyCacheWarmupProgress);
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _trayDependencyCacheWarmupService
                    .WarmupIfMissingAsync(modsPath, progress)
                    .ConfigureAwait(false);

                if (result.WarmedUp)
                {
                    SetTrayDependencyCacheWarmupState(
                        visible: _settingsController.ShowStartupTrayCacheWarmupBanner,
                        running: false,
                        indeterminate: false,
                        percent: 100,
                        detail: string.IsNullOrWhiteSpace(result.Message)
                            ? "Startup package index cache is ready."
                            : result.Message);
                    _workspaceVm.AppendSystemLog($"[startup][tray-cache] warmup-completed packages={result.PackageCount}");
                    await Task.Delay(2500).ConfigureAwait(false);
                    SetTrayDependencyCacheVisibility(false);
                    return;
                }

                _workspaceVm.AppendSystemLog(
                    "[startup][tray-cache] " +
                    (string.IsNullOrWhiteSpace(result.Message)
                        ? "warmup-skipped"
                        : result.Message));
                SetTrayDependencyCacheVisibility(false);
            }
            catch (Exception ex)
            {
                _workspaceVm.AppendSystemLog("[startup][tray-cache] warmup-failed: " + ex.Message);
                SetTrayDependencyCacheWarmupState(
                    visible: _settingsController.ShowStartupTrayCacheWarmupBanner,
                    running: false,
                    indeterminate: false,
                    percent: 0,
                    detail: "Startup cache warmup failed. First tray export may take longer.");
            }
        });
    }

    private void HandleTrayDependencyCacheWarmupProgress(TrayDependencyExportProgress progress)
    {
        var normalizedPercent = Math.Clamp(progress.Percent, 0, 100);
        var detail = string.IsNullOrWhiteSpace(progress.Detail)
            ? "Indexing packages..."
            : progress.Detail.Trim();
        var indeterminate = normalizedPercent == 0 && progress.Stage != TrayDependencyExportStage.Completed;
        var running = progress.Stage != TrayDependencyExportStage.Completed;

        SetTrayDependencyCacheWarmupState(
            visible: _settingsController.ShowStartupTrayCacheWarmupBanner,
            running: running,
            indeterminate: indeterminate,
            percent: running ? normalizedPercent : 100,
            detail: detail);

        if (!_settingsController.EnableStartupTrayCacheWarmupVerboseLog)
        {
            return;
        }

        if (progress.Stage != TrayDependencyExportStage.IndexingPackages)
        {
            return;
        }

        if (_lastWarmupProgressLogPercent >= 0 &&
            normalizedPercent < 100 &&
            normalizedPercent < _lastWarmupProgressLogPercent + 10)
        {
            return;
        }

        _lastWarmupProgressLogPercent = normalizedPercent;
        _workspaceVm.AppendSystemLog(
            $"[startup][tray-cache] progress={normalizedPercent}% detail={detail}");
    }

    private void SetTrayDependencyCacheWarmupState(
        bool visible,
        bool running,
        bool indeterminate,
        int percent,
        string detail)
    {
        ExecuteOnUi(() =>
        {
            IsTrayDependencyCacheWarmupVisible = visible;
            IsTrayDependencyCacheWarmupRunning = running;
            IsTrayDependencyCacheWarmupIndeterminate = indeterminate;
            TrayDependencyCacheWarmupPercent = percent;
            TrayDependencyCacheWarmupDetail = detail;
        });
    }

    private void SetTrayDependencyCacheVisibility(bool visible)
    {
        ExecuteOnUi(() => IsTrayDependencyCacheWarmupVisible = visible);
    }

    private static void ExecuteOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    private async Task LaunchGameAsync()
    {
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
            return;
        }

        SelectSection(nameof(AppSection.Settings));
        Ts4RootFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task ClearCacheAsync()
    {
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

        if (string.Equals(
                e.PropertyName,
                nameof(ShellSettingsController.ShowStartupTrayCacheWarmupBanner),
                StringComparison.Ordinal) &&
            IsTrayDependencyCacheWarmupRunning)
        {
            SetTrayDependencyCacheVisibility(_settingsController.ShowStartupTrayCacheWarmupBanner);
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
