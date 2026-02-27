using SimsModDesktop.Infrastructure.Settings;
using SimsModDesktop.Models;

namespace SimsModDesktop.Tests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsSettings()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var path = Path.Combine(tempDir.FullName, "settings.json");

        try
        {
            var store = new JsonSettingsStore(path);
            var settings = new AppSettings
            {
                ScriptPath = "C:\\tools\\sims-mod-cli.ps1",
                SelectedWorkspace = AppWorkspace.TrayPreview,
                SelectedAction = SimsAction.FindDuplicates,
                WhatIf = true
            };

            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            Assert.Equal(settings.ScriptPath, loaded.ScriptPath);
            Assert.Equal(settings.SelectedWorkspace, loaded.SelectedWorkspace);
            Assert.Equal(settings.SelectedAction, loaded.SelectedAction);
            Assert.True(loaded.WhatIf);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
