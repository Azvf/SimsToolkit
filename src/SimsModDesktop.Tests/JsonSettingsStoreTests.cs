using SimsModDesktop.Infrastructure.Settings;

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
                SelectedWorkspace = AppWorkspace.TrayPreview,
                SelectedAction = SimsAction.FindDuplicates,
                WhatIf = true
            };

            await store.SaveAsync(settings);
            var loaded = await store.LoadAsync();

            Assert.Equal(settings.SelectedWorkspace, loaded.SelectedWorkspace);
            Assert.Equal(settings.SelectedAction, loaded.SelectedAction);
            Assert.True(loaded.WhatIf);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_DoesNotLeaveTempFiles()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var path = Path.Combine(tempDir.FullName, "settings.json");

        try
        {
            var store = new JsonSettingsStore(path);

            await store.SaveAsync(new AppSettings { UiLanguageCode = "en-US" });
            await store.SaveAsync(new AppSettings { UiLanguageCode = "zh-CN" });

            var leftovers = Directory
                .GetFiles(tempDir.FullName, "settings.json.*.tmp", SearchOption.TopDirectoryOnly);

            Assert.True(File.Exists(path));
            Assert.Empty(leftovers);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
