using SimsModDesktop.Application.TextureCompression;

namespace SimsModDesktop.Tests;

public sealed class SqliteModPackageTextureAnalysisStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsSummaryFromLocalDatabase()
    {
        using var temp = new TempDirectory();
        var store = new SqliteModPackageTextureAnalysisStore(temp.Path);
        var summary = new ModPackageTextureSummary
        {
            PackagePath = @"D:\Mods\demo.package",
            FileLength = 4096,
            LastWriteUtcTicks = 12345,
            TextureResourceCount = 10,
            DdsCount = 6,
            PngCount = 2,
            UnsupportedTextureCount = 2,
            EditableTextureCount = 8,
            TotalTextureBytes = 2048,
            LastAnalyzedLocal = new DateTime(2026, 3, 2, 22, 0, 0, DateTimeKind.Local)
        };

        await store.SaveAsync(new ModPackageTextureAnalysisResult
        {
            Summary = summary,
            Candidates =
            [
                new ModPackageTextureCandidate
                {
                    ResourceKeyText = "00B2D882:00000000:0000000000000001",
                    ContainerKind = "DDS",
                    Format = "DXT5",
                    Width = 1024,
                    Height = 1024,
                    MipMapCount = 11,
                    Editable = true,
                    SuggestedAction = "Keep",
                    Notes = "Already compressed.",
                    SizeBytes = 512
                }
            ]
        });
        var loaded = await store.TryGetAsync(summary.PackagePath, summary.FileLength, summary.LastWriteUtcTicks);

        Assert.NotNull(loaded);
        Assert.Equal(summary.PackagePath, loaded!.Summary.PackagePath);
        Assert.Equal(summary.TextureResourceCount, loaded.Summary.TextureResourceCount);
        Assert.Equal(summary.EditableTextureCount, loaded.Summary.EditableTextureCount);
        Assert.Equal(summary.TotalTextureBytes, loaded.Summary.TotalTextureBytes);
        Assert.Single(loaded.Candidates);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SimsToolkit_ModTextureStore_" + Guid.NewGuid().ToString("N"));
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
