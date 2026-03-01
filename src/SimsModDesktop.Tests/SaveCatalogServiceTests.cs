using SimsModDesktop.SaveData.Services;

namespace SimsModDesktop.Tests;

public sealed class SaveCatalogServiceTests
{
    [Fact]
    public void GetPrimarySaveFiles_InvalidPath_ReturnsEmpty()
    {
        var service = new SaveCatalogService();

        var result = service.GetPrimarySaveFiles("Z:\\this\\path\\should\\not\\exist");

        Assert.Empty(result);
    }

    [Fact]
    public void GetPrimarySaveFiles_OnlyReturnsPrimarySaveFiles_SortedNewestFirst()
    {
        var service = new SaveCatalogService();
        using var root = new TempDirectory();
        var olderPath = Path.Combine(root.Path, "Slot_00000001.save");
        var newerPath = Path.Combine(root.Path, "Slot_00000002.save");
        var backupPath = Path.Combine(root.Path, "Slot_00000003.save.ver0");

        File.WriteAllText(olderPath, "old");
        File.WriteAllText(newerPath, "new");
        File.WriteAllText(backupPath, "backup");
        File.SetLastWriteTimeUtc(olderPath, new DateTime(2026, 2, 1, 1, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newerPath, new DateTime(2026, 2, 1, 2, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(backupPath, new DateTime(2026, 2, 1, 3, 0, 0, DateTimeKind.Utc));

        var result = service.GetPrimarySaveFiles(root.Path);

        Assert.Collection(
            result,
            first => Assert.Equal("Slot_00000002.save", first.FileName),
            second => Assert.Equal("Slot_00000001.save", second.FileName));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"save-catalog-{Guid.NewGuid():N}");
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
