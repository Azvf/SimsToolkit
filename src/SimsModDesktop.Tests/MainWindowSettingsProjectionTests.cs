using System.Text.Json.Nodes;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Models;

namespace SimsModDesktop.Tests;

public sealed class MainWindowSettingsProjectionTests
{
    [Fact]
    public void Capture_PersistsSnapshotAndDelegatesModuleSave()
    {
        var module = new FakeModule(SimsAction.Organize, "organize");
        module.OnSave = settings => settings.Organize.SourceDir = @"D:\FromModule";
        var registry = new ActionModuleRegistry([module]);

        var projection = new MainWindowSettingsProjection();
        var snapshot = new MainWindowSettingsSnapshot
        {
            ScriptPath = @"C:\tools\sims-mod-cli.ps1",
            Workspace = AppWorkspace.Toolkit,
            SelectedAction = SimsAction.Flatten,
            WhatIf = true,
            SharedFileOps = new AppSettings.SharedFileOpsSettings
            {
                SkipPruneEmptyDirs = true,
                ModFilesOnly = true,
                VerifyContentOnNameConflict = true,
                ModExtensionsText = ".package,.blend",
                PrefixHashBytesText = "2048",
                HashWorkerCountText = "4"
            },
            UiState = new AppSettings.UiStateSettings
            {
                ToolkitLogDrawerOpen = true,
                TrayPreviewLogDrawerOpen = true,
                ToolkitAdvancedOpen = true,
                TrayPreviewAdvancedOpen = false
            }
        };

        var settings = projection.Capture(snapshot, registry);

        Assert.Equal(snapshot.ScriptPath, settings.ScriptPath);
        Assert.Equal(snapshot.Workspace, settings.SelectedWorkspace);
        Assert.Equal(snapshot.SelectedAction, settings.SelectedAction);
        Assert.True(settings.WhatIf);
        Assert.True(settings.SharedFileOps.SkipPruneEmptyDirs);
        Assert.True(settings.SharedFileOps.ModFilesOnly);
        Assert.True(settings.SharedFileOps.VerifyContentOnNameConflict);
        Assert.Equal(".package,.blend", settings.SharedFileOps.ModExtensionsText);
        Assert.Equal("2048", settings.SharedFileOps.PrefixHashBytesText);
        Assert.Equal("4", settings.SharedFileOps.HashWorkerCountText);
        Assert.True(settings.UiState.ToolkitLogDrawerOpen);
        Assert.True(settings.UiState.TrayPreviewLogDrawerOpen);
        Assert.True(settings.UiState.ToolkitAdvancedOpen);
        Assert.False(settings.UiState.TrayPreviewAdvancedOpen);
        Assert.Equal(@"D:\FromModule", settings.Organize.SourceDir);
        Assert.Equal(1, module.SaveCallCount);
    }

    [Fact]
    public void Resolve_TrayPreviewSelection_MapsToTrayWorkspaceAndSafeAction()
    {
        var projection = new MainWindowSettingsProjection();
        var settings = new AppSettings
        {
            ScriptPath = @"C:\tools\sims-mod-cli.ps1",
            SelectedWorkspace = AppWorkspace.Toolkit,
            SelectedAction = SimsAction.TrayPreview,
            WhatIf = true,
            SharedFileOps = new AppSettings.SharedFileOpsSettings
            {
                SkipPruneEmptyDirs = true
            },
            UiState = new AppSettings.UiStateSettings
            {
                ToolkitLogDrawerOpen = true
            }
        };

        var resolved = projection.Resolve(settings, [SimsAction.Organize, SimsAction.Flatten]);

        Assert.Equal(SimsAction.Organize, resolved.SelectedAction);
        Assert.Equal(AppWorkspace.TrayPreview, resolved.Workspace);
        Assert.True(resolved.WhatIf);
        Assert.True(resolved.SharedFileOps.SkipPruneEmptyDirs);
        Assert.True(resolved.UiState.ToolkitLogDrawerOpen);
    }

    [Fact]
    public void LoadModuleSettings_DelegatesToAllModules()
    {
        var moduleA = new FakeModule(SimsAction.Organize, "a");
        var moduleB = new FakeModule(SimsAction.Flatten, "b");
        var registry = new ActionModuleRegistry([moduleA, moduleB]);
        var projection = new MainWindowSettingsProjection();

        projection.LoadModuleSettings(new AppSettings(), registry);

        Assert.Equal(1, moduleA.LoadCallCount);
        Assert.Equal(1, moduleB.LoadCallCount);
    }

    private sealed class FakeModule : IActionModule
    {
        public FakeModule(SimsAction action, string moduleKey)
        {
            Action = action;
            ModuleKey = moduleKey;
        }

        public SimsAction Action { get; }
        public string ModuleKey { get; }
        public string DisplayName => ModuleKey;
        public bool UsesSharedFileOps => false;
        public IReadOnlyCollection<string> SupportedActionPatchKeys => Array.Empty<string>();

        public int SaveCallCount { get; private set; }
        public int LoadCallCount { get; private set; }
        public Action<AppSettings>? OnSave { get; set; }

        public void LoadFromSettings(AppSettings settings)
        {
            LoadCallCount++;
        }

        public void SaveToSettings(AppSettings settings)
        {
            SaveCallCount++;
            OnSave?.Invoke(settings);
        }

        public bool TryBuildPlan(GlobalExecutionOptions options, out ModuleExecutionPlan plan, out string error)
        {
            plan = null!;
            error = "Not implemented for tests.";
            return false;
        }

        public bool TryApplyActionPatch(JsonObject patch, out string error)
        {
            error = string.Empty;
            return true;
        }
    }
}
