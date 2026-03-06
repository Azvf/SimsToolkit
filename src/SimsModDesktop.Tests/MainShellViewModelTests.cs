using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Application.Preview;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Infrastructure.Localization;
using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.Infrastructure.TextureCompression;
using SimsModDesktop.Infrastructure.TextureProcessing;
using SimsModDesktop.Presentation.Dialogs;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.Presentation.ViewModels;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;
using SimsModDesktop.Presentation.ViewModels.Shell;
using SimsModDesktop.Presentation.ViewModels.Saves;
using SimsModDesktop.Presentation.Warmup;

namespace SimsModDesktop.Tests;

public sealed class MainShellViewModelTests
{
    [Fact]
    public void NavigateToSettingsForPathFix_WhenPathIsInvalid_SelectsSettingsAndRequestsFocus()
    {
        var vm = CreateShellViewModel();
        var focusRequested = false;
        vm.Ts4RootFocusRequested += (_, _) => focusRequested = true;

        Assert.True(vm.NavigateToSettingsForPathFixCommand.CanExecute(null));

        vm.NavigateToSettingsForPathFixCommand.Execute(null);

        Assert.Equal(AppSection.Settings, vm.SelectedSection);
        Assert.True(vm.IsSettingsVisible);
        Assert.True(focusRequested);
    }

    [Fact]
    public void PathHealthCollapses_WhenAllCorePathsAreValid()
    {
        var vm = CreateShellViewModel();
        using var root = new TempDirectory();
        using var gameExecutable = new TempFile("exe");

        var modsPath = Directory.CreateDirectory(Path.Combine(root.Path, "Mods")).FullName;
        var trayPath = Directory.CreateDirectory(Path.Combine(root.Path, "Tray")).FullName;
        var savesPath = Directory.CreateDirectory(Path.Combine(root.Path, "saves")).FullName;

        vm.GameExecutablePath = gameExecutable.Path;
        vm.ModsPath = modsPath;
        vm.TrayPath = trayPath;
        vm.SavesPath = savesPath;

        var focusRequested = false;
        vm.Ts4RootFocusRequested += (_, _) => focusRequested = true;

        Assert.True(vm.HasAllCorePathsValid);
        Assert.False(vm.IsPathHealthExpanded);
        Assert.False(vm.NavigateToSettingsForPathFixCommand.CanExecute(null));

        vm.NavigateToSettingsForPathFixCommand.Execute(null);

        Assert.False(focusRequested);
        Assert.Equal(AppSection.Toolkit, vm.SelectedSection);
    }

    [Fact]
    public void SidebarCollapse_UsesCompactPathHealthState()
    {
        var vm = CreateShellViewModel();

        Assert.True(vm.IsSidebarExpanded);
        Assert.False(vm.IsSidebarCollapsed);
        Assert.True(vm.ShowDetailedPathHealth);
        Assert.False(vm.ShowCompactPathHealth);
        Assert.Equal(240, vm.SidebarWidth);
        Assert.Equal(24, vm.ShellColumnSpacing);

        vm.ToggleSidebarCommand.Execute(null);

        Assert.False(vm.IsSidebarExpanded);
        Assert.True(vm.IsSidebarCollapsed);
        Assert.False(vm.ShowDetailedPathHealth);
        Assert.True(vm.ShowCompactPathHealth);
        Assert.Equal(56, vm.SidebarWidth);
        Assert.Equal(8, vm.ShellColumnSpacing);
    }

    [Fact]
    public async Task ClearCache_UpdatesStatusMessage()
    {
        var cacheService = new FakeAppCacheMaintenanceService
        {
            NextResult = new AppCacheMaintenanceResult
            {
                Success = true,
                RemovedDirectoryCount = 2,
                Message = "Cleared 2 cache folder(s). Restart the app to drop in-memory caches."
            }
        };
        var vm = CreateShellViewModel(cacheService);

        Assert.True(vm.ClearCacheCommand.CanExecute(null));

        vm.ClearCacheCommand.Execute(null);
        await Task.Delay(10);

        Assert.Equal(1, cacheService.CallCount);
        Assert.Equal("Cleared 2 cache folder(s). Restart the app to drop in-memory caches.", vm.CacheMaintenanceStatus);
    }

    [Fact]
    public async Task ClearCache_ResetsTrayPreviewAndReloadsWhenReturningToTray()
    {
        var cacheService = new FakeAppCacheMaintenanceService();
        var trayThumbnailService = new FakeTrayThumbnailService();
        var trayPreviewCoordinator = new FakePreviewQueryService();
        var vm = CreateShellViewModel(cacheService, trayPreviewCoordinator, trayThumbnailService);
        using var trayRoot = new TempDirectory();
        using var modsRoot = new TempDirectory();

        vm.TrayPath = trayRoot.Path;
        vm.ModsPath = modsRoot.Path;
        await Task.Delay(50);
        var baselineLoadCount = trayPreviewCoordinator.LoadCount;

        vm.ClearCacheCommand.Execute(null);
        await Task.Delay(50);

        Assert.True(trayPreviewCoordinator.InvalidateCount >= 1);
        Assert.True(trayThumbnailService.ResetCount >= 1);

        vm.SelectSectionCommand.Execute(nameof(AppSection.Tray));
        await Task.Delay(50);

        Assert.True(trayPreviewCoordinator.LoadCount > baselineLoadCount);
    }

    [Fact]
    public async Task InitializeAsync_IgnoresSavedSectionAndStartsInToolkit()
    {
        var settings = new AppSettings();
        settings.Navigation.SelectedSection = AppSection.Tray;
        var vm = CreateShellViewModel(initialSettings: settings);

        await vm.InitializeAsync();

        Assert.Equal(AppSection.Toolkit, vm.SelectedSection);
        Assert.True(vm.IsToolkitSectionVisible);
        Assert.Equal(AppSection.Toolkit, vm.SelectedSection);
    }

    [Fact]
    public async Task InitializeAsync_NormalizesSavedThemeAndAppliesIt()
    {
        var settings = new AppSettings();
        settings.Theme.RequestedTheme = "Light";
        var themeService = new FakeAppThemeService();
        var vm = CreateShellViewModel(initialSettings: settings, themeService: themeService);

        await vm.InitializeAsync();

        Assert.Equal("Light", vm.RequestedTheme);
        Assert.Equal("Light", themeService.LastAppliedTheme);
        Assert.True(themeService.ApplyCallCount > 0);
    }

    [Fact]
    public void SelectSection_EmitsPageSwitchLogs()
    {
        var provider = new TestLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider).SetMinimumLevel(LogLevel.Debug));
        var vm = CreateShellViewModel(loggerFactory: loggerFactory);

        vm.SelectSectionCommand.Execute(nameof(AppSection.Tray));

        Assert.Contains(provider.Entries, entry =>
            entry.Message.Contains("ui.page.switch.start", StringComparison.Ordinal));
        Assert.Contains(provider.Entries, entry =>
            entry.Message.Contains("ui.page.switch.done", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InitializeAsync_DoesNotEmitStartupWarmupLogs_WhenModsPathExists()
    {
        using var root = new TempDirectory();
        var modsPath = Directory.CreateDirectory(Path.Combine(root.Path, "Mods")).FullName;
        var settings = new AppSettings();
        settings.GameLaunch.ModsPath = modsPath;

        var vm = CreateShellViewModel(initialSettings: settings);

        await vm.InitializeAsync();

        Assert.True(vm.WorkspaceVm.TrayPreviewWorkspace.IsTrayDependencyCacheReady || !string.IsNullOrWhiteSpace(vm.ModsPath));
    }

    [Fact]
    public async Task ResetDebugConfigCommand_IsDisabled_WhenNoDebugTogglesAreDefined()
    {
        var vm = CreateShellViewModel(initialSettings: new AppSettings());

        await vm.InitializeAsync();

        Assert.Empty(vm.DebugConfigItems);
        Assert.False(vm.ResetDebugConfigCommand.CanExecute(null));
    }

    [Fact]
    public void RequestedTheme_UsesThemeServiceForManualChanges()
    {
        var themeService = new FakeAppThemeService();
        var vm = CreateShellViewModel(themeService: themeService);

        vm.RequestedTheme = "Light";

        Assert.Equal("Light", themeService.LastAppliedTheme);
        Assert.Equal(1, themeService.ApplyCallCount);
    }

    [Fact]
    public void ModsPath_SyncsIntoModPreviewWorkspace()
    {
        var vm = CreateShellViewModel();
        using var modsRoot = new TempDirectory();

        vm.ModsPath = modsRoot.Path;

        Assert.Equal(modsRoot.Path, vm.WorkspaceVm.ModPreview.ModsRoot);
    }

    private static MainShellViewModel CreateShellViewModel(
        FakeAppCacheMaintenanceService? cacheService = null,
        FakePreviewQueryService? trayPreviewCoordinator = null,
        FakeTrayThumbnailService? trayThumbnailService = null,
        AppSettings? initialSettings = null,
        FakeAppThemeService? themeService = null,
        IReadOnlyDictionary<string, string>? initialDebugConfigEntries = null,
        ILoggerFactory? loggerFactory = null)
    {
        var settingsStore = new FakeSettingsStore(initialSettings ?? new AppSettings());
        var debugConfigStore = new FakeDebugConfigStore(initialDebugConfigEntries);
        themeService ??= new FakeAppThemeService();
        trayPreviewCoordinator ??= new FakePreviewQueryService();
        trayThumbnailService ??= new FakeTrayThumbnailService();
        var workspaceVm = CreateWorkspaceViewModel(settingsStore, trayPreviewCoordinator, trayThumbnailService, loggerFactory);
        var fileDialog = new FakeFileDialogService();
        var navigation = new NavigationService();
        var savesVm = new SaveWorkspaceViewModel(
            new FakeSaveHouseholdCoordinator(),
            fileDialog,
            new TrayDependenciesLauncher(workspaceVm, workspaceVm.TrayDependencies, navigation),
            new FakePreviewQueryService(),
            trayThumbnailService);
        var settingsController = new ShellSettingsController(
            workspaceVm,
            savesVm,
            fileDialog,
            settingsStore,
            debugConfigStore,
            themeService,
            new FakePathDiscoveryService(),
            logger: loggerFactory?.CreateLogger<ShellSettingsController>());
        var systemOperationsController = new ShellSystemOperationsController(
            workspaceVm,
            savesVm,
            new FakeGameLaunchService(),
            cacheService ?? new FakeAppCacheMaintenanceService(),
            loggerFactory?.CreateLogger<ShellSystemOperationsController>());
        return new MainShellViewModel(
            workspaceVm,
            savesVm,
            navigation,
            settingsController,
            systemOperationsController,
            loggerFactory?.CreateLogger<MainShellViewModel>() ?? NullLogger<MainShellViewModel>.Instance,
            null);
    }

    private static MainWindowViewModel CreateWorkspaceViewModel(
        ISettingsStore settingsStore,
        FakePreviewQueryService trayPreviewCoordinator,
        FakeTrayThumbnailService trayThumbnailService,
        ILoggerFactory? loggerFactory = null)
    {
        var organize = new OrganizePanelViewModel();
        var textureCompress = new TextureCompressPanelViewModel();
        var flatten = new FlattenPanelViewModel();
        var normalize = new NormalizePanelViewModel();
        var merge = new MergePanelViewModel();
        var findDup = new FindDupPanelViewModel();
        var trayDependencies = new TrayDependenciesPanelViewModel();
        var modPreview = new ModPreviewPanelViewModel();
        var trayPreview = new TrayPreviewPanelViewModel();
        var sharedFileOps = new SharedFileOpsPanelViewModel();
        var cacheWarmupController = new MainWindowCacheWarmupController(
            new NoOpModPackageInventoryService(),
            new NoOpModItemIndexScheduler(),
            new FakePackageIndexCache(),
            NullLogger<MainWindowCacheWarmupController>.Instance);
        var trayWarmupService = new TrayWarmupService(cacheWarmupController);
        var modsWarmupService = new ModsWarmupService(cacheWarmupController);
        var trayPreviewWorkspace = new TrayPreviewWorkspaceViewModel(
            trayPreview,
            trayPreviewCoordinator,
            trayThumbnailService,
            new FakeFileDialogService(),
            new FakeTrayDependencyExportService(),
            trayWarmupService,
            trayDependencies);
        var modPreviewWorkspace = new ModPreviewWorkspaceViewModel(
            modPreview,
            new NoOpModItemCatalogService(),
            new NoOpModItemIndexScheduler(),
            modsWarmupService,
            new NoOpModItemInspectService(),
            NullModPackageTextureEditService.Instance,
            new FakeFileDialogService());
        var toolkitActionPlanner = new ToolkitActionPlanner(
            organize,
            textureCompress,
            flatten,
            normalize,
            merge,
            findDup,
            trayDependencies,
            trayPreview);
        var settingsPersistenceController = new MainWindowSettingsPersistenceController(settingsStore);
        var settingsProjection = new MainWindowSettingsProjection();
        var recoveryController = new MainWindowRecoveryController();
        var trayPreviewStateController = new MainWindowTrayPreviewStateController();
        var trayPreviewSelectionController = new MainWindowTrayPreviewSelectionController();
        var trayExportService = new FakeTrayDependencyExportService();

        return new MainWindowViewModel(
            new FakeFileDialogService(),
            new FakeConfirmationDialogService(),
            new JsonLocalizationService(),
            toolkitActionPlanner,
            new MainWindowExecutionController(
                new FakeExecutionCoordinator(),
                new FakeTrayDependencyAnalysisService(),
                toolkitActionPlanner,
                recoveryController,
                trayWarmupService,
                CreateTextureCompressionService(),
                new TextureDimensionProbe(),
                loggerFactory?.CreateLogger<MainWindowExecutionController>() ?? NullLogger<MainWindowExecutionController>.Instance),
            new MainWindowStatusController(),
            recoveryController,
            new MainWindowTrayPreviewController(
                trayPreviewCoordinator,
                trayThumbnailService,
                toolkitActionPlanner,
                recoveryController,
                trayPreviewStateController,
                trayPreviewSelectionController,
                loggerFactory?.CreateLogger<MainWindowTrayPreviewController>() ?? NullLogger<MainWindowTrayPreviewController>.Instance),
            new MainWindowTrayExportController(
                trayExportService,
                trayWarmupService,
                loggerFactory?.CreateLogger<MainWindowTrayExportController>() ?? NullLogger<MainWindowTrayExportController>.Instance),
            new MainWindowValidationController(toolkitActionPlanner),
            new MainWindowLifecycleController(
                settingsPersistenceController,
                settingsProjection,
                recoveryController,
                toolkitActionPlanner,
                loggerFactory?.CreateLogger<MainWindowLifecycleController>() ?? NullLogger<MainWindowLifecycleController>.Instance),
            trayPreviewStateController,
            trayPreviewSelectionController,
            modPreviewWorkspace,
            trayPreviewWorkspace,
            organize,
            textureCompress,
            flatten,
            normalize,
            merge,
            findDup,
            trayDependencies: trayDependencies,
            modPreview: modPreview,
            trayPreview: trayPreview,
            sharedFileOps: sharedFileOps,
            logger: loggerFactory?.CreateLogger<MainWindowViewModel>());
    }

    private static ITextureCompressionService CreateTextureCompressionService()
    {
        var decodeService = new CompositeTextureDecodeService(new ImageSharpPngDecoder(), new PfimDdsDecoder());
        var pipeline = new TextureTranscodePipeline(
            decodeService,
            new ImageSharpResizeService(),
            new BcnTextureEncodeService());
        return new TextureCompressionService(decodeService, pipeline);
    }

    private sealed class FakeExecutionCoordinator : IExecutionCoordinator
    {
        public Task<SimsExecutionResult> ExecuteAsync(
            ISimsExecutionInput input,
            Action<string> onOutput,
            Action<SimsProgressUpdate>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SimsExecutionResult
            {
                ExitCode = 0,
                Executable = "pwsh",
                Arguments = Array.Empty<string>()
            });
        }
    }

    private sealed class FakePreviewQueryService : IPreviewQueryService
    {
        public int LoadCount { get; private set; }
        public int InvalidateCount { get; private set; }

        public bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result)
        {
            result = null!;
            return false;
        }

        public Task<TrayPreviewLoadResult> LoadAsync(TrayPreviewInput input, CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return Task.FromResult(new TrayPreviewLoadResult
            {
                Summary = new SimsTrayPreviewSummary(),
                Page = new SimsTrayPreviewPage
                {
                    PageIndex = 1,
                    PageSize = 50,
                    TotalItems = 0,
                    TotalPages = 1,
                    Items = Array.Empty<SimsTrayPreviewItem>()
                },
                LoadedPageCount = 1
            });
        }

        public Task<TrayPreviewPageResult> LoadPageAsync(
            TrayPreviewInput input,
            int requestedPageIndex,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayPreviewPageResult
            {
                Page = new SimsTrayPreviewPage
                {
                    PageIndex = 1,
                    PageSize = 50,
                    TotalItems = 0,
                    TotalPages = 1,
                    Items = Array.Empty<SimsTrayPreviewItem>()
                },
                LoadedPageCount = 1,
                FromCache = false
            });
        }

        public void Invalidate(PreviewSourceRef? source = null)
        {
            InvalidateCount++;
        }

        public void Reset()
        {
        }
    }

    private sealed class FakeTrayDependencyAnalysisService : ITrayDependencyAnalysisService
    {
        public Task<TrayDependencyAnalysisResult> AnalyzeAsync(
            TrayDependencyAnalysisRequest request,
            IProgress<TrayDependencyAnalysisProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayDependencyAnalysisResult
            {
                Success = true
            });
        }
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public Task<IReadOnlyList<string>> PickFolderPathsAsync(string title, bool allowMultiple)
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        public Task<string?> PickFilePathAsync(string title, string fileTypeName, IReadOnlyList<string> patterns)
        {
            return Task.FromResult<string?>(null);
        }

        public Task<string?> PickCsvSavePathAsync(string title, string suggestedFileName)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class FakeTrayDependencyExportService : ITrayDependencyExportService
    {
        public Task<TrayDependencyExportResult> ExportAsync(
            TrayDependencyExportRequest request,
            IProgress<TrayDependencyExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayDependencyExportResult
            {
                Success = true
            });
        }
    }

    private sealed class FakeConfirmationDialogService : IConfirmationDialogService
    {
        public Task<bool> ConfirmAsync(ConfirmationRequest request)
        {
            return Task.FromResult(true);
        }
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private readonly AppSettings _settings;

        public FakeSettingsStore(AppSettings settings)
        {
            _settings = settings;
        }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_settings);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDebugConfigStore : IDebugConfigStore
    {
        private readonly Dictionary<string, string> _entries;

        public FakeDebugConfigStore(IReadOnlyDictionary<string, string>? initialEntries = null)
        {
            _entries = initialEntries is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(initialEntries, StringComparer.OrdinalIgnoreCase);
        }

        public Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(_entries, StringComparer.OrdinalIgnoreCase));
        }

        public Task SaveAsync(IReadOnlyDictionary<string, string> entries, CancellationToken cancellationToken = default)
        {
            _entries.Clear();
            foreach (var entry in entries)
            {
                _entries[entry.Key] = entry.Value;
            }

            return Task.CompletedTask;
        }

        public Task EnsureTemplateAsync(
            IReadOnlyList<DebugConfigTemplateEntry> entries,
            CancellationToken cancellationToken = default)
        {
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                if (!_entries.ContainsKey(entry.Key))
                {
                    _entries[entry.Key] = entry.DefaultValue;
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class NoOpModItemCatalogService : IModItemCatalogService
    {
        public Task<ModItemCatalogPage> QueryPageAsync(ModItemCatalogQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new ModItemCatalogPage
            {
                Items = Array.Empty<ModItemListRow>(),
                TotalItems = 0,
                PageIndex = 1,
                PageSize = 50,
                TotalPages = 0
            });
    }

    private sealed class NoOpModItemIndexScheduler : IModItemIndexScheduler
    {
        public event EventHandler<ModFastBatchAppliedEventArgs>? FastBatchApplied
        {
            add { }
            remove { }
        }
        public event EventHandler<ModEnrichmentAppliedEventArgs>? EnrichmentApplied
        {
            add { }
            remove { }
        }
        public event EventHandler? AllWorkCompleted
        {
            add { }
            remove { }
        }
        public bool IsFastPassRunning => false;
        public bool IsDeepPassRunning => false;

        public Task QueueRefreshAsync(
            ModIndexRefreshRequest request,
            IProgress<ModIndexRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoOpModPackageScanService : IModPackageScanService
    {
        public Task<IReadOnlyList<ModPackageScanResult>> ScanAsync(string modsRoot, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModPackageScanResult>>(Array.Empty<ModPackageScanResult>());
    }

    private sealed class NoOpModPackageInventoryService : IModPackageInventoryService
    {
        public Task<ModPackageInventoryRefreshResult> RefreshAsync(
            string modsRoot,
            IProgress<ModPackageInventoryRefreshProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ModPackageInventoryRefreshResult
            {
                Snapshot = new ModPackageInventorySnapshot
                {
                    ModsRootPath = modsRoot,
                    InventoryVersion = 1,
                    Entries = Array.Empty<ModPackageInventoryEntry>(),
                    LastValidatedUtcTicks = DateTime.UtcNow.Ticks
                }
            });
        }
    }

    private sealed class NoOpModItemInspectService : IModItemInspectService
    {
        public Task<ModItemInspectDetail?> TryGetAsync(string itemKey, CancellationToken cancellationToken = default)
            => Task.FromResult<ModItemInspectDetail?>(null);
    }

    private sealed class FakeAppThemeService : IAppThemeService
    {
        public int ApplyCallCount { get; private set; }
        public string? LastAppliedTheme { get; private set; }

        public string Normalize(string? requestedTheme)
        {
            return string.Equals(requestedTheme, "Light", StringComparison.OrdinalIgnoreCase)
                ? "Light"
                : "Dark";
        }

        public Task<string> LoadRequestedThemeAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult("Dark");
        }

        public void Apply(string? requestedTheme)
        {
            ApplyCallCount++;
            LastAppliedTheme = Normalize(requestedTheme);
        }
    }

    private sealed class FakePathDiscoveryService : ITS4PathDiscoveryService
    {
        public TS4PathDiscoveryResult Discover()
        {
            return new TS4PathDiscoveryResult();
        }
    }

    private sealed class FakeGameLaunchService : IGameLaunchService
    {
        public LaunchGameResult Launch(LaunchGameRequest request)
        {
            return new LaunchGameResult
            {
                Success = true,
                Message = "ok"
            };
        }
    }

    private sealed class FakeSaveHouseholdCoordinator : ISaveHouseholdCoordinator
    {
        public IReadOnlyList<SaveFileEntry> GetSaveFiles(string savesRootPath)
        {
            return Array.Empty<SaveFileEntry>();
        }

        public bool TryLoadHouseholds(string saveFilePath, out SaveHouseholdSnapshot? snapshot, out string error)
        {
            snapshot = null;
            error = string.Empty;
            return false;
        }

        public bool TryGetPreviewDescriptor(string saveFilePath, out SavePreviewDescriptorManifest manifest)
        {
            manifest = null!;
            return false;
        }

        public bool IsPreviewDescriptorCurrent(string saveFilePath, SavePreviewDescriptorManifest manifest)
        {
            return false;
        }

        public PreviewSourceRef GetPreviewSource(string saveFilePath)
        {
            return PreviewSourceRef.ForSaveDescriptor(saveFilePath);
        }

        public Task<SavePreviewDescriptorBuildResult> BuildPreviewDescriptorAsync(
            string saveFilePath,
            IProgress<SavePreviewDescriptorBuildProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SavePreviewDescriptorBuildResult
            {
                Succeeded = true
            });
        }

        public Task<string?> EnsurePreviewArtifactAsync(
            string saveFilePath,
            string householdKey,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public void ClearPreviewData(string saveFilePath)
        {
        }

        public SaveHouseholdExportResult Export(SaveHouseholdExportRequest request)
        {
            return new SaveHouseholdExportResult
            {
                Succeeded = false,
                Error = "not implemented for tests"
            };
        }
    }

    private sealed class FakeTrayThumbnailService : ITrayThumbnailService
    {
        public int ResetCount { get; private set; }

        public Task<TrayThumbnailResult> GetThumbnailAsync(SimsTrayPreviewItem item, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayThumbnailResult
            {
                SourceKind = TrayThumbnailSourceKind.Placeholder,
                Success = false
            });
        }

        public Task CleanupStaleEntriesAsync(string trayRootPath, IReadOnlyCollection<string> liveItemKeys, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void ResetMemoryCache(string? trayRootPath = null)
        {
            ResetCount++;
        }
    }

    private sealed class FakeAppCacheMaintenanceService : IAppCacheMaintenanceService
    {
        public int CallCount { get; private set; }
        public AppCacheMaintenanceResult NextResult { get; set; } = new()
        {
            Success = true,
            Message = "No disk cache folders were present. Restart the app to drop in-memory caches."
        };

        public Task<AppCacheMaintenanceResult> ClearAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(NextResult);
        }

        public Task<AppCacheMaintenanceResult> MaintainAsync(
            AppCacheMaintenanceMode mode = AppCacheMaintenanceMode.Light,
            bool includePackageIndexCache = true,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AppCacheMaintenanceResult
            {
                Success = true,
                MaintenanceMode = mode,
                MaintainedDatabaseCount = includePackageIndexCache ? 2 : 1,
                Message = "Maintained cache databases."
            });
        }
    }

    private sealed class FakePackageIndexCache : IPackageIndexCache
    {
        public Task<PackageIndexSnapshot?> TryLoadSnapshotAsync(
            string modsRootPath,
            long inventoryVersion,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PackageIndexSnapshot?>(new PackageIndexSnapshot
            {
                ModsRootPath = string.IsNullOrWhiteSpace(modsRootPath)
                    ? string.Empty
                    : Path.GetFullPath(modsRootPath.Trim()),
                InventoryVersion = inventoryVersion <= 0 ? 1 : inventoryVersion,
                Packages = Array.Empty<IndexedPackageFile>()
            });
        }

        public Task<PackageIndexSnapshot> BuildSnapshotAsync(
            PackageIndexBuildRequest request,
            IProgress<TrayDependencyExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PackageIndexSnapshot
            {
                ModsRootPath = request.ModsRootPath,
                InventoryVersion = request.InventoryVersion,
                Packages = Array.Empty<IndexedPackageFile>()
            });
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 1000)
    {
        var startedAt = DateTime.UtcNow;
        while (!condition())
        {
            Dispatcher.UIThread.RunJobs(null);
            if ((DateTime.UtcNow - startedAt).TotalMilliseconds > timeoutMs)
            {
                break;
            }

            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sims-shell-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile(string extension)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.{extension}");
            File.WriteAllText(Path, "binary-placeholder");
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
