using System.Buffers.Binary;
using SimsModDesktop.PackageCore;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.TrayDependencyEngine.Tests;

public sealed class TrayDependencyEngineCoreTests
{
    private const ulong FirstInstance = 0x1111111111111111;
    private const ulong SecondInstance = 0x2222222222222222;
    private const uint SupportedResourceType = 55242443u;

    [Fact]
    public async Task PackageIndexCache_ReusesSnapshot_WhenPackagesAreUnchanged()
    {
        using var fixture = new TempDirectory("tray-cache");
        var packagePath = Path.Combine(fixture.Path, "sample.package");
        WritePackage(packagePath, [new PackageSpec(SupportedResourceType, 0, FirstInstance, [1, 2, 3, 4])]);

        var cache = new PackageIndexCache();
        var first = await BuildSnapshotAsync(cache, fixture.Path);
        var second = await cache.TryLoadSnapshotAsync(fixture.Path, first.InventoryVersion);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task TrayDependencyExportService_ReusesSingleReadSession_PerPackageDuringExpansion()
    {
        using var trayRoot = new TempDirectory("tray-session-tray");
        using var modsRoot = new TempDirectory("tray-session-mods");
        using var exportRoot = new TempDirectory("tray-session-out");

        var trayItemPath = Path.Combine(trayRoot.Path, "0xsession.trayitem");
        WriteTrayItem(trayItemPath, FirstInstance, SecondInstance);

        WritePackage(
            Path.Combine(modsRoot.Path, "dual.package"),
            [
                new PackageSpec(SupportedResourceType, 0, FirstInstance, [1, 2, 3, 4]),
                new PackageSpec(SupportedResourceType, 0, SecondInstance, [5, 6, 7, 8])
            ]);

        var packageIndexCache = new PackageIndexCache();
        var preloadedSnapshot = await BuildSnapshotAsync(packageIndexCache, modsRoot.Path);
        var countingReader = new CountingResourceReader();
        var service = new TrayDependencyExportService(packageIndexCache, countingReader);
        var request = new TrayDependencyExportRequest
        {
            ItemTitle = "Session",
            TrayItemKey = "0xsession",
            TrayRootPath = trayRoot.Path,
            TraySourceFiles = [trayItemPath],
            ModsRootPath = modsRoot.Path,
            TrayExportRoot = Path.Combine(exportRoot.Path, "Session_0xsession", "Tray"),
            ModsExportRoot = Path.Combine(exportRoot.Path, "Session_0xsession", "Mods"),
            PreloadedSnapshot = preloadedSnapshot
        };

        var result = await service.ExportAsync(request);

        Assert.True(result.Success);
        Assert.Equal(1, countingReader.OpenCount);
        Assert.Equal(1, result.CopiedModFileCount);
    }

    private static void WriteTrayItem(string path, params ulong[] instances)
    {
        using var stream = File.Create(path);
        Span<byte> buffer = stackalloc byte[8];
        foreach (var instance in instances)
        {
            stream.WriteByte(0x09);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer, instance);
            stream.Write(buffer);
        }
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
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var entry = index.AsSpan(flagsSize + (i * indexEntrySize), indexEntrySize);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(0, 4), spec.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(4, 4), spec.Group);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(8, 4), (uint)(spec.Instance >> 32));
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(12, 4), (uint)(spec.Instance & 0xFFFFFFFF));
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(16, 4), (uint)nextPosition);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(20, 4), (uint)spec.ResourceData.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(24, 4), (uint)spec.ResourceData.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(28, 2), 0);
            nextPosition += spec.ResourceData.Length;
        }

        using var stream = File.Create(path);
        stream.Write(header);
        stream.Write(index);
        foreach (var spec in specs)
        {
            stream.Write(spec.ResourceData);
        }
    }

    private static PackageIndexBuildRequest CreateBuildRequest(string modsRootPath)
    {
        var normalizedRoot = Path.GetFullPath(modsRootPath.Trim());
        var packageFiles = Directory.EnumerateFiles(normalizedRoot, "*.package", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new PackageIndexBuildFile
                {
                    FilePath = path,
                    Length = info.Length,
                    LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks
                };
            })
            .ToArray();

        return new PackageIndexBuildRequest
        {
            ModsRootPath = normalizedRoot,
            InventoryVersion = ComputeInventoryVersion(packageFiles),
            PackageFiles = packageFiles
        };
    }

    private static async Task<PackageIndexSnapshot> BuildSnapshotAsync(PackageIndexCache cache, string modsRootPath)
    {
        return await cache.BuildSnapshotAsync(CreateBuildRequest(modsRootPath));
    }

    private static long ComputeInventoryVersion(IReadOnlyList<PackageIndexBuildFile> packageFiles)
    {
        unchecked
        {
            long hash = 17;
            for (var i = 0; i < packageFiles.Count; i++)
            {
                var item = packageFiles[i];
                hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(item.FilePath);
                hash = (hash * 31) + item.Length.GetHashCode();
                hash = (hash * 31) + item.LastWriteUtcTicks.GetHashCode();
            }

            hash &= long.MaxValue;
            return hash == 0 ? 1 : hash;
        }
    }

    private sealed record PackageSpec(uint Type, uint Group, ulong Instance, byte[] ResourceData);

    private sealed class CountingResourceReader : IDbpfResourceReader
    {
        private readonly DbpfResourceReader _inner = new();

        public int OpenCount { get; private set; }

        public DbpfPackageReadSession OpenSession(string packagePath)
        {
            OpenCount++;
            return _inner.OpenSession(packagePath);
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
