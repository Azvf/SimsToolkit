using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;
using System.Text.Json.Nodes;

namespace SimsModDesktop.Tests;

public sealed class ActionModuleRegistryTests
{
    [Fact]
    public void MergeModule_TryBuildPlan_DeduplicatesSourcePaths()
    {
        var panel = new StubMergeModuleState
        {
            TargetPath = @"D:\Mods\Merged"
        };
        panel.ReplaceSourcePaths(
        [
            @"D:\Mods\A",
            @"D:\Mods\A",
            @"D:\Mods\B"
        ]);

        var module = new MergeActionModule(panel);
        var ok = module.TryBuildPlan(
            new GlobalExecutionOptions
            {
                ScriptPath = @"C:\tools\sims-mod-cli.ps1",
                WhatIf = true,
                Shared = new SharedFileOpsInput()
            },
            out var plan,
            out var error);

        Assert.True(ok, error);
        var cliPlan = Assert.IsType<CliExecutionPlan>(plan);
        var input = Assert.IsType<MergeInput>(cliPlan.Input);
        Assert.Equal(2, input.MergeSourcePaths.Count);
        Assert.Contains(@"D:\Mods\A", input.MergeSourcePaths);
        Assert.Contains(@"D:\Mods\B", input.MergeSourcePaths);
    }

    [Fact]
    public void Registry_GetMissingAction_Throws()
    {
        var registry = new ActionModuleRegistry(new IActionModule[]
        {
            new OrganizeActionModule(new StubOrganizeModuleState())
        });

        Assert.Throws<InvalidOperationException>(() => registry.Get(SimsAction.TrayPreview));
    }

    [Fact]
    public void Registry_DuplicateActions_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ActionModuleRegistry(new IActionModule[]
            {
                new FakeModule(SimsAction.Organize, "organize-a"),
                new FakeModule(SimsAction.Organize, "organize-b")
            }));

        Assert.Contains("Duplicate action modules", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_DuplicateModuleKeys_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ActionModuleRegistry(new IActionModule[]
            {
                new FakeModule(SimsAction.Organize, "shared-key"),
                new FakeModule(SimsAction.Flatten, "shared-key")
            }));

        Assert.Contains("Duplicate module keys", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_EmptyModuleKey_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ActionModuleRegistry(new IActionModule[]
            {
                new FakeModule(SimsAction.Organize, string.Empty)
            }));

        Assert.Contains("non-empty ModuleKey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TrayPreviewModule_SupportedPatchKeys_DoNotContainPathOverride()
    {
        var panel = new TrayPreviewPanelState
        {
            TrayRoot = @"D:\Tray"
        };
        var module = new TrayPreviewActionModule(panel);

        Assert.DoesNotContain("trayPath", module.SupportedActionPatchKeys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrayPreviewModule_ApplyPatch_IgnoresTrayPathOverride()
    {
        var panel = new TrayPreviewPanelState
        {
            TrayRoot = @"D:\Tray"
        };
        var module = new TrayPreviewActionModule(panel);
        var patch = new JsonObject
        {
            ["trayPath"] = @"E:\HijackTray",
            ["authorFilter"] = "alice"
        };

        var ok = module.TryApplyActionPatch(patch, out var error);

        Assert.True(ok, error);
        Assert.Equal(@"D:\Tray", panel.TrayRoot);
        Assert.Equal("alice", panel.AuthorFilter);
    }

    [Fact]
    public void TrayDependenciesModule_SupportedPatchKeys_DoNotContainPathOverride()
    {
        var panel = new TrayDependenciesPanelState
        {
            TrayPath = @"D:\Tray",
            ModsPath = @"D:\Mods"
        };
        var module = new TrayDependenciesActionModule(panel);

        Assert.DoesNotContain("trayPath", module.SupportedActionPatchKeys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("modsPath", module.SupportedActionPatchKeys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrayDependenciesModule_ApplyPatch_IgnoresPathOverride()
    {
        var panel = new TrayDependenciesPanelState
        {
            TrayPath = @"D:\Tray",
            ModsPath = @"D:\Mods"
        };
        var module = new TrayDependenciesActionModule(panel);
        var patch = new JsonObject
        {
            ["trayPath"] = @"E:\HijackTray",
            ["modsPath"] = @"E:\HijackMods",
            ["trayItemKey"] = "0x1"
        };

        var ok = module.TryApplyActionPatch(patch, out var error);

        Assert.True(ok, error);
        Assert.Equal(@"D:\Tray", panel.TrayPath);
        Assert.Equal(@"D:\Mods", panel.ModsPath);
        Assert.Equal("0x1", panel.TrayItemKey);
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

        public void LoadFromSettings(AppSettings settings)
        {
        }

        public void SaveToSettings(AppSettings settings)
        {
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

    private sealed class StubOrganizeModuleState : IOrganizeModuleState
    {
        public string SourceDir { get; set; } = string.Empty;
        public string ZipNamePattern { get; set; } = "*";
        public string ModsRoot { get; set; } = string.Empty;
        public string UnifiedModsFolder { get; set; } = string.Empty;
        public string TrayRoot { get; set; } = string.Empty;
        public bool KeepZip { get; set; }
    }

    private sealed class StubMergeModuleState : IMergeModuleState
    {
        private readonly List<string> _sourcePaths = new();

        public string TargetPath { get; set; } = string.Empty;

        public IReadOnlyList<string> CollectSourcePaths()
        {
            return _sourcePaths
                .Select(path => path.Trim())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public string SerializeSourcePaths()
        {
            return string.Join(Environment.NewLine, CollectSourcePaths());
        }

        public void ApplySourcePathsText(string? rawValue)
        {
            var tokens = (rawValue ?? string.Empty)
                .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            ReplaceSourcePaths(tokens);
        }

        public void ReplaceSourcePaths(IReadOnlyList<string> sourcePaths)
        {
            _sourcePaths.Clear();
            _sourcePaths.AddRange(sourcePaths);
        }
    }

    private sealed class TrayPreviewPanelState : ITrayPreviewModuleState
    {
        public string TrayRoot { get; set; } = string.Empty;
        public string PresetTypeFilter { get; set; } = "All";
        public string BuildSizeFilter { get; set; } = "All";
        public string HouseholdSizeFilter { get; set; } = "All";
        public string AuthorFilter { get; set; } = string.Empty;
        public string TimeFilter { get; set; } = "All";
        public string SearchQuery { get; set; } = string.Empty;
        public string LayoutMode { get; set; } = "Entry";
        public bool EnableDebugPreview { get; set; }
    }

    private sealed class TrayDependenciesPanelState : ITrayDependenciesModuleState
    {
        public string TrayPath { get; set; } = string.Empty;
        public string ModsPath { get; set; } = string.Empty;
        public string TrayItemKey { get; set; } = string.Empty;
        public string S4tiPath { get; set; } = string.Empty;
        public string MinMatchCountText { get; set; } = "1";
        public string TopNText { get; set; } = "200";
        public string MaxPackageCountText { get; set; } = "0";
        public bool ExportUnusedPackages { get; set; }
        public bool ExportMatchedPackages { get; set; }
        public string OutputCsv { get; set; } = string.Empty;
        public string UnusedOutputCsv { get; set; } = string.Empty;
        public string ExportTargetPath { get; set; } = string.Empty;
        public string ExportMinConfidence { get; set; } = "Low";
    }
}
