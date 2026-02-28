using SimsModDesktop.Models;
using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class NavigationServiceTests
{
    [Fact]
    public void SelectSection_UpdatesCurrentModulesAndDefaultModule()
    {
        var nav = new NavigationService();

        nav.SelectSection(AppSection.Tray);

        Assert.Equal(AppSection.Tray, nav.SelectedSection);
        Assert.Equal("traypreview", nav.SelectedModuleKey);
        Assert.Equal(2, nav.CurrentModules.Count);
    }

    [Fact]
    public void SelectModule_InvalidKey_NoChange()
    {
        var nav = new NavigationService();
        nav.SelectSection(AppSection.Mods);
        var original = nav.SelectedModuleKey;

        nav.SelectModule("invalid-module");

        Assert.Equal(original, nav.SelectedModuleKey);
    }
}
