using SimsModDesktop.Application.Presets;
using SimsModDesktop.Models;

namespace SimsModDesktop.Tests;

public sealed class QuickPresetCatalogTests
{
    [Fact]
    public async Task ReloadAsync_UserPresetOverridesBuiltInById()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var defaultsPath = Path.Combine(tempDir.FullName, "defaults.json");
        var userPath = Path.Combine(tempDir.FullName, "user.json");

        await File.WriteAllTextAsync(defaultsPath, """
        {
          "version": 1,
          "presets": [
            {
              "id": "merge-fast",
              "name": "Merge Fast",
              "action": "Merge",
              "actionPatch": { "targetPath": "D:\\Mods\\Merged-A" }
            }
          ]
        }
        """);

        await File.WriteAllTextAsync(userPath, """
        {
          "version": 1,
          "presets": [
            {
              "id": "merge-fast",
              "name": "Merge Fast Override",
              "action": "Merge",
              "actionPatch": { "targetPath": "D:\\Mods\\Merged-B" }
            }
          ]
        }
        """);

        try
        {
            var catalog = new QuickPresetCatalog(defaultsPath, userPath);
            await catalog.ReloadAsync();

            var presets = catalog.GetAll();
            Assert.Single(presets);
            Assert.Equal("merge-fast", presets[0].Id);
            Assert.Equal("Merge Fast Override", presets[0].Name);
            Assert.Equal(SimsAction.Merge, presets[0].Action);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReloadAsync_UnknownField_SkipsPresetAndAddsWarning()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var defaultsPath = Path.Combine(tempDir.FullName, "defaults.json");

        await File.WriteAllTextAsync(defaultsPath, """
        {
          "version": 1,
          "presets": [
            {
              "id": "bad",
              "name": "Bad",
              "action": "Flatten",
              "unexpected": "x",
              "actionPatch": {}
            }
          ]
        }
        """);

        try
        {
            var catalog = new QuickPresetCatalog(defaultsPath, Path.Combine(tempDir.FullName, "user.json"));
            await catalog.ReloadAsync();

            Assert.Empty(catalog.GetAll());
            Assert.NotEmpty(catalog.LastWarnings);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ReloadAsync_UnknownActionPatchField_SkipsPresetAndAddsWarning()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var defaultsPath = Path.Combine(tempDir.FullName, "defaults.json");

        await File.WriteAllTextAsync(defaultsPath, """
        {
          "version": 1,
          "presets": [
            {
              "id": "bad-action-patch",
              "name": "Bad Action Patch",
              "action": "Flatten",
              "actionPatch": { "unknownField": true }
            }
          ]
        }
        """);

        try
        {
            var catalog = new QuickPresetCatalog(defaultsPath, Path.Combine(tempDir.FullName, "user.json"));
            await catalog.ReloadAsync();

            Assert.Empty(catalog.GetAll());
            Assert.NotEmpty(catalog.LastWarnings);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
