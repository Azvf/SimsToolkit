using System.Reflection;
using System.Text.Json.Nodes;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Presets;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.Models;
using SimsModDesktop.ViewModels;

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
        Assert.Contains("预检通过", vm.ValidationSummaryText, StringComparison.Ordinal);
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
        vm.IsTrayPreviewAdvancedOpen = true;

        await vm.PersistSettingsAsync();

        Assert.NotNull(settingsStore.LastSaved);
        Assert.True(settingsStore.LastSaved!.UiState.ToolkitLogDrawerOpen);
        Assert.True(settingsStore.LastSaved.UiState.TrayPreviewLogDrawerOpen);
        Assert.True(settingsStore.LastSaved.UiState.ToolkitAdvancedOpen);
        Assert.True(settingsStore.LastSaved.UiState.TrayPreviewAdvancedOpen);
    }

    [Fact]
    public async Task QuickPresetAutoRun_DangerPath_StillRequiresConfirmation()
    {
        var execution = new FakeExecutionCoordinator();
        var confirmation = new FakeConfirmationDialogService { NextResult = false };
        var vm = CreateViewModel(execution, confirmation);
        await vm.InitializeAsync();

        using var script = new TempFile("ps1");
        vm.ScriptPath = script.Path;

        var preset = new QuickPresetDefinition
        {
            Id = "finddup-cleanup",
            Name = "FindDup Cleanup",
            Action = SimsAction.FindDuplicates,
            AutoRun = true,
            ActionPatch = new JsonObject
            {
                ["cleanup"] = true,
                ["rootPath"] = "C:\\Mods"
            }
        };

        await InvokePrivateAsync(vm, "RunQuickPresetAsync", preset);

        Assert.Equal(1, confirmation.CallCount);
        Assert.Equal(0, execution.ExecuteCount);
    }

    private static MainWindowViewModel CreateViewModel(
        FakeExecutionCoordinator execution,
        FakeConfirmationDialogService confirmation,
        FakeSettingsStore? settingsStore = null)
    {
        return new MainWindowViewModel(
            execution,
            new FakeTrayPreviewCoordinator(),
            new FakeFileDialogService(),
            confirmation,
            settingsStore ?? new FakeSettingsStore(new AppSettings()),
            new FakeQuickPresetCatalog());
    }

    private static async Task InvokePrivateAsync(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = method!.Invoke(target, args) as Task;
        Assert.NotNull(task);
        await task!;
    }

    private static void InvokePrivateVoid(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(target, null);
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
                Dashboard = new SimsTrayPreviewDashboard(),
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
        private readonly AppSettings _settings;

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
            return Task.CompletedTask;
        }
    }

    private sealed class FakeQuickPresetCatalog : IQuickPresetCatalog
    {
        public IReadOnlyList<QuickPresetDefinition> GetAll() => Array.Empty<QuickPresetDefinition>();
        public IReadOnlyList<string> LastWarnings => Array.Empty<string>();
        public string UserPresetDirectory => Path.GetTempPath();
        public string UserPresetPath => Path.Combine(Path.GetTempPath(), "quick-presets.json");
        public Task ReloadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
}
