using System.Collections.ObjectModel;
using System.ComponentModel;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Presentation.Dialogs;
using SimsModDesktop.Presentation.ViewModels.Infrastructure;
using SimsModDesktop.Presentation.ViewModels.Saves;

namespace SimsModDesktop.Presentation.ViewModels.Shell;

public sealed class ShellSettingsController : ObservableObject
{
    private static readonly DebugToggleDefinition[] DebugToggleDefinitions =
    [
        new(
            DebugConfigKeys.StartupTrayCacheWarmupEnabled,
            "Startup Tray Cache Warmup",
            "Build tray dependency package index on startup when no local cache exists.",
            DefaultValue: true),
        new(
            DebugConfigKeys.StartupTrayCacheWarmupShowBanner,
            "Warmup Progress Banner",
            "Show startup warmup progress panel and status text in Shell.",
            DefaultValue: true),
        new(
            DebugConfigKeys.StartupTrayCacheWarmupVerboseLog,
            "Warmup Verbose Log",
            "Write warmup progress checkpoints into the toolkit log.",
            DefaultValue: true)
    ];

    private readonly MainWindowViewModel _workspaceVm;
    private readonly SaveWorkspaceViewModel _savesVm;
    private readonly IFileDialogService _fileDialogService;
    private readonly ISettingsStore _settingsStore;
    private readonly IDebugConfigStore _debugConfigStore;
    private readonly IAppThemeService _appThemeService;
    private readonly ITS4PathDiscoveryService _pathDiscovery;
    private readonly Dictionary<string, DebugConfigToggleItemViewModel> _debugConfigItemsByKey =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _enableLaunchGame = true;
    private string _requestedTheme = "Dark";
    private string _ts4RootPath = string.Empty;
    private string _gameExecutablePath = string.Empty;
    private string _modsPath = string.Empty;
    private string _trayPath = string.Empty;
    private string _savesPath = string.Empty;
    private bool _isApplyingDerivedPaths;
    private string _lastDerivedModsPath = string.Empty;
    private string _lastDerivedTrayPath = string.Empty;
    private string _lastDerivedSavesPath = string.Empty;

    public ShellSettingsController(
        MainWindowViewModel workspaceVm,
        SaveWorkspaceViewModel savesVm,
        IFileDialogService fileDialogService,
        ISettingsStore settingsStore,
        IDebugConfigStore debugConfigStore,
        IAppThemeService appThemeService,
        ITS4PathDiscoveryService pathDiscovery)
    {
        _workspaceVm = workspaceVm;
        _savesVm = savesVm;
        _fileDialogService = fileDialogService;
        _settingsStore = settingsStore;
        _debugConfigStore = debugConfigStore;
        _appThemeService = appThemeService;
        _pathDiscovery = pathDiscovery;

        DebugConfigItems = new ObservableCollection<DebugConfigToggleItemViewModel>();
        InitializeDebugConfigItems();
    }

    public ObservableCollection<DebugConfigToggleItemViewModel> DebugConfigItems { get; }

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
            _appThemeService.Apply(_requestedTheme);
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
            SyncWorkspacePaths();
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
            SyncWorkspacePaths();
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
            _savesVm.SavesPath = _savesPath;
        }
    }

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
    public bool IsDerivedPathsReadOnly => !string.IsNullOrWhiteSpace(Ts4RootPath);
    public bool IsDarkThemeSelected => string.Equals(RequestedTheme, "Dark", StringComparison.OrdinalIgnoreCase);
    public bool IsLightThemeSelected => string.Equals(RequestedTheme, "Light", StringComparison.OrdinalIgnoreCase);
    public bool EnableStartupTrayCacheWarmup => GetDebugToggleValue(DebugConfigKeys.StartupTrayCacheWarmupEnabled);
    public bool ShowStartupTrayCacheWarmupBanner => GetDebugToggleValue(DebugConfigKeys.StartupTrayCacheWarmupShowBanner);
    public bool EnableStartupTrayCacheWarmupVerboseLog => GetDebugToggleValue(DebugConfigKeys.StartupTrayCacheWarmupVerboseLog);

    public async Task InitializeAsync()
    {
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

        _appThemeService.Apply(RequestedTheme);
        SyncWorkspacePaths();
    }

    public async Task PersistAsync(AppSection selectedSection)
    {
        var settings = await _settingsStore.LoadAsync();
        settings.Navigation = new AppSettings.NavigationSettings
        {
            SelectedSection = selectedSection
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
        settings.Saves = _savesVm.ToSettings();

        await _settingsStore.SaveAsync(settings);
        await _debugConfigStore.SaveAsync(BuildDebugConfigEntries());
    }

    public async Task BrowseTs4RootAsync()
    {
        var selectedPaths = await _fileDialogService.PickFolderPathsAsync("Select The Sims 4 Root", allowMultiple: false);
        var selectedPath = selectedPaths.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        Ts4RootPath = selectedPath;
    }

    private async Task LoadShellSettingsAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        EnableLaunchGame = settings.FeatureFlags.EnableLaunchGame;
        RequestedTheme = _appThemeService.Normalize(settings.Theme.RequestedTheme);
        _savesVm.LoadFromSettings(settings.Saves ?? new AppSettings.SavesSettings());
        GameExecutablePath = settings.GameLaunch.GameExecutablePath;
        ModsPath = settings.GameLaunch.ModsPath;
        TrayPath = settings.GameLaunch.TrayPath;
        SavesPath = settings.GameLaunch.SavesPath;
        Ts4RootPath = settings.GameLaunch.Ts4RootPath;
        var debugConfig = await _debugConfigStore.LoadAsync();
        ApplyDebugConfig(debugConfig);
        await _debugConfigStore.EnsureTemplateAsync(BuildDebugConfigTemplateEntries());
    }

    public void ResetDebugConfigToDefaults()
    {
        foreach (var item in DebugConfigItems)
        {
            item.ResetToDefault();
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
    }

    private void SyncWorkspacePaths()
    {
        _workspaceVm.ModPreview.ModsRoot = ModsPath;
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

    private void InitializeDebugConfigItems()
    {
        DebugConfigItems.Clear();
        _debugConfigItemsByKey.Clear();

        foreach (var definition in DebugToggleDefinitions)
        {
            var item = new DebugConfigToggleItemViewModel(
                definition.Key,
                definition.DisplayName,
                definition.Description,
                definition.DefaultValue);
            item.PropertyChanged += OnDebugConfigItemPropertyChanged;
            DebugConfigItems.Add(item);
            _debugConfigItemsByKey[definition.Key] = item;
        }
    }

    private void ApplyDebugConfig(IReadOnlyDictionary<string, string>? valueMap)
    {
        valueMap ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in DebugToggleDefinitions)
        {
            if (!_debugConfigItemsByKey.TryGetValue(definition.Key, out var item))
            {
                continue;
            }

            if (valueMap.TryGetValue(definition.Key, out var rawValue) &&
                bool.TryParse(rawValue, out var parsed))
            {
                item.Value = parsed;
                continue;
            }

            item.Value = definition.DefaultValue;
        }
    }

    private IReadOnlyDictionary<string, string> BuildDebugConfigEntries()
    {
        return DebugConfigItems
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                item => item.Key,
                item => item.Value ? bool.TrueString : bool.FalseString,
                StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyList<DebugConfigTemplateEntry> BuildDebugConfigTemplateEntries()
    {
        return DebugToggleDefinitions
            .OrderBy(definition => definition.Key, StringComparer.OrdinalIgnoreCase)
            .Select(definition => new DebugConfigTemplateEntry(
                definition.Key,
                definition.DefaultValue ? bool.TrueString : bool.FalseString,
                definition.Description))
            .ToList();
    }

    private bool GetDebugToggleValue(string key)
    {
        if (_debugConfigItemsByKey.TryGetValue(key, out var item))
        {
            return item.Value;
        }

        for (var index = 0; index < DebugToggleDefinitions.Length; index++)
        {
            if (string.Equals(DebugToggleDefinitions[index].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return DebugToggleDefinitions[index].DefaultValue;
            }
        }

        return false;
    }

    private void OnDebugConfigItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(DebugConfigToggleItemViewModel.Value), StringComparison.Ordinal))
        {
            return;
        }

        OnPropertyChanged(nameof(EnableStartupTrayCacheWarmup));
        OnPropertyChanged(nameof(ShowStartupTrayCacheWarmupBanner));
        OnPropertyChanged(nameof(EnableStartupTrayCacheWarmupVerboseLog));
    }

    private static class DebugConfigKeys
    {
        public const string StartupTrayCacheWarmupEnabled = "startup.tray_cache_warmup.enabled";
        public const string StartupTrayCacheWarmupShowBanner = "startup.tray_cache_warmup.show_banner";
        public const string StartupTrayCacheWarmupVerboseLog = "startup.tray_cache_warmup.verbose_log";
    }

    private sealed record DebugToggleDefinition(
        string Key,
        string DisplayName,
        string Description,
        bool DefaultValue);
}
