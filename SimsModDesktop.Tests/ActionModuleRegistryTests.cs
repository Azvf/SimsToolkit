using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.Tests;

public sealed class ActionModuleRegistryTests
{
    [Fact]
    public void MergeModule_TryBuildPlan_DeduplicatesSourcePaths()
    {
        var panel = new MergePanelViewModel();
        panel.ReplaceSourcePaths(new[]
        {
            @"D:\Mods\A",
            @"D:\Mods\A",
            @"D:\Mods\B"
        });
        panel.TargetPath = @"D:\Mods\Merged";

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
            new OrganizeActionModule(new OrganizePanelViewModel())
        });

        Assert.Throws<InvalidOperationException>(() => registry.Get(SimsAction.TrayPreview));
    }
}
