using System.Reflection;
using SimsModDesktop.Application.Results;
using SimsModDesktop.Models;
using SimsModDesktop.Infrastructure.Persistence;

namespace SimsModDesktop.Tests;

public sealed class ActionResultRepositoryTests
{
    [Fact]
    public void AppCacheDatabase_DefaultConstructor_UsesSingleAppCacheFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var expectedPath = Path.Combine(appData, "SimsModDesktop", "Cache", "app-cache.db");
        var database = new AppCacheDatabase();
        var field = typeof(AppCacheDatabase).GetField("_databasePath", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        var actualPath = field!.GetValue(database) as string;

        Assert.Equal(expectedPath, actualPath);
    }

    [Fact]
    public async Task SaveAsync_PersistsAndReloadsLatestTwenty()
    {
        using var cacheDir = new TempDirectory("action-history");
        var writer = new ActionResultRepository(cacheDir.Path);

        for (var index = 0; index < 25; index++)
        {
            await writer.SaveAsync(new ActionResultEnvelope
            {
                Action = SimsAction.FindDuplicates,
                Source = "Test",
                GeneratedAtLocal = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Local).AddMinutes(index),
                Rows =
                [
                    new ActionResultRow
                    {
                        Name = $"Run {index}",
                        Status = $"Status {index}",
                        RawSummary = $"Summary {index}"
                    }
                ]
            });
        }

        var reader = new ActionResultRepository(cacheDir.Path);
        await reader.InitializeAsync();

        Assert.Equal(20, reader.History.Count);
        Assert.Equal("Run 24", reader.Latest!.Rows[0].Name);
        Assert.DoesNotContain(reader.History, item => item.Rows[0].Name == "Run 0");
    }

    [Fact]
    public async Task ClearAsync_RemovesPersistedHistory()
    {
        using var cacheDir = new TempDirectory("action-history-clear");
        var repository = new ActionResultRepository(cacheDir.Path);
        await repository.SaveAsync(new ActionResultEnvelope
        {
            Action = SimsAction.TrayDependencies,
            Source = "Test",
            Rows = [new ActionResultRow { Name = "Single", Status = "Done" }]
        });

        await repository.ClearAsync();

        var reloaded = new ActionResultRepository(cacheDir.Path);
        await reloaded.InitializeAsync();
        Assert.Empty(reloaded.History);
        Assert.Null(reloaded.Latest);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
