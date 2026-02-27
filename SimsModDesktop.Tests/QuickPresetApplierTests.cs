using System.Text.Json.Nodes;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Presets;
using SimsModDesktop.Models;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.Tests;

public sealed class QuickPresetApplierTests
{
    [Fact]
    public void TryApply_FlattenPreset_UpdatesActionAndSharedFields()
    {
        var flattenPanel = new FlattenPanelViewModel();
        var sharedPanel = new SharedFileOpsPanelViewModel();
        var registry = new ActionModuleRegistry(new IActionModule[]
        {
            new FlattenActionModule(flattenPanel)
        });

        var applier = new QuickPresetApplier(registry, sharedPanel);
        var preset = new QuickPresetDefinition
        {
            Id = "flatten-fast",
            Name = "Flatten Fast",
            Action = SimsAction.Flatten,
            ActionPatch = new JsonObject
            {
                ["rootPath"] = @"D:\Mods\Root",
                ["flattenToRoot"] = true
            },
            SharedPatch = new JsonObject
            {
                ["modFilesOnly"] = true,
                ["hashWorkerCount"] = 12
            }
        };

        var ok = applier.TryApply(preset, out var error);

        Assert.True(ok, error);
        Assert.Equal(@"D:\Mods\Root", flattenPanel.RootPath);
        Assert.True(flattenPanel.FlattenToRoot);
        Assert.True(sharedPanel.ModFilesOnly);
        Assert.Equal("12", sharedPanel.HashWorkerCountText);
    }
}
