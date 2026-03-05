using System.Buffers.Binary;
using System.Text;
using EA.Sims4.Persistence;
using ProtoBuf;
using SimsModDesktop.PackageCore;
using SimsModDesktop.SaveData.Services;

namespace SimsModDesktop.Tests;

public sealed class SaveAppearanceLinkServiceTests
{
    [Fact]
    public async Task BuildSnapshotAsync_BuildsOutfitCaspAndMorphSummaries()
    {
        using var fixture = new TempDirectory("save-appearance-full");
        var savePath = Path.Combine(fixture.Path, "slot_00000001.save");
        var modsPath = Path.Combine(fixture.Path, "Mods");
        var gamePath = Path.Combine(fixture.Path, "Game");
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(gamePath);

        const ulong partA = 0x1000000000000001;
        const ulong partB = 0x1000000000000002;
        const ulong smodInstance = 0x5000000000000001;
        const ulong sculptInstance = 0x5000000000000002;
        const ulong bgeoInstance = 0x6000000000000001;
        const ulong dmapShapeInstance = 0x6000000000000002;
        const ulong dmapNormalInstance = 0x6000000000000003;
        const ulong bondInstance = 0x6000000000000004;

        WritePackageWithSingleResource(
            Path.Combine(modsPath, "part-a.package"),
            Sims4ResourceTypeRegistry.CasPart,
            0xA0000001,
            partA,
            BuildCaspFixture(
                diffuseInstance: 0x7100000000000001,
                normalInstance: 0x7100000000000002,
                specularInstance: 0x7100000000000003,
                meshInstance: 0x7200000000000001,
                regionMapInstance: 0x7300000000000001));
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "part-b.package"),
            Sims4ResourceTypeRegistry.CasPart,
            0xA0000002,
            partB,
            BuildCaspFixture(
                diffuseInstance: 0x7400000000000001,
                normalInstance: 0x7400000000000002,
                specularInstance: 0x7400000000000003,
                meshInstance: 0x7500000000000001,
                regionMapInstance: 0x7600000000000001));

        WritePackageWithSingleResource(
            Path.Combine(modsPath, "morph-smod.package"),
            Sims4ResourceTypeRegistry.SimModifier,
            0,
            smodInstance,
            BuildSmodFixture(bgeoInstance, dmapShapeInstance, dmapNormalInstance, bondInstance));
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "morph-sculpt.package"),
            Sims4ResourceTypeRegistry.Sculpt,
            0,
            sculptInstance,
            BuildSculptFixture(bgeoInstance, dmapShapeInstance, dmapNormalInstance, bondInstance));
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "morph-bgeo.package"),
            Sims4ResourceTypeRegistry.BlendGeometry,
            0,
            bgeoInstance,
            BuildBgeoHeaderFixture());
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "morph-dmap-shape.package"),
            Sims4ResourceTypeRegistry.DeformerMap,
            0,
            dmapShapeInstance,
            BuildDmapHeaderFixture());
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "morph-dmap-normal.package"),
            Sims4ResourceTypeRegistry.DeformerMap,
            0,
            dmapNormalInstance,
            BuildDmapHeaderFixture());
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "morph-bond.package"),
            Sims4ResourceTypeRegistry.BoneDelta,
            0,
            bondInstance,
            BuildBondHeaderFixture());

        var saveData = new SaveGameData();
        var sim = new SimData
        {
            sim_id = 0x1111,
            first_name = "Alex",
            last_name = "Tester",
            extended_species = 1,
            outfits = new OutfitList(),
            facial_attr = BuildMorphPayload(smodInstance, sculptInstance)
        };
        sim.outfits.outfits.Add(new OutfitData
        {
            outfit_id = 0xAAAA,
            category = 0,
            outfit_flags = 0x10,
            created = 12345,
            parts = new EA.Sims4.IdList { ids = [partA, partB] },
            body_types_list = new BodyTypesList { body_types = [5, 6] },
            part_shifts = new ColorShiftList { color_shift = [0x100, 0x200] }
        });
        saveData.sims.Add(sim);

        WriteSavePackage(savePath, saveData);

        var service = new SaveAppearanceLinkService();
        var snapshot = await service.BuildSnapshotAsync(savePath, gamePath, modsPath);

        var simResult = Assert.Single(snapshot.Sims);
        Assert.Equal(0x1111UL, simResult.SimId);
        Assert.Equal("Alex Tester", simResult.FullName);
        Assert.Single(simResult.Outfits);

        var outfit = simResult.Outfits[0];
        Assert.Equal(2, outfit.Parts.Count);
        var partAResult = outfit.Parts[0];
        Assert.NotNull(partAResult.CasPart);
        Assert.NotNull(partAResult.ResolvedCasPartKey);
        Assert.NotEmpty(partAResult.TextureRefs);

        Assert.NotEmpty(snapshot.MorphGraphSummary.SimModifierLinks);
        Assert.NotEmpty(snapshot.MorphGraphSummary.SculptLinks);
        Assert.NotEmpty(snapshot.MorphGraphSummary.ReferencedResources);
        Assert.All(snapshot.MorphGraphSummary.ReferencedResources, health => Assert.True(health.Exists));
        Assert.Contains(snapshot.MorphGraphSummary.ReferencedResources, health => health.Kind == Ts4MorphReferencedResourceKind.BlendGeometry && health.HeaderParsed);
        Assert.Contains(snapshot.MorphGraphSummary.ReferencedResources, health => health.Kind == Ts4MorphReferencedResourceKind.DeformerMap && health.HeaderParsed);
        Assert.Contains(snapshot.MorphGraphSummary.ReferencedResources, health => health.Kind == Ts4MorphReferencedResourceKind.BoneDelta && health.HeaderParsed);
        Assert.True(snapshot.ResourceStats.TotalReferences > 0);
    }

    [Fact]
    public async Task BuildSnapshotAsync_MissingCasp_ReportsResourceNotFound()
    {
        using var fixture = new TempDirectory("save-appearance-missing-casp");
        var savePath = Path.Combine(fixture.Path, "slot_00000002.save");
        var modsPath = Path.Combine(fixture.Path, "Mods");
        var gamePath = Path.Combine(fixture.Path, "Game");
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(gamePath);

        var saveData = new SaveGameData();
        var sim = new SimData
        {
            sim_id = 0x2222,
            first_name = "Missing",
            last_name = "Part",
            extended_species = 1,
            outfits = new OutfitList()
        };
        sim.outfits.outfits.Add(new OutfitData
        {
            outfit_id = 0xBBBB,
            category = 0,
            parts = new EA.Sims4.IdList { ids = [0x9900000000000001] },
            body_types_list = new BodyTypesList { body_types = [5] },
            part_shifts = new ColorShiftList { color_shift = [0] }
        });
        saveData.sims.Add(sim);
        WriteSavePackage(savePath, saveData);

        var service = new SaveAppearanceLinkService();
        var snapshot = await service.BuildSnapshotAsync(savePath, gamePath, modsPath);

        Assert.Contains(snapshot.Issues, issue =>
            issue.Code == "RESOURCE_NOT_FOUND" &&
            issue.Scope == Ts4AppearanceIssueScope.Part);
    }

    [Fact]
    public async Task BuildSnapshotAsync_InvalidCasp_ReportsParserFailed()
    {
        using var fixture = new TempDirectory("save-appearance-invalid-casp");
        var savePath = Path.Combine(fixture.Path, "slot_00000003.save");
        var modsPath = Path.Combine(fixture.Path, "Mods");
        var gamePath = Path.Combine(fixture.Path, "Game");
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(gamePath);

        const ulong partId = 0x8800000000000001;
        WritePackageWithSingleResource(
            Path.Combine(modsPath, "broken-casp.package"),
            Sims4ResourceTypeRegistry.CasPart,
            0,
            partId,
            [0x01, 0x02, 0x03, 0x04]);

        var saveData = new SaveGameData();
        var sim = new SimData
        {
            sim_id = 0x3333,
            first_name = "Broken",
            last_name = "Casp",
            extended_species = 1,
            outfits = new OutfitList()
        };
        sim.outfits.outfits.Add(new OutfitData
        {
            outfit_id = 0xCCCC,
            category = 0,
            parts = new EA.Sims4.IdList { ids = [partId] },
            body_types_list = new BodyTypesList { body_types = [5] },
            part_shifts = new ColorShiftList { color_shift = [0] }
        });
        saveData.sims.Add(sim);
        WriteSavePackage(savePath, saveData);

        var service = new SaveAppearanceLinkService();
        var snapshot = await service.BuildSnapshotAsync(savePath, gamePath, modsPath);

        Assert.Contains(snapshot.Issues, issue =>
            issue.Code == "PARSER_FAILED" &&
            issue.Scope == Ts4AppearanceIssueScope.Part);
    }

    [Fact]
    public async Task BuildSnapshotAsync_MalformedMorphPayload_DoesNotThrowAndAddsIssue()
    {
        using var fixture = new TempDirectory("save-appearance-invalid-morph");
        var savePath = Path.Combine(fixture.Path, "slot_00000004.save");
        var modsPath = Path.Combine(fixture.Path, "Mods");
        var gamePath = Path.Combine(fixture.Path, "Game");
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(gamePath);

        var saveData = new SaveGameData();
        saveData.sims.Add(new SimData
        {
            sim_id = 0x4444,
            first_name = "Broken",
            last_name = "Morph",
            facial_attr = [0xFF, 0x00, 0xFF, 0x00, 0x01]
        });
        WriteSavePackage(savePath, saveData);

        var service = new SaveAppearanceLinkService();
        var snapshot = await service.BuildSnapshotAsync(savePath, gamePath, modsPath);

        Assert.Single(snapshot.Sims);
        Assert.Contains(snapshot.Issues, issue => issue.Code == "MORPH_PAYLOAD_INVALID");
    }

    [Fact]
    public async Task BuildSnapshotAsync_NonHumanSpecies_ReportsLimitationIssue()
    {
        using var fixture = new TempDirectory("save-appearance-nonhuman");
        var savePath = Path.Combine(fixture.Path, "slot_00000005.save");
        var modsPath = Path.Combine(fixture.Path, "Mods");
        var gamePath = Path.Combine(fixture.Path, "Game");
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(gamePath);

        var saveData = new SaveGameData();
        saveData.sims.Add(new SimData
        {
            sim_id = 0x5555,
            first_name = "Wolf",
            last_name = "Form",
            extended_species = 3
        });
        WriteSavePackage(savePath, saveData);

        var service = new SaveAppearanceLinkService();
        var snapshot = await service.BuildSnapshotAsync(savePath, gamePath, modsPath);

        Assert.Contains(snapshot.Issues, issue =>
            issue.Code == "SPECIES_LIMITATION" &&
            issue.Scope == Ts4AppearanceIssueScope.Sim &&
            issue.SimId == 0x5555UL);
    }

    [Fact]
    public async Task BuildSnapshotAsync_MissingSimInfo_ReportsInfoSeverity()
    {
        using var fixture = new TempDirectory("save-appearance-missing-siminfo");
        var savePath = Path.Combine(fixture.Path, "slot_00000006.save");
        var modsPath = Path.Combine(fixture.Path, "Mods");
        var gamePath = Path.Combine(fixture.Path, "Game");
        Directory.CreateDirectory(modsPath);
        Directory.CreateDirectory(gamePath);

        var saveData = new SaveGameData();
        saveData.sims.Add(new SimData
        {
            sim_id = 0x6666,
            first_name = "No",
            last_name = "SimInfo",
            extended_species = 1
        });
        WriteSavePackage(savePath, saveData);

        var service = new SaveAppearanceLinkService();
        var snapshot = await service.BuildSnapshotAsync(savePath, gamePath, modsPath);

        Assert.Contains(snapshot.Issues, issue =>
            issue.Code == "RESOURCE_NOT_FOUND" &&
            issue.Scope == Ts4AppearanceIssueScope.Sim &&
            issue.Severity == Ts4AppearanceIssueSeverity.Info &&
            issue.SimId == 0x6666UL);
    }

    private static byte[] BuildMorphPayload(ulong simModifierInstance, ulong sculptInstance)
    {
        var payload = new BlobSimFacialCustomizationData
        {
            sculpts = [sculptInstance]
        };
        payload.face_modifiers.Add(new Modifier
        {
            key = simModifierInstance,
            amount = 0.75f
        });

        using var stream = new MemoryStream();
        Serializer.Serialize(stream, payload);
        return stream.ToArray();
    }

    private static byte[] BuildCaspFixture(
        ulong diffuseInstance,
        ulong normalInstance,
        ulong specularInstance,
        ulong meshInstance,
        ulong regionMapInstance)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        using var beWriter = new BinaryWriter(stream, Encoding.BigEndianUnicode, leaveOpen: true);

        writer.Write(32u);
        writer.Write(0u);
        writer.Write(0);
        beWriter.Write("Appearance Part");
        writer.Write(1.0f);
        writer.Write((ushort)0);
        writer.Write(0xDEAD0001u);
        writer.Write(0u);
        writer.Write((byte)0);
        writer.Write(0ul);
        writer.Write(0u);
        writer.Write(0);
        writer.Write(0u);
        writer.Write(0x11111111u);
        writer.Write(0x22222222u);
        writer.Write((byte)0);
        writer.Write(2u);
        writer.Write(0u);
        writer.Write(0x00002010u);
        writer.Write(1u);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(0ul);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(0);
        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write((byte)1);
        writer.Write(0ul);
        writer.Write(0u);
        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write((byte)1);
        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write((byte)4);
        writer.Write((byte)0);
        writer.Write((byte)2);
        writer.Write((byte)3);
        writer.Write(0u);
        writer.Write((byte)byte.MaxValue);
        writer.Write((byte)5);

        WriteIgt(writer, Sims4ResourceTypeRegistry.Geom, 0x10000000, meshInstance);
        WriteIgt(writer, Sims4ResourceTypeRegistry.Dds, 0x10000000, diffuseInstance);
        WriteIgt(writer, Sims4ResourceTypeRegistry.Dds, 0x10000000, normalInstance);
        WriteIgt(writer, Sims4ResourceTypeRegistry.Dds, 0x10000000, specularInstance);
        WriteIgt(writer, Sims4ResourceTypeRegistry.RegionMap, 0x10000000, regionMapInstance);

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildSmodFixture(ulong bgeoInstance, ulong dmapShapeInstance, ulong dmapNormalInstance, ulong bondInstance)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(1u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(0u);
        WriteItg(writer, Sims4ResourceTypeRegistry.BlendGeometry, 0, bgeoInstance);
        writer.Write(144u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        WriteItg(writer, Sims4ResourceTypeRegistry.BoneDelta, 0, bondInstance);
        WriteItg(writer, Sims4ResourceTypeRegistry.DeformerMap, 0, dmapShapeInstance);
        WriteItg(writer, Sims4ResourceTypeRegistry.DeformerMap, 0, dmapNormalInstance);

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildSculptFixture(ulong bgeoInstance, ulong dmapShapeInstance, ulong dmapNormalInstance, ulong bondInstance)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(1u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(0u);
        WriteItg(writer, Sims4ResourceTypeRegistry.BlendGeometry, 0, bgeoInstance);
        writer.Write(0x61u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        WriteItg(writer, Sims4ResourceTypeRegistry.Dds, 0, 0x7770000000000001);
        WriteItg(writer, Sims4ResourceTypeRegistry.Dds, 0, 0x7770000000000002);
        WriteItg(writer, Sims4ResourceTypeRegistry.Dds, 0, 0x7770000000000003);
        writer.Write((byte)0);
        WriteItg(writer, Sims4ResourceTypeRegistry.DeformerMap, 0, dmapShapeInstance);
        WriteItg(writer, Sims4ResourceTypeRegistry.DeformerMap, 0, dmapNormalInstance);
        WriteItg(writer, Sims4ResourceTypeRegistry.BoneDelta, 0, bondInstance);
        writer.Write(0u);

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildBgeoHeaderFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(3u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0x4F454742u);
        writer.Write(0x00000600u);
        writer.Write(1u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildDmapHeaderFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(7u);
        writer.Write(4u);
        writer.Write(2u);
        writer.Write(0x00002010u);
        writer.Write(1u);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write((byte)1);
        writer.Write(-0.2f);
        writer.Write(0.4f);
        writer.Write(0);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildBondHeaderFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(3u);
        writer.Write(1u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(1u);
        WriteItg(writer, Sims4ResourceTypeRegistry.BoneDelta, 0, 0x9000000000000001);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(1u);
        writer.Write(1u);
        for (var index = 0; index < 10; index++)
        {
            writer.Write(0f);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteItg(BinaryWriter writer, uint type, uint group, ulong instance)
    {
        writer.Write(instance);
        writer.Write(type);
        writer.Write(group);
    }

    private static void WriteIgt(BinaryWriter writer, uint type, uint group, ulong instance)
    {
        writer.Write(instance);
        writer.Write(group);
        writer.Write(type);
    }

    private static void WriteSavePackage(string savePath, SaveGameData saveData)
    {
        using var payload = new MemoryStream();
        Serializer.Serialize(payload, saveData);
        WritePackageWithSingleResource(savePath, 0x0000000D, 0, 1, payload.ToArray());
    }

    private static void WritePackageWithSingleResource(string packagePath, uint type, uint group, ulong instance, byte[] bytes)
    {
        const int headerSize = 96;
        const int flagsSize = 4;
        const int indexEntrySize = 32;

        var indexPosition = headerSize;
        var recordSize = flagsSize + indexEntrySize;
        var resourcePosition = headerSize + recordSize;

        var header = new byte[headerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), 1179664964u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), 2u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(36, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40, 4), (uint)indexPosition);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44, 4), (uint)recordSize);
        BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(64, 8), (ulong)indexPosition);

        var index = new byte[recordSize];
        var entry = index.AsSpan(flagsSize, indexEntrySize);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(0, 4), type);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(4, 4), group);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(8, 4), (uint)(instance >> 32));
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(12, 4), (uint)(instance & 0xFFFFFFFF));
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(16, 4), (uint)resourcePosition);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(20, 4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(24, 4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.Slice(28, 2), 0);

        using var stream = File.Create(packagePath);
        stream.Write(header);
        stream.Write(index);
        stream.Write(bytes);
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
