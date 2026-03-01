using System.Reflection;
using Avalonia.Threading;
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

namespace SimsModDesktop.Tests;

public sealed class MainWindowViewModelInteractionTests
{
    [Fact]
    public async Task DangerGuard_RejectConfirmation_DoesNotExecute()
    {
        var execution = new FakeExecutionCoordinator();
        var confirmation = new FakeConfirmationDialogService { NextResult = false };
        var vm = CreateViewModel(execution, confirmation);
        await vm.InitializeAsync();

        using var script = new TempFile("ps1");
        vm.ScriptPath = script.Path;
        vm.SelectedAction = SimsAction.FindDuplicates;
        vm.FindDup.Cleanup = true;
        vm.WhatIf = false;

        await InvokePrivateAsync(vm, "RunToolkitAsync");

        Assert.Equal(0, execution.ExecuteCount);
        Assert.Equal(1, confirmation.CallCount);
    }

    [Fact]
    public async Task DangerGuard_AcceptConfirmation_Executes()
    {
        var execution = new FakeExecutionCoordinator();
        var confirmation = new FakeConfirmationDialogService { NextResult = true };
        var vm = CreateViewModel(execution, confirmation);
        await vm.InitializeAsync();

        using var script = new TempFile("ps1");
        vm.ScriptPath = script.Path;
        vm.SelectedAction = SimsAction.FindDuplicates;
        vm.FindDup.Cleanup = true;
        vm.WhatIf = false;

        await InvokePrivateAsync(vm, "RunToolkitAsync");

        Assert.Equal(1, execution.ExecuteCount);
        Assert.Equal(1, confirmation.CallCount);
    }

    [Fact]
    public async Task DangerGuard_WhatIfEnabled_SkipsConfirmation()
    {
        var execution = new FakeExecutionCoordinator();
        var confirmation = new FakeConfirmationDialogService { NextResult = false };
        var vm = CreateViewModel(execution, confirmation);
        await vm.InitializeAsync();

        using var script = new TempFile("ps1");
        vm.ScriptPath = script.Path;
        vm.SelectedAction = SimsAction.FindDuplicates;
        vm.FindDup.Cleanup = true;
        vm.WhatIf = true;

        await InvokePrivateAsync(vm, "RunToolkitAsync");

        Assert.Equal(1, execution.ExecuteCount);
        Assert.Equal(0, confirmation.CallCount);
    }

    [Fact]
    public async Task ValidationState_UpdatesWithScriptPath()
    {
        var vm = CreateViewModel(new FakeExecutionCoordinator(), new FakeConfirmationDialogService());
        await vm.InitializeAsync();

        vm.Workspace = AppWorkspace.Toolkit;
        vm.ScriptPath = string.Empty;
        InvokePrivateVoid(vm, "RefreshValidationNow");

        Assert.True(vm.HasValidationErrors);
        Assert.Contains("Script path is required", vm.ValidationSummaryText, StringComparison.OrdinalIgnoreCase);

        using var script = new TempFile("ps1");
        vm.ScriptPath = script.Path;
        InvokePrivateVoid(vm, "RefreshValidationNow");

        Assert.False(vm.HasValidationErrors);
        Assert.Contains("Validation passed", vm.ValidationSummaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PersistSettings_RoundTripsUiState()
    {
        var settingsStore = new FakeSettingsStore(new AppSettings());
        var vm = CreateViewModel(
            new FakeExecutionCoordinator(),
            new FakeConfirmationDialogService(),
            settingsStore: settingsStore);

        await vm.InitializeAsync();
        vm.IsToolkitLogDrawerOpen = true;
        vm.IsTrayPreviewLogDrawerOpen = true;
        vm.IsToolkitAdvancedOpen = true;
        var preferredLanguage = vm.AvailableLanguages
            .Select(option => option.Code)
            .FirstOrDefault(code => !string.Equals(code, "en-US", StringComparison.OrdinalIgnoreCase))
            ?? "en-US";
        vm.SelectedLanguageCode = preferredLanguage;

        await vm.PersistSettingsAsync();

        Assert.NotNull(settingsStore.LastSaved);
        Assert.True(settingsStore.LastSaved!.UiState.ToolkitLogDrawerOpen);
        Assert.True(settingsStore.LastSaved.UiState.TrayPreviewLogDrawerOpen);
        Assert.True(settingsStore.LastSaved.UiState.ToolkitAdvancedOpen);
        Assert.Equal(vm.SelectedLanguageCode, settingsStore.LastSaved.UiLanguageCode);
    }

    [Fact]
    public async Task TrayPreviewLayoutMode_PersistsWithoutExplicitWindowClose()
    {
        var settingsStore = new FakeSettingsStore(new AppSettings
        {
            Navigation = new AppSettings.NavigationSettings
            {
                SelectedSection = AppSection.Settings,
                SelectedModuleKey = "traypreview"
            },
            Theme = new AppSettings.ThemeSettings
            {
                RequestedTheme = "Light"
            }
        });
        var vm = CreateViewModel(
            new FakeExecutionCoordinator(),
            new FakeConfirmationDialogService(),
            settingsStore: settingsStore);

        await vm.InitializeAsync();
        vm.TrayPreview.LayoutMode = "Grid";

        await WaitForAsync(() => settingsStore.LastSaved is not null);

        Assert.NotNull(settingsStore.LastSaved);
        Assert.Equal("Grid", settingsStore.LastSaved!.TrayPreview.LayoutMode);
        Assert.Equal(AppSection.Settings, settingsStore.LastSaved.Navigation.SelectedSection);
        Assert.Equal("traypreview", settingsStore.LastSaved.Navigation.SelectedModuleKey);
        Assert.Equal("Light", settingsStore.LastSaved.Theme.RequestedTheme);
    }

    [Fact]
    public async Task AvailableLanguages_ContainsDefaultLanguage()
    {
        var vm = CreateViewModel(new FakeExecutionCoordinator(), new FakeConfirmationDialogService());
        await vm.InitializeAsync();

        Assert.NotEmpty(vm.AvailableLanguages);
        Assert.Contains(vm.AvailableLanguages, option =>
            string.Equals(option.Code, "en-US", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BrowseFolder_UnknownTarget_ReportsStatusAndLog()
    {
        var vm = CreateViewModel(new FakeExecutionCoordinator(), new FakeConfirmationDialogService());
        await vm.InitializeAsync();

        await InvokePrivateAsync(vm, "BrowseFolderAsync", "UnknownFolderTarget");

        Assert.Contains("Unsupported folder browse target", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("UnknownFolderTarget", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("[ui] unsupported folder browse target: UnknownFolderTarget", vm.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrowseCsv_UnknownTarget_ReportsStatusAndLog()
    {
        var vm = CreateViewModel(new FakeExecutionCoordinator(), new FakeConfirmationDialogService());
        await vm.InitializeAsync();

        await InvokePrivateAsync(vm, "BrowseCsvPathAsync", "UnknownCsvTarget");

        Assert.Contains("Unsupported csv browse target", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("UnknownCsvTarget", vm.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("[ui] unsupported csv browse target: UnknownCsvTarget", vm.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrayPreviewEmptyState_PathMissing_ShowsMissingStatus()
    {
        var vm = CreateViewModel(new FakeExecutionCoordinator(), new FakeConfirmationDialogService());
        await vm.InitializeAsync();

        vm.Workspace = AppWorkspace.TrayPreview;
        vm.TrayPreview.TrayRoot = string.Empty;

        Assert.True(vm.IsTrayPreviewEmptyStateVisible);
        Assert.True(vm.IsTrayPreviewPathMissing);
        Assert.True(vm.IsTrayPreviewEmptyStatusMissing);
        Assert.False(vm.HasTrayPreviewItems);
        Assert.False(vm.IsTrayPreviewPagerVisible);
    }

    [Fact]
    public async Task TrayPreviewEmptyState_ValidPathButNoItems_ShowsNoResultsStatus()
    {
        var vm = CreateViewModel(new FakeExecutionCoordinator(), new FakeConfirmationDialogService());
        await vm.InitializeAsync();

        using var trayRoot = new TempDirectory();
        vm.Workspace = AppWorkspace.TrayPreview;
        vm.TrayPreview.TrayRoot = trayRoot.Path;

        await InvokePrivateAsync(
            vm,
            "RunTrayPreviewAsync",
            new TrayPreviewInput
            {
                TrayPath = trayRoot.Path
            });

        Assert.True(vm.IsTrayPreviewEmptyStateVisible);
        Assert.False(vm.IsTrayPreviewPathMissing);
        Assert.True(vm.IsTrayPreviewEmptyStatusWarning);
        Assert.False(vm.HasTrayPreviewItems);
        Assert.False(vm.IsTrayPreviewPagerVisible);
    }

    [Fact]
    public async Task TrayPreviewThumbnailFailure_StopsLoadingAndShowsErrorState()
    {
        var thumbnailService = new FailingTrayThumbnailService();
        var vm = CreateViewModel(
            new FakeExecutionCoordinator(),
            new FakeConfirmationDialogService(),
            trayThumbnailService: thumbnailService);

        await vm.InitializeAsync();

        using var trayRoot = new TempDirectory();
        InvokePrivateVoid(
            vm,
            "SetTrayPreviewPage",
            new SimsTrayPreviewPage
            {
                PageIndex = 1,
                PageSize = 50,
                TotalItems = 1,
                TotalPages = 1,
                Items =
                [
                    new SimsTrayPreviewItem
                    {
                        TrayItemKey = "0x0000000000000042",
                        PresetType = "Household",
                        TrayRootPath = trayRoot.Path
                    }
                ]
            },
            1);

        await WaitForAsync(() => vm.PreviewItems.Count == 1 && !vm.PreviewItems[0].IsThumbnailLoading);

        Assert.Equal(1, thumbnailService.CallCount);
        Assert.False(vm.PreviewItems[0].HasThumbnail);
        Assert.True(vm.PreviewItems[0].HasThumbnailError);
        Assert.False(vm.PreviewItems[0].IsThumbnailLoading);
    }

    private static MainWindowViewModel CreateViewModel(
        FakeExecutionCoordinator execution,
        FakeConfirmationDialogService confirmation,
        FakeSettingsStore? settingsStore = null,
        ITrayThumbnailService? trayThumbnailService = null)
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
        var trayPreviewCoordinator = new FakeTrayPreviewCoordinator();

        return new MainWindowViewModel(
            new ToolkitExecutionRunner(execution),
            new TrayPreviewRunner(trayPreviewCoordinator),
            new FakeFileDialogService(),
            confirmation,
            new JsonLocalizationService(),
            settingsStore ?? new FakeSettingsStore(new AppSettings()),
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
            sharedFileOps,
            trayThumbnailService);
    }

    private static async Task InvokePrivateAsync(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = method!.Invoke(target, args) as Task;
        Assert.NotNull(task);
        await task!;
    }

    private static void InvokePrivateVoid(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(target, args);
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

        Dispatcher.UIThread.RunJobs(null);
        Assert.True(condition());
    }

    private sealed class FakeExecutionCoordinator : IExecutionCoordinator
    {
        public int ExecuteCount { get; private set; }

        public Task<SimsExecutionResult> ExecuteAsync(ISimsExecutionInput input, Action<string> onOutput, Action<SimsProgressUpdate>? onProgress = null, CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
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
        public bool NextResult { get; set; } = true;
        public int CallCount { get; private set; }

        public Task<bool> ConfirmAsync(ConfirmationRequest request)
        {
            CallCount++;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        private AppSettings _settings;

        public FakeSettingsStore(AppSettings settings)
        {
            _settings = settings;
        }

        public AppSettings? LastSaved { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_settings);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            LastSaved = settings;
            _settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingTrayThumbnailService : ITrayThumbnailService
    {
        public int CallCount { get; private set; }

        public Task<TrayThumbnailResult> GetThumbnailAsync(
            SimsTrayPreviewItem item,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new TrayThumbnailResult
            {
                SourceKind = TrayThumbnailSourceKind.Placeholder,
                Success = false
            });
        }

        public Task CleanupStaleEntriesAsync(
            string trayRootPath,
            IReadOnlyCollection<string> liveItemKeys,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TempFile : IDisposable
    {
        public TempFile(string extension)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.{extension}");
            File.WriteAllText(Path, "# temp");
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

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sims-tray-{Guid.NewGuid():N}");
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
}

