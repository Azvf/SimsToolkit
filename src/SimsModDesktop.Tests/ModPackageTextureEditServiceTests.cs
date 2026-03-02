using System.Buffers.Binary;
using System.IO.Compression;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Infrastructure.TextureProcessing;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Tests;

public sealed class ModPackageTextureEditServiceTests
{
    [Fact]
    public async Task ApplySuggestedEditAsync_PreservesUntouchedStoredPayloadAndCompression()
    {
        using var temp = new TempDirectory();
        var packagePath = Path.Combine(temp.Path, "sample.package");
        var targetStoredBytes = new byte[] { 1, 2, 3, 4 };
        var untouchedSourceBytes = new byte[] { 9, 8, 7, 6, 5 };
        var untouchedStoredBytes = CompressZlib(untouchedSourceBytes);

        WritePackage(
            packagePath,
            [
                new PackageSpec(0x3453CF95, 0x0, 0x1000000000000001, targetStoredBytes),
                new PackageSpec(0x00B2D882, 0x0, 0x1000000000000002, untouchedStoredBytes, untouchedSourceBytes.Length, 23106)
            ]);

        var store = new SqliteModPackageTextureEditStore(temp.Path);
        var service = new ModPackageTextureEditService(
            new FakeTextureDecodeService(),
            new FakeTextureTranscodePipeline([7, 7, 7, 7, 7, 7, 7, 7]),
            store);

        var result = await service.ApplySuggestedEditAsync(
            packagePath,
            new ModPackageTextureCandidate
            {
                ResourceKeyText = "3453CF95:00000000:1000000000000001",
                ContainerKind = "PNG",
                Format = "PNG(RGB)",
                Width = 8,
                Height = 8,
                MipMapCount = 1,
                Editable = true,
                SuggestedAction = "ConvertToBC1",
                Notes = "test",
                SizeBytes = targetStoredBytes.Length
            });

        Assert.True(result.Success);

        var package = DbpfPackageIndexReader.ReadPackageIndex(packagePath);
        Assert.Equal(2, package.Entries.Length);
        Assert.Equal(0, package.Entries[0].CompressionType);
        Assert.Equal(23106, package.Entries[1].CompressionType);
        Assert.Equal(untouchedStoredBytes.Length, package.Entries[1].CompressedSize);
        Assert.Equal(untouchedSourceBytes.Length, package.Entries[1].UncompressedSize);

        Assert.Equal([7, 7, 7, 7, 7, 7, 7, 7], ReadStoredBytes(packagePath, package.Entries[0]));
        Assert.Equal(untouchedStoredBytes, ReadStoredBytes(packagePath, package.Entries[1]));

        var history = await store.GetHistoryAsync(packagePath, "3453CF95:00000000:1000000000000001");
        Assert.Single(history);
        Assert.Equal(targetStoredBytes, history[0].OriginalBytes);
    }

    private static byte[] ReadStoredBytes(string packagePath, DbpfIndexEntry entry)
    {
        using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var payload = new byte[entry.CompressedSize];
        stream.Seek(entry.DataOffset, SeekOrigin.Begin);
        FillExactly(stream, payload);
        return payload;
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

    private static void FillExactly(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected end of stream.");
            }

            total += read;
        }
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
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SimsToolkit_ModPackageTextureEditService_" + Guid.NewGuid().ToString("N"));
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

    private sealed class FakeTextureDecodeService : ITextureDecodeService
    {
        public bool TryDecode(
            TextureContainerKind containerKind,
            ReadOnlyMemory<byte> sourceBytes,
            out TexturePixelBuffer pixelBuffer,
            out string error)
        {
            pixelBuffer = new TexturePixelBuffer
            {
                Width = 4,
                Height = 4,
                Layout = TexturePixelLayout.Rgba32,
                PixelBytes = new byte[4 * 4 * 4]
            };
            error = string.Empty;
            return true;
        }
    }

    private sealed class FakeTextureTranscodePipeline : ITextureTranscodePipeline
    {
        private readonly byte[] _encodedBytes;

        public FakeTextureTranscodePipeline(byte[] encodedBytes)
        {
            _encodedBytes = encodedBytes;
        }

        public TextureTranscodeResult Transcode(TextureTranscodeRequest request)
        {
            return new TextureTranscodeResult
            {
                Success = true,
                EncodedBytes = _encodedBytes,
                OutputFormat = request.TargetFormat,
                OutputWidth = request.TargetWidth,
                OutputHeight = request.TargetHeight,
                MipMapCount = request.GenerateMipMaps ? 1 : 0
            };
        }
    }
}
