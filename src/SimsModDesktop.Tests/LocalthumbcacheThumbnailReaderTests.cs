using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class LocalthumbcacheThumbnailReaderTests
{
    [Fact]
    public void TryExtractBestImage_ReadsMatchingResourceFromSyntheticPackage()
    {
        using var fixture = new TempSimsFolder();
        var png = ImageTestHelpers.CreatePngBytes(3, 2);
        var resourceOffset = 96;
        var packageBytes = new byte[resourceOffset + png.Length + 8];
        var instanceBytes = BitConverter.GetBytes(0x0000000000000042UL);

        Buffer.BlockCopy(instanceBytes, 0, packageBytes, 16, instanceBytes.Length);
        Buffer.BlockCopy(BitConverter.GetBytes(resourceOffset), 0, packageBytes, 24, sizeof(int));
        Buffer.BlockCopy(BitConverter.GetBytes(png.Length), 0, packageBytes, 28, sizeof(int));
        Buffer.BlockCopy(png, 0, packageBytes, resourceOffset, png.Length);
        File.WriteAllBytes(fixture.LocalthumbcachePath, packageBytes);

        var reader = new LocalthumbcacheThumbnailReader();
        var image = reader.TryExtractBestImage(fixture.TrayPath, "0x0000000000000042");

        Assert.NotNull(image);
        Assert.Equal(3, image!.Width);
        Assert.Equal(2, image.Height);
    }

    [Fact]
    public void TryExtractBestImage_ReturnsNullWhenInstanceDoesNotMatch()
    {
        using var fixture = new TempSimsFolder();
        var png = ImageTestHelpers.CreatePngBytes(2, 2);
        var resourceOffset = 80;
        var packageBytes = new byte[resourceOffset + png.Length];
        var instanceBytes = BitConverter.GetBytes(0x0000000000000001UL);

        Buffer.BlockCopy(instanceBytes, 0, packageBytes, 8, instanceBytes.Length);
        Buffer.BlockCopy(BitConverter.GetBytes(resourceOffset), 0, packageBytes, 16, sizeof(int));
        Buffer.BlockCopy(BitConverter.GetBytes(png.Length), 0, packageBytes, 20, sizeof(int));
        Buffer.BlockCopy(png, 0, packageBytes, resourceOffset, png.Length);
        File.WriteAllBytes(fixture.LocalthumbcachePath, packageBytes);

        var reader = new LocalthumbcacheThumbnailReader();
        var image = reader.TryExtractBestImage(fixture.TrayPath, "0x0000000000000099");

        Assert.Null(image);
    }

    private sealed class TempSimsFolder : IDisposable
    {
        public TempSimsFolder()
        {
            RootPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sims-thumbs-{Guid.NewGuid():N}");
            TrayPath = System.IO.Path.Combine(RootPath, "Tray");
            Directory.CreateDirectory(TrayPath);
            LocalthumbcachePath = System.IO.Path.Combine(RootPath, "localthumbcache.package");
        }

        public string RootPath { get; }
        public string TrayPath { get; }
        public string LocalthumbcachePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
