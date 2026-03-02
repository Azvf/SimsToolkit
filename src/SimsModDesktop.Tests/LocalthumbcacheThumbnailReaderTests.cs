using System.Buffers.Binary;
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

    [Fact]
    public void TryExtractBestImage_WithValidDbpfPackage_FiltersByTrayInstance()
    {
        using var fixture = new TempSimsFolder();
        var requested = ImageTestHelpers.CreatePngBytes(3, 2);
        var largerButWrong = ImageTestHelpers.CreatePngBytes(9, 9);
        WritePackage(
            fixture.LocalthumbcachePath,
            [
                new PackageSpec(0x11111111, 0, 0x0000000000000042, requested),
                new PackageSpec(0x22222222, 0, 0x0000000000000099, largerButWrong)
            ]);

        var reader = new LocalthumbcacheThumbnailReader();
        var image = reader.TryExtractBestImage(fixture.TrayPath, "0x0000000000000042");

        Assert.NotNull(image);
        Assert.Equal(3, image!.Width);
        Assert.Equal(2, image.Height);
    }

    [Fact]
    public void TryExtractBestImage_RebuildsPackageIndexWhenLocalthumbcacheTimestampChanges()
    {
        using var fixture = new TempSimsFolder();
        WritePackage(
            fixture.LocalthumbcachePath,
            [
                new PackageSpec(0x11111111, 0, 0x0000000000000042, ImageTestHelpers.CreatePngBytes(3, 2))
            ]);

        var reader = new LocalthumbcacheThumbnailReader();
        var first = reader.TryExtractBestImage(fixture.TrayPath, "0x0000000000000042");

        Assert.NotNull(first);
        Assert.Equal(3, first!.Width);
        Assert.Equal(2, first.Height);

        WritePackage(
            fixture.LocalthumbcachePath,
            [
                new PackageSpec(0x11111111, 0, 0x0000000000000042, ImageTestHelpers.CreatePngBytes(7, 5))
            ]);
        File.SetLastWriteTimeUtc(fixture.LocalthumbcachePath, DateTime.UtcNow.AddMinutes(1));

        var second = reader.TryExtractBestImage(fixture.TrayPath, "0x0000000000000042");

        Assert.NotNull(second);
        Assert.Equal(7, second!.Width);
        Assert.Equal(5, second.Height);
    }

    private static void WritePackage(string path, IReadOnlyList<PackageSpec> specs)
    {
        const int headerSize = 96;
        const int flagsSize = 4;
        const int indexEntrySize = 32;
        var indexPosition = headerSize;
        var recordSize = flagsSize + (indexEntrySize * specs.Count);
        var resourcePosition = headerSize + recordSize;

        var header = new byte[headerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 1179664964u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), 2u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(36, 4), (uint)specs.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40, 4), (uint)indexPosition);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44, 4), (uint)recordSize);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(64, 8), (ulong)indexPosition);

        var index = new byte[recordSize];
        BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(0, 4), 0u);
        var nextPosition = resourcePosition;

        using var stream = File.Create(path);
        stream.Write(header);
        stream.Write(index);

        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var entry = index.AsSpan(flagsSize + (i * indexEntrySize), indexEntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(0, 4), spec.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(4, 4), spec.Group);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(8, 4), (uint)(spec.Instance >> 32));
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(12, 4), (uint)(spec.Instance & 0xFFFFFFFF));
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(16, 4), (uint)nextPosition);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(20, 4), (uint)spec.StoredBytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(24, 4), (uint)spec.StoredBytes.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(28, 2), 0);
            nextPosition += spec.StoredBytes.Length;
        }

        stream.Position = headerSize;
        stream.Write(index);
        foreach (var spec in specs)
        {
            stream.Write(spec.StoredBytes);
        }
    }

    private sealed record PackageSpec(
        uint Type,
        uint Group,
        ulong Instance,
        byte[] StoredBytes);

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
