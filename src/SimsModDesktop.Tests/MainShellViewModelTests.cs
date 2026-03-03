using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.Infrastructure.Localization;
using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.ViewModels;
using SimsModDesktop.ViewModels.Panels;
using SimsModDesktop.ViewModels.Preview;
using SimsModDesktop.ViewModels.Shell;
using SimsModDesktop.ViewModels.Saves;

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
        var trayPreviewCoordinator = new FakeTrayPreviewCoordinator();
        var vm = CreateShellViewModel(cacheService, trayPreviewCoordinator, trayThumbnailService);
        using var trayRoot = new TempDirectory();

        vm.TrayPath = trayRoot.Path;
        await Task.Delay(50);
        var baselineLoadCount = trayPreviewCoordinator.LoadCount;

        vm.ClearCacheCommand.Execute(null);
        await Task.Delay(50);

        Assert.Equal(1, trayPreviewCoordinator.InvalidateCount);
        Assert.Equal(1, trayThumbnailService.ResetCount);

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
        FakeTrayPreviewCoordinator? trayPreviewCoordinator = null,
        FakeTrayThumbnailService? trayThumbnailService = null,
        AppSettings? initialSettings = null,
        FakeAppThemeService? themeService = null)
    {
        var settingsStore = new FakeSettingsStore(initialSettings ?? new AppSettings());
        themeService ??= new FakeAppThemeService();
        trayPreviewCoordinator ??= new FakeTrayPreviewCoordinator();
        trayThumbnailService ??= new FakeTrayThumbnailService();
        var workspaceVm = CreateWorkspaceViewModel(settingsStore, trayPreviewCoordinator, trayThumbnailService);
        var fileDialog = new FakeFileDialogService();
        var navigation = new NavigationService();
        var savesVm = new SaveWorkspaceViewModel(
            new FakeSaveHouseholdCoordinator(),
            fileDialog,
            new TrayDependenciesLauncher(workspaceVm, workspaceVm.TrayDependencies, navigation),
            new TrayPreviewRunner(new FakeTrayPreviewCoordinator()),
            trayThumbnailService);
        return new MainShellViewModel(
            workspaceVm,
            savesVm,
            navigation,
            fileDialog,
            settingsStore,
            themeService,
            new FakePathDiscoveryService(),
            new FakeGameLaunchService(),
            cacheService ?? new FakeAppCacheMaintenanceService());
    }

    private static MainWindowViewModel CreateWorkspaceViewModel(
        ISettingsStore settingsStore,
        FakeTrayPreviewCoordinator trayPreviewCoordinator,
        FakeTrayThumbnailService trayThumbnailService)
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

        var moduleRegistry = new ActionModuleRegistry(new IActionModule[]
        {
            new OrganizeActionModule(organize),
            new TextureCompressActionModule(textureCompress),
            new FlattenActionModule(flatten),
            new NormalizeActionModule(normalize),
            new MergeActionModule(merge),
            new FindDupActionModule(findDup),
            new TrayDependenciesActionModule(trayDependencies),
            new TrayPreviewActionModule(trayPreview)
        });
        var trayPreviewRunner = new TrayPreviewRunner(trayPreviewCoordinator);
        var trayPreviewWorkspace = new TrayPreviewWorkspaceViewModel(
            trayPreview,
            trayPreviewRunner,
            trayThumbnailService,
            new FakeFileDialogService(),
            new FakeTrayDependencyExportService(),
            trayDependencies);
        var modPreviewWorkspace = new ModPreviewWorkspaceViewModel(
            modPreview,
            new ModPreviewCatalogService());

        return new MainWindowViewModel(
            new ToolkitExecutionRunner(new FakeExecutionCoordinator()),
            trayPreviewRunner,
            new FakeTrayDependencyExportService(),
            new FakeTrayDependencyAnalysisService(),
            new FakeFileDialogService(),
            new FakeConfirmationDialogService(),
            new JsonLocalizationService(),
            settingsStore,
            new MainWindowSettingsProjection(),
            moduleRegistry,
            new MainWindowPlanBuilder(moduleRegistry),
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
            trayThumbnailService: trayThumbnailService);
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

    private sealed class FakeTrayPreviewCoordinator : ITrayPreviewCoordinator
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

        public Task<TrayPreviewPageResult> LoadPageAsync(int requestedPageIndex, CancellationToken cancellationToken = default)
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

        public void Invalidate(string? trayRootPath = null)
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

        public bool TryGetPreviewCacheManifest(string saveFilePath, out SavePreviewCacheManifest manifest)
        {
            manifest = null!;
            return false;
        }

        public bool IsPreviewCacheCurrent(string saveFilePath, SavePreviewCacheManifest manifest)
        {
            return false;
        }

        public string GetPreviewCacheRoot(string saveFilePath)
        {
            return string.Empty;
        }

        public Task<SavePreviewCacheBuildResult> BuildPreviewCacheAsync(
            string saveFilePath,
            IProgress<SavePreviewCacheBuildProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SavePreviewCacheBuildResult
            {
                Succeeded = true
            });
        }

        public void ClearPreviewCache(string saveFilePath)
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
