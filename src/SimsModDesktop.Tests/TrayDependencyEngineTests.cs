using System.Buffers.Binary;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Tests;

public sealed class TrayDependencyEngineTests
{
    [Fact]
    public async Task PackageIndexCache_ReusesSnapshot_WhenPackagesAreUnchanged()
    {
        using var modsRoot = new TempDirectory("mods-cache");
        var packagePath = Path.Combine(modsRoot.Path, "sample.package");
        WritePackage(packagePath, KnownInstance, KnownResourceType, group: 0, resourceData: [1, 2, 3, 4]);

        var cache = new PackageIndexCache();
        var first = await cache.GetSnapshotAsync(modsRoot.Path);
        var second = await cache.GetSnapshotAsync(modsRoot.Path);

        Assert.Same(first, second);

        File.SetLastWriteTimeUtc(packagePath, DateTime.UtcNow.AddSeconds(5));
        var third = await cache.GetSnapshotAsync(modsRoot.Path);

        Assert.NotSame(first, third);
        Assert.Single(third.Packages);
    }

    [Fact]
    public async Task PackageIndexCache_ReadsPackagesWithMultipleIndexEntries()
    {
        using var modsRoot = new TempDirectory("mods-multi");
        var packagePath = Path.Combine(modsRoot.Path, "multi.package");
        WritePackage(
            packagePath,
            [
                new PackageSpec(KnownResourceType, 0, KnownInstance, [1, 2, 3, 4]),
                new PackageSpec(KnownResourceType, 1, KnownInstance + 1, [5, 6, 7, 8])
            ]);

        var cache = new PackageIndexCache();
        var snapshot = await cache.GetSnapshotAsync(modsRoot.Path);

        Assert.Single(snapshot.Packages);
        Assert.Equal(2, snapshot.Packages[0].Entries.Length);
    }

    [Fact]
    public async Task PackageIndexCache_ReadsPackages_WhenHeaderStoresPerEntryRecordSize()
    {
        using var modsRoot = new TempDirectory("mods-per-entry");
        var packagePath = Path.Combine(modsRoot.Path, "per-entry.package");
        WritePackage(
            packagePath,
            [
                new PackageSpec(KnownResourceType, 0, KnownInstance, [1, 2, 3, 4]),
                new PackageSpec(KnownResourceType, 1, KnownInstance + 1, [5, 6, 7, 8])
            ]);

        PatchHeaderIndexRecordSize(packagePath, 32u);

        var cache = new PackageIndexCache();
        var snapshot = await cache.GetSnapshotAsync(modsRoot.Path);

        Assert.Single(snapshot.Packages);
        Assert.Equal(2, snapshot.Packages[0].Entries.Length);
    }

    [Fact]
    public async Task TrayDependencyExportService_ExportsTrayAndMatchedMods_WithoutS4ti()
    {
        using var trayRoot = new TempDirectory("tray-export");
        using var modsRoot = new TempDirectory("mods-export");
        using var exportRoot = new TempDirectory("out-export");

        var trayItemPath = Path.Combine(trayRoot.Path, "0x1.trayitem");
        WriteTrayItem(trayItemPath, KnownInstance);

        var packagePath = Path.Combine(modsRoot.Path, "resolved.package");
        WritePackage(packagePath, KnownInstance, KnownResourceType, group: 0, resourceData: [9, 9, 9, 9]);

        var service = new TrayDependencyExportService(new PackageIndexCache());
        var request = new TrayDependencyExportRequest
        {
            ItemTitle = "Sample",
            TrayItemKey = "0x1",
            TrayRootPath = trayRoot.Path,
            TraySourceFiles = [trayItemPath],
            ModsRootPath = modsRoot.Path,
            TrayExportRoot = Path.Combine(exportRoot.Path, "Sample_0x1", "Tray"),
            ModsExportRoot = Path.Combine(exportRoot.Path, "Sample_0x1", "Mods")
        };

        var progressEvents = new List<TrayDependencyExportProgress>();
        var result = await service.ExportAsync(
            request,
            new Progress<TrayDependencyExportProgress>(progress => progressEvents.Add(progress)));

        Assert.True(result.Success);
        Assert.Equal(1, result.CopiedTrayFileCount);
        Assert.Equal(1, result.CopiedModFileCount);
        Assert.Contains(progressEvents, progress => progress.Stage == TrayDependencyExportStage.IndexingPackages);
        Assert.Contains(progressEvents, progress => progress.Stage == TrayDependencyExportStage.Completed && progress.Percent == 100);
        Assert.True(File.Exists(Path.Combine(request.TrayExportRoot, "0x1.trayitem")));
        Assert.True(File.Exists(Path.Combine(request.ModsExportRoot, "resolved.package")));
    }

    [Fact]
    public async Task TrayDependencyExportService_RunsExportPipelineOffCallerThread()
    {
        using var trayRoot = new TempDirectory("tray-export-async");
        using var exportRoot = new TempDirectory("out-export-async");

        var trayItemPath = Path.Combine(trayRoot.Path, "0x1.trayitem");
        WriteTrayItem(trayItemPath, KnownInstance);

        var gate = new TaskCompletionSource<PackageIndexSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new TrayDependencyExportService(new BlockingPackageIndexCache(gate.Task));
        var request = new TrayDependencyExportRequest
        {
            ItemTitle = "Sample",
            TrayItemKey = "0x1",
            TrayRootPath = trayRoot.Path,
            TraySourceFiles = [trayItemPath],
            ModsRootPath = trayRoot.Path,
            TrayExportRoot = Path.Combine(exportRoot.Path, "Sample_0x1", "Tray"),
            ModsExportRoot = Path.Combine(exportRoot.Path, "Sample_0x1", "Mods")
        };

        var exportTask = service.ExportAsync(request);

        Assert.False(exportTask.IsCompleted);

        gate.SetResult(new PackageIndexSnapshot
        {
            ModsRootPath = trayRoot.Path,
            Packages = Array.Empty<IndexedPackageFile>()
        });

        var result = await exportTask;

        Assert.True(result.Success);
        Assert.Equal(1, result.CopiedTrayFileCount);
    }

    [Fact]
    public async Task TrayDependencyExportService_ExpandsCasPartStructuredDependencies()
    {
        using var trayRoot = new TempDirectory("tray-caspart");
        using var modsRoot = new TempDirectory("mods-caspart");
        using var exportRoot = new TempDirectory("out-caspart");

        const ulong rootInstance = 0x1000000000000001;
        const ulong nestedTextureInstance = 0x1000000000000002;
        var trayItemPath = Path.Combine(trayRoot.Path, "0xcas.trayitem");
        WriteTrayItem(trayItemPath, rootInstance);

        WritePackage(
            Path.Combine(modsRoot.Path, "caspart.package"),
            rootInstance,
            KnownResourceType,
            group: 0,
            resourceData: BuildResourceKeyBytes(TextureCompositorType, 0, nestedTextureInstance));
        WritePackage(
            Path.Combine(modsRoot.Path, "texture.package"),
            nestedTextureInstance,
            TextureCompositorType,
            group: 0,
            resourceData: [1, 2, 3, 4]);

        var service = new TrayDependencyExportService(new PackageIndexCache());
        var request = CreateRequest("CasPart", "0xcas", trayRoot.Path, trayItemPath, modsRoot.Path, exportRoot.Path);

        var result = await service.ExportAsync(request);

        Assert.True(result.Success);
        Assert.Equal(2, result.CopiedModFileCount);
        Assert.True(File.Exists(Path.Combine(request.ModsExportRoot, "caspart.package")));
        Assert.True(File.Exists(Path.Combine(request.ModsExportRoot, "texture.package")));
    }

    [Fact]
    public async Task TrayDependencyExportService_ExpandsSkinToneStructuredDependencies()
    {
        using var trayRoot = new TempDirectory("tray-skintone");
        using var modsRoot = new TempDirectory("mods-skintone");
        using var exportRoot = new TempDirectory("out-skintone");

        const ulong rootInstance = 0x2000000000000001;
        const ulong nestedMaterialInstance = 0x2000000000000002;
        var trayItemPath = Path.Combine(trayRoot.Path, "0xskin.trayitem");
        WriteTrayItem(trayItemPath, rootInstance);

        WritePackage(
            Path.Combine(modsRoot.Path, "skintone.package"),
            rootInstance,
            SkinToneType,
            group: 0,
            resourceData: BuildIdBytes(nestedMaterialInstance));
        WritePackage(
            Path.Combine(modsRoot.Path, "material.package"),
            nestedMaterialInstance,
            MaterialDefinitionType,
            group: 0,
            resourceData: [9, 8, 7, 6]);

        var service = new TrayDependencyExportService(new PackageIndexCache());
        var request = CreateRequest("SkinTone", "0xskin", trayRoot.Path, trayItemPath, modsRoot.Path, exportRoot.Path);

        var result = await service.ExportAsync(request);

        Assert.True(result.Success);
        Assert.Equal(2, result.CopiedModFileCount);
        Assert.True(File.Exists(Path.Combine(request.ModsExportRoot, "skintone.package")));
        Assert.True(File.Exists(Path.Combine(request.ModsExportRoot, "material.package")));
    }

    [Fact]
    public async Task TrayDependencyExportService_ExpandsObjectDefinitionStructuredDependencies()
    {
        using var trayRoot = new TempDirectory("tray-objectdef");
        using var modsRoot = new TempDirectory("mods-objectdef");
        using var exportRoot = new TempDirectory("out-objectdef");

        const ulong rootInstance = 0x3000000000000001;
        const ulong nestedImageInstance = 0x3000000000000002;
        const uint group = 7;
        var trayItemPath = Path.Combine(trayRoot.Path, "0xobj.trayitem");
        WriteTrayItem(trayItemPath, rootInstance);

        WritePackage(
            Path.Combine(modsRoot.Path, "objectdef.package"),
            rootInstance,
            ObjectDefinitionType,
            group,
            resourceData: BuildResourceKeyBytes(ImageResourceType, group, nestedImageInstance));
        WritePackage(
            Path.Combine(modsRoot.Path, "image.package"),
            nestedImageInstance,
            ImageResourceType,
            group,
            resourceData: [4, 3, 2, 1]);

        var service = new TrayDependencyExportService(new PackageIndexCache());
        var request = CreateRequest("ObjectDef", "0xobj", trayRoot.Path, trayItemPath, modsRoot.Path, exportRoot.Path);

        var result = await service.ExportAsync(request);

        Assert.True(result.Success);
        Assert.Equal(2, result.CopiedModFileCount);
        Assert.True(File.Exists(Path.Combine(request.ModsExportRoot, "objectdef.package")));
        Assert.True(File.Exists(Path.Combine(request.ModsExportRoot, "image.package")));
    }

    [Fact]
    public async Task TrayDependencyAnalysisService_AnalyzesAndExportsMatchedPackages_WithoutS4ti()
    {
        using var trayRoot = new TempDirectory("tray-analysis");
        using var modsRoot = new TempDirectory("mods-analysis");
        using var outputRoot = new TempDirectory("out-analysis");

        var trayItemPath = Path.Combine(trayRoot.Path, "0x1.trayitem");
        WriteTrayItem(trayItemPath, KnownInstance);

        var packagePath = Path.Combine(modsRoot.Path, "resolved.package");
        WritePackage(packagePath, KnownInstance, KnownResourceType, group: 0, resourceData: [1, 2, 3, 4]);

        var service = new TrayDependencyAnalysisService(new PackageIndexCache());
        var request = new TrayDependencyAnalysisRequest
        {
            TrayPath = trayRoot.Path,
            ModsRootPath = modsRoot.Path,
            TrayItemKey = "0x1",
            OutputCsv = Path.Combine(outputRoot.Path, "matched.csv"),
            ExportMatchedPackages = true,
            ExportTargetPath = Path.Combine(outputRoot.Path, "exports"),
            ExportMinConfidence = "Low"
        };

        var progressEvents = new List<TrayDependencyAnalysisProgress>();
        var result = await service.AnalyzeAsync(
            request,
            new Progress<TrayDependencyAnalysisProgress>(progress => progressEvents.Add(progress)));

        Assert.True(result.Success);
        Assert.Equal(1, result.MatchedPackageCount);
        Assert.Equal(1, result.ExportedMatchedPackageCount);
        Assert.Equal(Path.GetFullPath(request.OutputCsv), result.OutputCsvPath);
        Assert.Contains(progressEvents, progress => progress.Stage == TrayDependencyAnalysisStage.IndexingPackages);
        Assert.Contains(progressEvents, progress => progress.Stage == TrayDependencyAnalysisStage.Completed && progress.Percent == 100);
        Assert.True(File.Exists(result.OutputCsvPath));
        Assert.True(File.Exists(Path.Combine(result.MatchedExportPath!, "resolved.package")));
    }

    private static void WriteTrayItem(string path, ulong instance)
    {
        var bytes = new byte[9];
        bytes[0] = 0x09; // field 1, wire type 1
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(1, 8), instance);
        File.WriteAllBytes(path, bytes);
    }

    private static void WritePackage(string path, ulong instance, uint type, uint group, byte[] resourceData)
    {
        WritePackage(path, [new PackageSpec(type, group, instance, resourceData)]);
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
        var resourceBlocks = new List<byte[]>(specs.Count);
        foreach (var spec in specs)
        {
            resourceBlocks.Add(spec.ResourceData);
        }

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
        foreach (var resourceBlock in resourceBlocks)
        {
            stream.Write(resourceBlock);
        }
    }

    private static void PatchHeaderIndexRecordSize(string path, uint value)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        stream.Position = 44;
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static TrayDependencyExportRequest CreateRequest(
        string itemTitle,
        string trayItemKey,
        string trayRootPath,
        string trayItemPath,
        string modsRootPath,
        string exportRoot)
    {
        return new TrayDependencyExportRequest
        {
            ItemTitle = itemTitle,
            TrayItemKey = trayItemKey,
            TrayRootPath = trayRootPath,
            TraySourceFiles = [trayItemPath],
            ModsRootPath = modsRootPath,
            TrayExportRoot = Path.Combine(exportRoot, $"{itemTitle}_{trayItemKey}", "Tray"),
            ModsExportRoot = Path.Combine(exportRoot, $"{itemTitle}_{trayItemKey}", "Mods")
        };
    }

    private static byte[] BuildResourceKeyBytes(uint type, uint group, ulong instance)
    {
        var data = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), type);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4, 4), group);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8, 8), instance);
        return data;
    }

    private static byte[] BuildIdBytes(ulong instance)
    {
        var data = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(data, instance);
        return data;
    }

    private const ulong KnownInstance = 0x1122334455667788;
    private const uint KnownResourceType = 55242443u;
    private const uint SkinToneType = 55867754u;
    private const uint TextureCompositorType = 3066607264u;
    private const uint ImageResourceType = 877907861u;
    private const uint MaterialDefinitionType = 734023391u;
    private const uint ObjectDefinitionType = 3235601127u;

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

    private sealed record PackageSpec(uint Type, uint Group, ulong Instance, byte[] ResourceData);

    private sealed class BlockingPackageIndexCache : IPackageIndexCache
    {
        private readonly Task<PackageIndexSnapshot> _snapshotTask;

        public BlockingPackageIndexCache(Task<PackageIndexSnapshot> snapshotTask)
        {
            _snapshotTask = snapshotTask;
        }

        public Task<PackageIndexSnapshot> GetSnapshotAsync(
            string modsRootPath,
            IProgress<TrayDependencyExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return _snapshotTask;
        }
    }
}
