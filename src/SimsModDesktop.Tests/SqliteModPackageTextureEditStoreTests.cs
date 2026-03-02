using SimsModDesktop.Application.TextureCompression;

namespace SimsModDesktop.Tests;

public sealed class SqliteModPackageTextureEditStoreTests
{
    [Fact]
    public async Task SaveLoadAndMarkRolledBack_RoundTripsBinaryHistory()
    {
        using var temp = new TempDirectory();
        var store = new SqliteModPackageTextureEditStore(temp.Path);
        var record = new ModPackageTextureEditRecord
        {
            EditId = "edit-1",
            PackagePath = @"D:\Mods\demo.package",
            ResourceKeyText = "00B2D882:00000000:0000000000000001",
            RecordKind = "Apply",
            AppliedAction = "ConvertToBC3",
            OriginalBytes = [1, 2, 3],
            ReplacementBytes = [4, 5, 6],
            AppliedUtcTicks = DateTime.UtcNow.Ticks,
            Notes = "unit-test"
        };

        await store.SaveAsync(record);

        var latest = await store.TryGetLatestActiveEditAsync(record.PackagePath, record.ResourceKeyText);
        Assert.NotNull(latest);
        Assert.Equal(record.EditId, latest!.EditId);
        Assert.Equal(record.OriginalBytes, latest.OriginalBytes);
        Assert.Equal(record.ReplacementBytes, latest.ReplacementBytes);

        await store.MarkRolledBackAsync(record.EditId);

        var activeAfterRollback = await store.TryGetLatestActiveEditAsync(record.PackagePath, record.ResourceKeyText);
        Assert.Null(activeAfterRollback);

        var history = await store.GetHistoryAsync(record.PackagePath, record.ResourceKeyText, maxCount: 5);
        Assert.Single(history);
        Assert.NotNull(history[0].RolledBackUtcTicks);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SimsToolkit_ModTextureEditStore_" + Guid.NewGuid().ToString("N"));
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
