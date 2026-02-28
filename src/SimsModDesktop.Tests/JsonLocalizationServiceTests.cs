using SimsModDesktop.Infrastructure.Localization;

namespace SimsModDesktop.Tests;

public sealed class JsonLocalizationServiceTests
{
    [Fact]
    public void AvailableLanguages_LoadedFromLocalizationFiles()
    {
        using var temp = new TempDirectory();
        WriteLocale(temp.Path, "en-US.json", new Dictionary<string, string>
        {
            ["status.ready"] = "Ready EN"
        });
        WriteLocale(temp.Path, "ja-JP.json", new Dictionary<string, string>
        {
            ["status.ready"] = "Ready JA"
        });
        WriteLocale(temp.Path, "zh-CN.todo.json", new Dictionary<string, string>
        {
            ["status.ready"] = "TODO zh"
        });

        using var service = new JsonLocalizationService(Path.Combine(temp.Path, "assets", "localization"));

        Assert.Contains(service.AvailableLanguages, option => option.Code == "en-US");
        Assert.Contains(service.AvailableLanguages, option => option.Code == "ja-JP");
        Assert.Contains(service.AvailableLanguages, option => option.Code == "zh-CN");

        service.SetLanguage("ja-JP");
        Assert.Equal("ja-JP", service.CurrentLanguageCode);
        Assert.Equal("Ready JA", service["status.ready"]);
    }

    [Fact]
    public async Task AvailableLanguages_ReloadsWhenNewLanguageFileAdded()
    {
        using var temp = new TempDirectory();
        WriteLocale(temp.Path, "en-US.json", new Dictionary<string, string>
        {
            ["status.ready"] = "Ready EN"
        });

        using var service = new JsonLocalizationService(Path.Combine(temp.Path, "assets", "localization"));
        Assert.DoesNotContain(service.AvailableLanguages, option => option.Code == "fr-FR");

        WriteLocale(temp.Path, "fr-FR.json", new Dictionary<string, string>
        {
            ["status.ready"] = "Pret"
        });

        var loaded = await WaitUntilAsync(
            () => service.AvailableLanguages.Any(option => option.Code == "fr-FR"),
            timeout: TimeSpan.FromSeconds(5));
        Assert.True(loaded);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return condition();
    }

    private static void WriteLocale(string rootPath, string fileName, IReadOnlyDictionary<string, string> values)
    {
        var localizationDir = Path.Combine(rootPath, "assets", "localization");
        Directory.CreateDirectory(localizationDir);

        var lines = values
            .Select(pair => $"  \"{pair.Key}\": \"{pair.Value}\"")
            .ToArray();
        var json = "{\n" + string.Join(",\n", lines) + "\n}";
        File.WriteAllText(Path.Combine(localizationDir, fileName), json);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"SimsToolkit.Localization.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Ignore temp cleanup failures.
            }
        }
    }
}
