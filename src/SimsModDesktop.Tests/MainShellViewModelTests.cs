using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.Infrastructure.Localization;
using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.Models;
using SimsModDesktop.Services;
using SimsModDesktop.ViewModels;
using SimsModDesktop.ViewModels.Panels;
using SimsModDesktop.ViewModels.Shell;

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
        Assert.Equal(AppSection.Mods, vm.SelectedSection);
    }

    private static MainShellViewModel CreateShellViewModel()
    {
        var settingsStore = new FakeSettingsStore(new AppSettings());
        var workspaceVm = CreateWorkspaceViewModel(settingsStore);
        return new MainShellViewModel(
            workspaceVm,
            new NavigationService(),
            new FakeFileDialogService(),
            settingsStore,
            new FakePathDiscoveryService(),
            new FakeGameLaunchService());
    }

    private static MainWindowViewModel CreateWorkspaceViewModel(ISettingsStore settingsStore)
    {
        var organize = new OrganizePanelViewModel();
        var flatten = new FlattenPanelViewModel();
        var normalize = new NormalizePanelViewModel();
        var merge = new MergePanelViewModel();
        var findDup = new FindDupPanelViewModel();
        var trayDependencies = new TrayDependenciesPanelViewModel();
        var trayPreview = new TrayPreviewPanelViewModel();
        var sharedFileOps = new SharedFileOpsPanelViewModel();

        var moduleRegistry = new ActionModuleRegistry(new IActionModule[]
        {
            new OrganizeActionModule(organize),
            new FlattenActionModule(flatten),
            new NormalizeActionModule(normalize),
            new MergeActionModule(merge),
            new FindDupActionModule(findDup),
            new TrayDependenciesActionModule(trayDependencies),
            new TrayPreviewActionModule(trayPreview)
        });

        return new MainWindowViewModel(
            new ToolkitExecutionRunner(new FakeExecutionCoordinator()),
            new TrayPreviewRunner(new FakeTrayPreviewCoordinator()),
            new FakeFileDialogService(),
            new FakeConfirmationDialogService(),
            new JsonLocalizationService(),
            settingsStore,
            new MainWindowSettingsProjection(),
            moduleRegistry,
            new MainWindowPlanBuilder(moduleRegistry),
            organize,
            flatten,
            normalize,
            merge,
            findDup,
            trayDependencies,
            trayPreview,
            sharedFileOps);
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
        public bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result)
        {
            result = null!;
            return false;
        }

        public Task<TrayPreviewLoadResult> LoadAsync(TrayPreviewInput input, CancellationToken cancellationToken = default)
        {
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

        public void Reset()
        {
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
