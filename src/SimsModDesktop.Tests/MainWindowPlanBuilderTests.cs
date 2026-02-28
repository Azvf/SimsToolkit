using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.Tests;

public sealed class MainWindowPlanBuilderTests
{
    [Fact]
    public void TryBuildToolkitCliPlan_FindDupWithSharedOptions_Succeeds()
    {
        var findDupPanel = new FindDupPanelViewModel
        {
            RootPath = @"D:\Mods",
            OutputCsv = @"D:\out.csv",
            Recurse = true,
            Cleanup = false
        };
        var registry = new ActionModuleRegistry(
        [
            new FindDupActionModule(findDupPanel)
        ]);
        var builder = new MainWindowPlanBuilder(registry);

        var ok = builder.TryBuildToolkitCliPlan(
            new MainWindowPlanBuilderState
            {
                ScriptPath = @"C:\tools\sims-mod-cli.ps1",
                WhatIf = true,
                SelectedAction = SimsAction.FindDuplicates,
                SharedFileOps = new SharedFileOpsPlanState
                {
                    SkipPruneEmptyDirs = true,
                    ModFilesOnly = true,
                    VerifyContentOnNameConflict = true,
                    ModExtensionsText = ".package, .ts4script",
                    PrefixHashBytesText = "4096",
                    HashWorkerCountText = "2"
                }
            },
            out var module,
            out var plan,
            out var error);

        Assert.True(ok, error);
        Assert.Equal(SimsAction.FindDuplicates, module.Action);
        var input = Assert.IsType<FindDupInput>(plan.Input);
        Assert.Equal(@"C:\tools\sims-mod-cli.ps1", input.ScriptPath);
        Assert.True(input.WhatIf);
        Assert.True(input.Shared.SkipPruneEmptyDirs);
        Assert.True(input.Shared.ModFilesOnly);
        Assert.True(input.Shared.VerifyContentOnNameConflict);
        Assert.Equal(4096, input.Shared.PrefixHashBytes);
        Assert.Equal(2, input.Shared.HashWorkerCount);
        Assert.Contains(".package", input.Shared.ModExtensions);
        Assert.Contains(".ts4script", input.Shared.ModExtensions);
    }

    [Fact]
    public void TryBuildToolkitCliPlan_MissingScriptPath_Fails()
    {
        var registry = new ActionModuleRegistry(
        [
            new OrganizeActionModule(new OrganizePanelViewModel())
        ]);
        var builder = new MainWindowPlanBuilder(registry);

        var ok = builder.TryBuildToolkitCliPlan(
            new MainWindowPlanBuilderState
            {
                ScriptPath = string.Empty,
                SelectedAction = SimsAction.Organize
            },
            out _,
            out _,
            out var error);

        Assert.False(ok);
        Assert.Contains("Script path is required", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildToolkitCliPlan_InvalidSharedNumber_Fails()
    {
        var registry = new ActionModuleRegistry(
        [
            new FindDupActionModule(new FindDupPanelViewModel())
        ]);
        var builder = new MainWindowPlanBuilder(registry);

        var ok = builder.TryBuildToolkitCliPlan(
            new MainWindowPlanBuilderState
            {
                ScriptPath = @"C:\tools\sims-mod-cli.ps1",
                SelectedAction = SimsAction.FindDuplicates,
                SharedFileOps = new SharedFileOpsPlanState
                {
                    PrefixHashBytesText = "NaN"
                }
            },
            out _,
            out _,
            out var error);

        Assert.False(ok);
        Assert.Contains("Invalid number", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildTrayPreviewInput_WithoutScriptPath_Succeeds()
    {
        var trayRoot = Path.GetTempPath();
        var trayPreviewPanel = new TrayPreviewPanelViewModel
        {
            TrayRoot = trayRoot,
            PresetTypeFilter = "Lot",
            BuildSizeFilter = "30 x 30",
            HouseholdSizeFilter = "All",
            AuthorFilter = "Author-01",
            TimeFilter = "Last30d",
            SearchQuery = "villa"
        };
        var registry = new ActionModuleRegistry(
        [
            new TrayPreviewActionModule(trayPreviewPanel)
        ]);
        var builder = new MainWindowPlanBuilder(registry);

        var ok = builder.TryBuildTrayPreviewInput(
            new MainWindowPlanBuilderState
            {
                ScriptPath = string.Empty,
                SelectedAction = SimsAction.Organize
            },
            out var input,
            out var error);

        Assert.True(ok, error);
        Assert.Equal(Path.GetFullPath(trayRoot), input.TrayPath);
        Assert.Equal("Lot", input.PresetTypeFilter);
        Assert.Equal("30 x 30", input.BuildSizeFilter);
        Assert.Equal("All", input.HouseholdSizeFilter);
        Assert.Equal("Author-01", input.AuthorFilter);
        Assert.Equal("Last30d", input.TimeFilter);
        Assert.Equal("villa", input.SearchQuery);
    }
}
