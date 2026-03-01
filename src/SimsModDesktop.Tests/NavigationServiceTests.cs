using SimsModDesktop.Models;
using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class NavigationServiceTests
{
    [Fact]
    public void SelectSection_UpdatesSelection()
    {
        var nav = new NavigationService();

        nav.SelectSection(AppSection.Tray);

        Assert.Equal(AppSection.Tray, nav.SelectedSection);
    }

    [Fact]
    public void SectionItems_ExposeAllTopLevelSections()
    {
        var nav = new NavigationService();

        Assert.Collection(
            nav.SectionItems,
            item => Assert.Equal(AppSection.Toolkit, item.Section),
            item => Assert.Equal(AppSection.Mods, item.Section),
            item => Assert.Equal(AppSection.Tray, item.Section),
            item => Assert.Equal(AppSection.Saves, item.Section),
            item => Assert.Equal(AppSection.Settings, item.Section));
    }
}
