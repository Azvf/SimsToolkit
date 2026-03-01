using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class TrayEmbeddedImageExtractorTests
{
    [Fact]
    public void TryExtractBestImage_FindsPngEmbeddedInNoise()
    {
        using var tempFile = new TempBinaryFile(".bpi");
        var payload = new byte[] { 0x10, 0x20, 0x30, 0x40 }
            .Concat(ImageTestHelpers.CreatePngBytes(2, 2))
            .Concat(new byte[] { 0x50, 0x60, 0x70 })
            .ToArray();
        File.WriteAllBytes(tempFile.Path, payload);

        var extractor = new TrayEmbeddedImageExtractor();
        var image = extractor.TryExtractBestImage(tempFile.Path);

        Assert.NotNull(image);
        Assert.Equal(2, image!.Width);
        Assert.Equal(2, image.Height);
        Assert.NotEmpty(image.Data);
    }

    [Fact]
    public void TryExtractBestImage_ReturnsNullWhenNoImageIsPresent()
    {
        using var tempFile = new TempBinaryFile(".hhi");
        File.WriteAllBytes(tempFile.Path, new byte[] { 0x01, 0x02, 0x03, 0x04 });

        var extractor = new TrayEmbeddedImageExtractor();
        var image = extractor.TryExtractBestImage(tempFile.Path);

        Assert.Null(image);
    }

    private sealed class TempBinaryFile : IDisposable
    {
        public TempBinaryFile(string extension)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
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
