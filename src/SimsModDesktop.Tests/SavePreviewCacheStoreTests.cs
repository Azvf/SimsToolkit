using SimsModDesktop.Application.Saves;

namespace SimsModDesktop.Tests;

public sealed class SavePreviewCacheStoreTests
{
    [Fact]
    public void SaveThenTryLoad_PersistsManifestInSqlite()
    {
        using var cacheDir = new TempDirectory("save-preview-cache");
        using var saveFile = new TempFile(".save");
        var store = new SavePreviewCacheStore(cacheDir.Path);
        var manifest = new SavePreviewCacheManifest
        {
            SourceSavePath = saveFile.Path,
            SourceLength = new FileInfo(saveFile.Path).Length,
            SourceLastWriteTimeUtc = File.GetLastWriteTimeUtc(saveFile.Path),
            CacheSchemaVersion = "ignored-on-save",
            BuildStartedUtc = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc),
            BuildCompletedUtc = new DateTime(2026, 3, 2, 12, 1, 0, DateTimeKind.Utc),
            TotalHouseholdCount = 3,
            ExportableHouseholdCount = 2,
            ReadyHouseholdCount = 1,
            FailedHouseholdCount = 1,
            BlockedHouseholdCount = 1,
            Entries =
            [
                new SavePreviewCacheHouseholdEntry
                {
                    HouseholdId = 42,
                    HouseholdName = "Unit Test",
                    BuildState = "Ready",
                    TrayItemKey = "0x42",
                    GeneratedFileNames = ["0x42.trayitem", "0x42.householdbinary"]
                }
            ]
        };

        store.Save(saveFile.Path, manifest);

        Assert.True(File.Exists(Path.Combine(cacheDir.Path, "app-cache.db")));
        Assert.False(File.Exists(Path.Combine(store.GetCacheRootPath(saveFile.Path), "manifest.json")));

        var loaded = store.TryLoad(saveFile.Path, out var reloaded);

        Assert.True(loaded);
        Assert.Equal(Path.GetFullPath(saveFile.Path), reloaded.SourceSavePath);
        Assert.Equal(3, reloaded.TotalHouseholdCount);
        Assert.Equal(2, reloaded.ExportableHouseholdCount);
        var entry = Assert.Single(reloaded.Entries);
        Assert.Equal((ulong)42, entry.HouseholdId);
        Assert.Equal("0x42", entry.TrayItemKey);
        Assert.Equal(new[] { "0x42.trayitem", "0x42.householdbinary" }, entry.GeneratedFileNames);
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

    private sealed class TempFile : IDisposable
    {
        public TempFile(string extension)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
            File.WriteAllText(Path, "save");
            File.SetLastWriteTimeUtc(Path, new DateTime(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc));
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
