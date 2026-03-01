using System.Buffers.Binary;
using System.IO.Compression;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.PackageCore.Tests;

public sealed class DbpfPackageCoreTests
{
    [Fact]
    public void DbpfPackageIndexReader_ReadsStandardPackageAndBuildsTypeBuckets()
    {
        using var fixture = new TempDirectory("dbpf-index");
        var packagePath = Path.Combine(fixture.Path, "sample.package");
        WritePackage(
            packagePath,
            [
                new PackageSpec(0x12345678, 0x1, 0x1000000000000001, [1, 2, 3, 4]),
                new PackageSpec(0x12345678, 0x2, 0x1000000000000002, [5, 6, 7, 8])
            ]);

        var package = DbpfPackageIndexReader.ReadPackageIndex(packagePath);

        Assert.Equal(2, package.Entries.Length);
        Assert.True(package.TypeBuckets.TryGetValue(0x12345678, out var bucket));
        Assert.NotNull(bucket);
        Assert.Equal(2, bucket!.InstanceToEntryIndexes.Count);
        Assert.Equal(0x1u, package.Entries[0].Group);
        Assert.Equal(4, package.Entries[0].CompressedSize);
    }

    [Fact]
    public void DbpfResourceReader_ReadsRawAndZlibResources()
    {
        using var fixture = new TempDirectory("dbpf-read");
        var packagePath = Path.Combine(fixture.Path, "resources.package");
        var rawBytes = new byte[] { 1, 2, 3, 4 };
        var zlibSource = new byte[] { 9, 8, 7, 6, 5, 4 };

        WritePackage(
            packagePath,
            [
                new PackageSpec(0x10, 0, 0x2000000000000001, rawBytes),
                new PackageSpec(0x20, 0, 0x2000000000000002, CompressZlib(zlibSource), zlibSource.Length, 23106)
            ]);

        var package = DbpfPackageIndexReader.ReadPackageIndex(packagePath);
        var reader = new DbpfResourceReader();
        using var session = reader.OpenSession(packagePath);

        Assert.True(session.TryReadBytes(package.Entries[0], out var raw, out var rawError));
        Assert.Null(rawError);
        Assert.Equal(rawBytes, raw);

        Assert.True(session.TryReadBytes(package.Entries[1], out var zlib, out var zlibError));
        Assert.Null(zlibError);
        Assert.Equal(zlibSource, zlib);
    }

    [Fact]
    public async Task DbpfPackageCatalog_ReusesPersistentCache_WhenFingerprintMatches()
    {
        using var fixture = new TempDirectory("dbpf-cache");
        var packagePath = Path.Combine(fixture.Path, "cached.package");
        var cachePath = Path.Combine(fixture.Path, "catalog.bin");
        var expectedType = 0xABCDEF12u;

        WritePackage(packagePath, [new PackageSpec(expectedType, 0, 0x3000000000000001, [1, 3, 5, 7])]);
        var originalWriteUtc = File.GetLastWriteTimeUtc(packagePath);

        var catalog = new DbpfPackageCatalog();
        var first = await catalog.BuildSnapshotAsync(
            fixture.Path,
            new DbpfCatalogBuildOptions
            {
                CacheFilePath = cachePath
            });

        var originalBytes = File.ReadAllBytes(packagePath);
        var replacement = new byte[originalBytes.Length];
        File.WriteAllBytes(packagePath, replacement);
        File.SetLastWriteTimeUtc(packagePath, originalWriteUtc);

        var second = await catalog.BuildSnapshotAsync(
            fixture.Path,
            new DbpfCatalogBuildOptions
            {
                CacheFilePath = cachePath
            });

        Assert.Single(first.Packages);
        Assert.Single(second.Packages);
        Assert.Equal(expectedType, first.Packages[0].Entries[0].Type);
        Assert.Equal(expectedType, second.Packages[0].Entries[0].Type);
        Assert.Empty(second.Issues);
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
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(24, 4), (uint)spec.ActualUncompressedSize);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(28, 2), spec.CompressionType);
            nextPosition += spec.StoredBytes.Length;
        }

        stream.Position = headerSize;
        stream.Write(index);
        foreach (var spec in specs)
        {
            stream.Write(spec.StoredBytes);
        }
    }

    private static byte[] CompressZlib(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(bytes);
        }

        return output.ToArray();
    }

    private sealed record PackageSpec(
        uint Type,
        uint Group,
        ulong Instance,
        byte[] StoredBytes,
        int? ExplicitUncompressedSize = null,
        ushort CompressionType = 0)
    {
        public int ActualUncompressedSize { get; } = ExplicitUncompressedSize ?? StoredBytes.Length;
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
