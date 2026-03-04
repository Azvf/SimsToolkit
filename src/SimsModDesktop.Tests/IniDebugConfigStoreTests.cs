using SimsModDesktop.Application.Settings;
using SimsModDesktop.Infrastructure.Settings;

namespace SimsModDesktop.Tests;

public sealed class IniDebugConfigStoreTests
{
    [Fact]
    public async Task LoadAsync_ReturnsEmpty_WhenFileDoesNotExist()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var iniPath = Path.Combine(tempDir.FullName, "debug-config.ini");

        try
        {
            var store = new IniDebugConfigStore(iniPath);
            var loaded = await store.LoadAsync();

            Assert.Empty(loaded);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsEntries()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var iniPath = Path.Combine(tempDir.FullName, "debug-config.ini");

        try
        {
            var store = new IniDebugConfigStore(iniPath);
            var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mods.catalog.fast_mode"] = "false",
                ["tray.preview.trace_progress"] = "true"
            };

            await store.SaveAsync(expected);
            var loaded = await store.LoadAsync();

            Assert.Equal("false", loaded["mods.catalog.fast_mode"]);
            Assert.Equal("true", loaded["tray.preview.trace_progress"]);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task EnsureTemplateAsync_CreatesTemplateFile_WithCommentsAndDefaults()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var iniPath = Path.Combine(tempDir.FullName, "debug-config.ini");

        try
        {
            var store = new IniDebugConfigStore(iniPath);
            var template = new[]
            {
                new DebugConfigTemplateEntry(
                    "mods.catalog.fast_mode",
                    "true",
                    "Use the fast Mod catalog path for local experiments."),
                new DebugConfigTemplateEntry(
                    "tray.preview.trace_progress",
                    "true",
                    "Write extra progress checkpoints while loading tray previews.")
            };

            await store.EnsureTemplateAsync(template);

            Assert.True(File.Exists(iniPath));
            var text = await File.ReadAllTextAsync(iniPath);
            Assert.Contains("[debug]", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("mods.catalog.fast_mode", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Use the fast Mod catalog path for local experiments.", text, StringComparison.Ordinal);

            var loaded = await store.LoadAsync();
            Assert.Equal("true", loaded["mods.catalog.fast_mode"]);
            Assert.Equal("true", loaded["tray.preview.trace_progress"]);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task EnsureTemplateAsync_DoesNotOverrideExistingValue()
    {
        var tempDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var iniPath = Path.Combine(tempDir.FullName, "debug-config.ini");

        try
        {
            var store = new IniDebugConfigStore(iniPath);
            await store.SaveAsync(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mods.catalog.fast_mode"] = "false"
            });

            await store.EnsureTemplateAsync(
            [
                new DebugConfigTemplateEntry(
                    "mods.catalog.fast_mode",
                    "true",
                    "Use the fast Mod catalog path for local experiments."),
                new DebugConfigTemplateEntry(
                    "tray.preview.show_trace",
                    "true",
                    "Show verbose tray preview trace output.")
            ]);

            var loaded = await store.LoadAsync();
            Assert.Equal("false", loaded["mods.catalog.fast_mode"]);
            Assert.Equal("true", loaded["tray.preview.show_trace"]);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
