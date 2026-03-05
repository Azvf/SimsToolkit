using System.Buffers.Binary;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Tests;

public sealed class ModPackageTextureAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeResultAsync_ReportsUnsupportedTextureTypes()
    {
        using var fixture = new TempDirectory("texture-analysis");
        var packagePath = Path.Combine(fixture.Path, "unsupported.package");
        WritePackage(
            packagePath,
            [
                new PackageSpec(0x2F7D0004u, 0, 0x1000000000000001, [1, 2, 3, 4]), // DST
                new PackageSpec(0x3453CF95u, 0, 0x1000000000000002, [5, 6, 7, 8])  // RLE2
            ]);

        var service = new ModPackageTextureAnalysisService(new InMemoryAnalysisStore(), new DbpfResourceReader());

        var result = await service.AnalyzeResultAsync(packagePath);

        Assert.Equal(2, result.Summary.TextureResourceCount);
        Assert.Equal(2, result.Summary.UnsupportedTextureCount);
        Assert.Equal(0, result.Summary.EditableTextureCount);
        Assert.Equal(2, result.Candidates.Count);
        Assert.All(result.Candidates, candidate =>
        {
            Assert.Equal("Unsupported", candidate.Format);
            Assert.False(candidate.Editable);
            Assert.Equal("Skip", candidate.SuggestedAction);
        });
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

    private sealed record PackageSpec(uint Type, uint Group, ulong Instance, byte[] StoredBytes);

    private sealed class InMemoryAnalysisStore : IModPackageTextureAnalysisStore
    {
        public Task<ModPackageTextureAnalysisResult?> TryGetAsync(
            string packagePath,
            long fileLength,
            long lastWriteUtcTicks,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ModPackageTextureAnalysisResult?>(null);
        }

        public Task SaveAsync(ModPackageTextureAnalysisResult analysis, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
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

