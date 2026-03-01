using SimsModDesktop.Services;
using SimsModDesktop.Models;

namespace SimsModDesktop.Tests;

public sealed class TrayEmbeddedImageExtractorTests
{
    [Fact]
    public void TryExtractBestImage_DecodesExactEncodedTrayImageFile()
    {
        using var tempFile = new TempBinaryFile(".bpi");
        File.WriteAllBytes(
            tempFile.Path,
            ImageTestHelpers.CreateEncodedTrayImageBytes(
                ImageTestHelpers.CreateJpegBytes(2, 2)));

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

    [Fact]
    public void TryExtractBestImage_ItemScansSourceFilesWithoutS4ti()
    {
        using var tempTrayItem = new TempBinaryFile(".trayitem");
        using var tempSgi = new TempBinaryFile(".sgi");
        File.WriteAllBytes(tempTrayItem.Path, new byte[] { 0x01, 0x02, 0x03, 0x04 });
        File.WriteAllBytes(
            tempSgi.Path,
            ImageTestHelpers.CreateEncodedTrayImageBytes(
                ImageTestHelpers.CreateJpegBytes(3, 2)));

        var extractor = new TrayEmbeddedImageExtractor();
        var image = extractor.TryExtractBestImage(new SimsTrayPreviewItem
        {
            TrayItemKey = "0x1",
            PresetType = "Household",
            TrayRootPath = Path.GetDirectoryName(tempTrayItem.Path) ?? string.Empty,
            SourceFilePaths = [tempTrayItem.Path, tempSgi.Path]
        });

        Assert.NotNull(image);
        Assert.Equal(3, image!.Width);
        Assert.Equal(2, image.Height);
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
