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
}
