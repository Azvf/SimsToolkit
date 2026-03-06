using System.Reflection;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Application.Models;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Presentation.Dialogs;
using SimsModDesktop.Presentation.Services;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Saves;
using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.Tests;

public sealed class SaveWorkspaceViewModelTests
{
    [Fact]
    public async Task AnalyzeDependenciesAsync_UsesPreviewArtifactProviderResult()
    {
        using var temp = new TempDirectory();
        var saveFilePath = Path.Combine(temp.Path, "slot_00000001.save");
        File.WriteAllText(saveFilePath, "save");
        var artifactRoot = Path.Combine(temp.Path, "preview-root");
        Directory.CreateDirectory(artifactRoot);

        var coordinator = new FakeSaveHouseholdCoordinator
        {
            EnsurePreviewArtifactResult = artifactRoot
        };
        var launcher = new FakeTrayDependenciesLauncher();
        var vm = CreateViewModel(coordinator, launcher);
        SelectSaveAndPreviewItem(vm, saveFilePath, "tray-item-1");

        await InvokeAnalyzeDependenciesAsync(vm);

        Assert.Equal(saveFilePath, coordinator.LastEnsurePreviewArtifactSaveFilePath);
        Assert.Equal("tray-item-1", coordinator.LastEnsurePreviewArtifactHouseholdKey);
        Assert.Equal("tray-dependency-analysis", coordinator.LastEnsurePreviewArtifactPurpose);
        Assert.Equal(artifactRoot, launcher.LastTrayRootPath);
        Assert.Equal("tray-item-1", launcher.LastTrayItemKey);
        Assert.Equal("Started tray dependency analysis for the selected save household.", vm.StatusText);
    }

    [Fact]
    public async Task AnalyzeDependenciesAsync_WhenArtifactProviderDoesNotReturnValidRoot_DoesNotLaunchAnalysis()
    {
        using var temp = new TempDirectory();
        var saveFilePath = Path.Combine(temp.Path, "slot_00000002.save");
        File.WriteAllText(saveFilePath, "save");

        var coordinator = new FakeSaveHouseholdCoordinator
        {
            EnsurePreviewArtifactResult = Path.Combine(temp.Path, "missing-root")
        };
        var launcher = new FakeTrayDependenciesLauncher();
        var vm = CreateViewModel(coordinator, launcher);
        SelectSaveAndPreviewItem(vm, saveFilePath, "tray-item-2");

        await InvokeAnalyzeDependenciesAsync(vm);

        Assert.Null(launcher.LastTrayRootPath);
        Assert.Null(launcher.LastTrayItemKey);
        Assert.Equal("The selected save household is not ready for dependency analysis yet.", vm.StatusText);
    }

    [Fact]
    public async Task SelectedPreviewItem_WhenIdlePrimeEnabled_PrimesArtifactInBackground()
    {
        using var temp = new TempDirectory();
        var saveFilePath = Path.Combine(temp.Path, "slot_00000003.save");
        File.WriteAllText(saveFilePath, "save");

        var coordinator = new FakeSaveHouseholdCoordinator();
        var launcher = new FakeTrayDependenciesLauncher();
        var vm = CreateViewModel(
            coordinator,
            launcher,
            uiActivityMonitor: new FakeUiActivityMonitor(DateTimeOffset.UtcNow - TimeSpan.FromMinutes(1)),
            configurationProvider: new StaticConfigurationProvider(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Performance.IdlePrewarm.SaveArtifactPrimeEnabled"] = true,
                ["Performance.IdlePrewarm.DelayMs"] = 50
            }));

        vm.SelectedSave = new SaveFileEntry
        {
            FilePath = saveFilePath,
            FileName = Path.GetFileName(saveFilePath),
            LastWriteTimeLocal = DateTime.Now,
            LengthBytes = 4
        };

        SetPrivateField(vm, "_isActive", true);
        SetPrivateField(vm, "_currentManifest", new SavePreviewDescriptorManifest
        {
            Entries =
            [
                new SavePreviewDescriptorEntry
                {
                    HouseholdId = 101,
                    TrayItemKey = "tray-item-3",
                    BuildState = "Ready"
                }
            ]
        });
        SetPrivateField(vm, "_currentSnapshot", new SaveHouseholdSnapshot
        {
            SavePath = saveFilePath,
            Households =
            [
                new SaveHouseholdItem
                {
                    HouseholdId = 101,
                    Name = "Idle Prime Household",
                    HouseholdSize = 2,
                    CanExport = true
                }
            ]
        });

        SelectPreviewItem(vm, "tray-item-3");

        await WaitForAsync(() => coordinator.EnsurePreviewArtifactCallCount > 0, timeoutMs: 3000);

        Assert.Equal(saveFilePath, coordinator.LastEnsurePreviewArtifactSaveFilePath);
        Assert.Equal("tray-item-3", coordinator.LastEnsurePreviewArtifactHouseholdKey);
        Assert.Equal("workspace-idle", coordinator.LastEnsurePreviewArtifactPurpose);
    }

    [Fact]
    public async Task ClearSelectedSaveCacheAsync_ClearsPreviewWithoutTriggeringDescriptorError()
    {
        using var temp = new TempDirectory();
        var saveFilePath = Path.Combine(temp.Path, "slot_00000004.save");
        File.WriteAllText(saveFilePath, "save");

        var coordinator = new FakeSaveHouseholdCoordinator();
        var launcher = new FakeTrayDependenciesLauncher();
        var vm = CreateViewModel(coordinator, launcher);
        vm.SelectedSave = new SaveFileEntry
        {
            FilePath = saveFilePath,
            FileName = Path.GetFileName(saveFilePath),
            LastWriteTimeLocal = DateTime.Now,
            LengthBytes = 4
        };

        SetPrivateField(vm, "_currentManifest", new SavePreviewDescriptorManifest
        {
            Entries =
            [
                new SavePreviewDescriptorEntry
                {
                    HouseholdId = 401,
                    TrayItemKey = "tray-item-4",
                    BuildState = "Ready"
                }
            ]
        });

        var method = typeof(SaveWorkspaceViewModel).GetMethod("ClearSelectedSaveCacheAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = method!.Invoke(vm, null) as Task;
        Assert.NotNull(task);
        await task!;

        Assert.Equal(saveFilePath, coordinator.LastClearedPreviewDataSaveFilePath);
        Assert.Equal("Selected save preview cache was cleared.", vm.StatusText);
        Assert.Equal("Preview cache cleared. Rebuild descriptor to reload.", vm.CacheStatusText);
    }

    [Fact]
    public async Task PreviewSelection_DefaultsToFirstReadyHousehold()
    {
        using var temp = new TempDirectory();
        var saveFilePath = Path.Combine(temp.Path, "slot_00000005.save");
        File.WriteAllText(saveFilePath, "save");

        var coordinator = new FakeSaveHouseholdCoordinator();
        var launcher = new FakeTrayDependenciesLauncher();
        var vm = CreateViewModel(coordinator, launcher);
        vm.SelectedSave = new SaveFileEntry
        {
            FilePath = saveFilePath,
            FileName = Path.GetFileName(saveFilePath),
            LastWriteTimeLocal = DateTime.Now,
            LengthBytes = 4
        };

        SetPrivateField(vm, "_currentManifest", new SavePreviewDescriptorManifest
        {
            Entries =
            [
                new SavePreviewDescriptorEntry
                {
                    HouseholdId = 501,
                    TrayItemKey = "blocked-item",
                    BuildState = "Blocked"
                },
                new SavePreviewDescriptorEntry
                {
                    HouseholdId = 502,
                    TrayItemKey = "ready-item",
                    BuildState = "Ready"
                }
            ]
        });

        vm.Surface.PreviewItems.Add(new TrayPreviewListItemViewModel(new SimsTrayPreviewItem
        {
            TrayItemKey = "blocked-item",
            PresetType = "Household"
        }));
        vm.Surface.PreviewItems.Add(new TrayPreviewListItemViewModel(new SimsTrayPreviewItem
        {
            TrayItemKey = "ready-item",
            PresetType = "Household"
        }));

        await WaitForAsync(() => vm.Surface.SelectedPreviewItem is not null, timeoutMs: 3000);

        Assert.Equal("ready-item", vm.Surface.SelectedPreviewItem?.TrayItemKey);
    }

    private static SaveWorkspaceViewModel CreateViewModel(
        FakeSaveHouseholdCoordinator coordinator,
        FakeTrayDependenciesLauncher launcher,
        IUiActivityMonitor? uiActivityMonitor = null,
        IConfigurationProvider? configurationProvider = null)
    {
        return new SaveWorkspaceViewModel(
            coordinator,
            new FakeFileDialogService(),
            launcher,
            new FakeTrayPreviewCoordinator(),
            new FakeTrayThumbnailService(),
            uiActivityMonitor: uiActivityMonitor,
            configurationProvider: configurationProvider);
    }

    private static void SelectSaveAndPreviewItem(
        SaveWorkspaceViewModel vm,
        string saveFilePath,
        string trayItemKey)
    {
        vm.SelectedSave = new SaveFileEntry
        {
            FilePath = saveFilePath,
            FileName = Path.GetFileName(saveFilePath),
            LastWriteTimeLocal = DateTime.Now,
            LengthBytes = 4
        };

        var previewItemVm = new TrayPreviewListItemViewModel(new SimsTrayPreviewItem
        {
            TrayItemKey = trayItemKey,
            PresetType = "Household",
            TrayRootPath = string.Empty,
            SourceFilePaths = Array.Empty<string>()
        });
        vm.Surface.PreviewItems.Add(previewItemVm);
        vm.Surface.ApplySelection(previewItemVm, controlPressed: false, shiftPressed: false);
    }

    private static void SelectPreviewItem(SaveWorkspaceViewModel vm, string trayItemKey)
    {
        var previewItemVm = new TrayPreviewListItemViewModel(new SimsTrayPreviewItem
        {
            TrayItemKey = trayItemKey,
            PresetType = "Household",
            TrayRootPath = string.Empty,
            SourceFilePaths = Array.Empty<string>()
        });
        vm.Surface.PreviewItems.Add(previewItemVm);
        vm.Surface.ApplySelection(previewItemVm, controlPressed: false, shiftPressed: false);
    }

    private static async Task InvokeAnalyzeDependenciesAsync(SaveWorkspaceViewModel vm)
    {
        var method = typeof(SaveWorkspaceViewModel).GetMethod("AnalyzeDependenciesAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = method!.Invoke(vm, null) as Task;
        Assert.NotNull(task);
        await task!;
    }

    private static void SetPrivateField<TValue>(object target, string fieldName, TValue value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs)
    {
        var startedAt = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - startedAt > timeoutMs)
            {
                throw new TimeoutException("Condition was not met before timeout.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class FakeSaveHouseholdCoordinator : ISaveHouseholdCoordinator
    {
        public string? EnsurePreviewArtifactResult { get; set; }

        public int EnsurePreviewArtifactCallCount { get; private set; }

        public string? LastEnsurePreviewArtifactSaveFilePath { get; private set; }

        public string? LastEnsurePreviewArtifactHouseholdKey { get; private set; }

        public string? LastEnsurePreviewArtifactPurpose { get; private set; }

        public string? LastClearedPreviewDataSaveFilePath { get; private set; }

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
            manifest = new SavePreviewDescriptorManifest();
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
            EnsurePreviewArtifactCallCount++;
            LastEnsurePreviewArtifactSaveFilePath = saveFilePath;
            LastEnsurePreviewArtifactHouseholdKey = householdKey;
            LastEnsurePreviewArtifactPurpose = purpose;
            return Task.FromResult(EnsurePreviewArtifactResult);
        }

        public void ClearPreviewData(string saveFilePath)
        {
            LastClearedPreviewDataSaveFilePath = saveFilePath;
        }

        public SaveHouseholdExportResult Export(SaveHouseholdExportRequest request)
        {
            return new SaveHouseholdExportResult
            {
                Succeeded = false
            };
        }
    }

    private sealed class FakeTrayDependenciesLauncher : ITrayDependenciesLauncher
    {
        public string? LastTrayRootPath { get; private set; }

        public string? LastTrayItemKey { get; private set; }

        public Task RunForTrayItemAsync(
            string trayRootPath,
            string trayItemKey,
            CancellationToken cancellationToken = default)
        {
            LastTrayRootPath = trayRootPath;
            LastTrayItemKey = trayItemKey;
            return Task.CompletedTask;
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

    private sealed class FakeUiActivityMonitor : IUiActivityMonitor
    {
        public FakeUiActivityMonitor(DateTimeOffset lastInteractionUtc)
        {
            LastInteractionUtc = lastInteractionUtc;
        }

        public DateTimeOffset LastInteractionUtc { get; private set; }

        public void RecordInteraction()
        {
            LastInteractionUtc = DateTimeOffset.UtcNow;
        }
    }

    private sealed class StaticConfigurationProvider : IConfigurationProvider
    {
        private readonly IReadOnlyDictionary<string, object?> _values;

        public StaticConfigurationProvider(IReadOnlyDictionary<string, object?> values)
        {
            _values = values;
        }

        public Task<T?> GetConfigurationAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (_values.TryGetValue(key, out var value) && value is T typed)
            {
                return Task.FromResult<T?>(typed);
            }

            return Task.FromResult<T?>(default);
        }

        public Task SetConfigurationAsync<T>(string key, T value, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_values.ContainsKey(key));

        public Task<bool> RemoveConfigurationAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<string>> GetAllKeysAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(_values.Keys.ToArray());

        public Task<bool> IsPlatformSpecificAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public string GetPlatformSpecificPrefix()
            => string.Empty;

        public T? GetDefaultValue<T>(string key)
            => default;

        public Task<bool> ResetToDefaultAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, object?>> GetConfigurationsAsync(
            IReadOnlyList<string> keys,
            CancellationToken cancellationToken = default)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                values[key] = _values.TryGetValue(key, out var value) ? value : null;
            }

            return Task.FromResult<IReadOnlyDictionary<string, object?>>(values);
        }

        public Task SetConfigurationsAsync(
            IReadOnlyDictionary<string, object> configurations,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeTrayPreviewCoordinator : ITrayPreviewCoordinator
    {
        public bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result)
        {
            result = new TrayPreviewLoadResult
            {
                Summary = new SimsTrayPreviewSummary(),
                Page = new SimsTrayPreviewPage()
            };
            return false;
        }

        public Task<TrayPreviewLoadResult> LoadAsync(TrayPreviewInput input, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayPreviewLoadResult
            {
                Summary = new SimsTrayPreviewSummary(),
                Page = new SimsTrayPreviewPage()
            });
        }

        public Task<TrayPreviewPageResult> LoadPageAsync(int requestedPageIndex, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayPreviewPageResult
            {
                Page = new SimsTrayPreviewPage()
            });
        }

        public void Invalidate(PreviewSourceRef? source = null)
        {
        }

        public void Reset()
        {
        }
    }

    private sealed class FakeTrayThumbnailService : ITrayThumbnailService
    {
        public Task<TrayThumbnailResult> GetThumbnailAsync(SimsTrayPreviewItem item, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayThumbnailResult());
        }

        public Task CleanupStaleEntriesAsync(
            string trayRootPath,
            IReadOnlyCollection<string> liveItemKeys,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void ResetMemoryCache(string? trayRootPath = null)
        {
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"save-workspace-{Guid.NewGuid():N}");
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
