using SimsModDesktop.Application.Settings;

namespace SimsModDesktop.Tests;

public sealed class MainWindowSettingsProjectionTests
{
    [Fact]
    public void Resolve_TrayPreviewSelection_MapsToTrayWorkspaceAndSafeAction()
    {
        var projection = new MainWindowSettingsProjection();
        var settings = new AppSettings
        {
            UiLanguageCode = "zh-CN",
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

        Assert.Equal("zh-CN", resolved.UiLanguageCode);
        Assert.Equal(SimsAction.Organize, resolved.SelectedAction);
        Assert.Equal(AppWorkspace.TrayPreview, resolved.Workspace);
        Assert.True(resolved.WhatIf);
        Assert.True(resolved.SharedFileOps.SkipPruneEmptyDirs);
        Assert.True(resolved.UiState.ToolkitLogDrawerOpen);
    }
}
