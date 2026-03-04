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
                ["startup.tray_cache_warmup.enabled"] = "false",
                ["startup.tray_cache_warmup.verbose_log"] = "true"
            };

            await store.SaveAsync(expected);
            var loaded = await store.LoadAsync();

            Assert.Equal("false", loaded["startup.tray_cache_warmup.enabled"]);
            Assert.Equal("true", loaded["startup.tray_cache_warmup.verbose_log"]);
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
                    "startup.tray_cache_warmup.enabled",
                    "true",
                    "Build tray dependency package index on startup when no local cache exists."),
                new DebugConfigTemplateEntry(
                    "startup.tray_cache_warmup.verbose_log",
                    "true",
                    "Write warmup progress checkpoints into the toolkit log.")
            };

            await store.EnsureTemplateAsync(template);

            Assert.True(File.Exists(iniPath));
            var text = await File.ReadAllTextAsync(iniPath);
            Assert.Contains("[debug]", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("startup.tray_cache_warmup.enabled", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Build tray dependency package index on startup when no local cache exists.", text, StringComparison.Ordinal);

            var loaded = await store.LoadAsync();
            Assert.Equal("true", loaded["startup.tray_cache_warmup.enabled"]);
            Assert.Equal("true", loaded["startup.tray_cache_warmup.verbose_log"]);
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
                ["startup.tray_cache_warmup.enabled"] = "false"
            });

            await store.EnsureTemplateAsync(
            [
                new DebugConfigTemplateEntry(
                    "startup.tray_cache_warmup.enabled",
                    "true",
                    "Build tray dependency package index on startup when no local cache exists."),
                new DebugConfigTemplateEntry(
                    "startup.tray_cache_warmup.show_banner",
                    "true",
                    "Show startup warmup progress panel and status text in Shell.")
            ]);

            var loaded = await store.LoadAsync();
            Assert.Equal("false", loaded["startup.tray_cache_warmup.enabled"]);
            Assert.Equal("true", loaded["startup.tray_cache_warmup.show_banner"]);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
