using SimsModDesktop.Models;
using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class TrayMetadataIndexStoreTests
{
    [Fact]
    public void StoreAndGetMetadata_PersistsExtendedTrayMetadataFieldsWithoutUiProjection()
    {
        using var trayDir = new TempDirectory("tray-index");
        using var cacheDir = new TempDirectory("tray-cache");
        var trayItemPath = Path.Combine(trayDir.Path, "0x1.trayitem");
        File.WriteAllBytes(trayItemPath, [0x01, 0x02, 0x03]);
        File.SetLastWriteTimeUtc(trayItemPath, DateTime.UtcNow.AddSeconds(2));

        var metadata = new TrayMetadataResult
        {
            TrayItemPath = trayItemPath,
            TrayMetadataId = "12345",
            Name = "Hidden Metadata Payload",
            DescriptionHashtags = "#tagA #tagB",
            CgName = "CG Flag",
            VenueType = 3001,
            LotTraits = [10001, 10002],
            RoomType = 41,
            Members =
            [
                new TrayMemberDisplayMetadata
                {
                    SlotIndex = 1,
                    FullName = "Alice Prescott",
                    Age = 4,
                    Species = 1,
                    OccultTypes = 8,
                    FameRankedStatId = 55,
                    FameValue = 4.5f
                }
            ]
        };

        var writer = new TrayMetadataIndexStore(cacheDir.Path);
        writer.Store(new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase)
        {
            [trayItemPath] = metadata
        });

        var reader = new TrayMetadataIndexStore(cacheDir.Path);
        var loaded = reader.GetMetadata([trayItemPath]);

        var result = Assert.Single(loaded);
        Assert.Equal("12345", result.Value.TrayMetadataId);
        Assert.Equal("#tagA #tagB", result.Value.DescriptionHashtags);
        Assert.Equal("CG Flag", result.Value.CgName);
        Assert.Equal((ulong)3001, result.Value.VenueType);
        Assert.Equal(new ulong[] { 10001, 10002 }, result.Value.LotTraits);
        Assert.Equal((uint)41, result.Value.RoomType);

        var member = Assert.Single(result.Value.Members);
        Assert.Equal((uint)4, member.Age);
        Assert.Equal((uint)1, member.Species);
        Assert.Equal((uint)8, member.OccultTypes);
        Assert.Equal((ulong)55, member.FameRankedStatId);
        Assert.Equal(4.5f, member.FameValue.GetValueOrDefault());
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
