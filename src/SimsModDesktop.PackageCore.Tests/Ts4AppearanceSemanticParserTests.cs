using System.Buffers.Binary;
using System.Text;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.PackageCore.Tests;

public sealed class Ts4AppearanceSemanticParserTests
{
    [Fact]
    public void Ts4FnvHash_Fnv64_MatchesKnownValue()
    {
        var hash = Ts4FnvHash.Fnv64("JawWidth");

        Assert.Equal(0x83C650E19CA34E33ul, hash);
    }

    [Fact]
    public void Ts4CasModifierTuningCatalogLoader_ParsesScaleAndHash()
    {
        using var fixture = new TempDirectory("casmod-catalog");
        var packagePath = Path.Combine(fixture.Path, "catalog.package");
        WritePackageWithSingleResource(
            packagePath,
            Sims4ResourceTypeRegistry.Tuning1,
            0,
            0x0000000000001111,
            Encoding.UTF8.GetBytes("""
                <I c="Client_CASModifierTuning">
                  <T n="ModifierName">JawWidth</T>
                  <T n="Scale">1.5</T>
                  <L n="Ages"><E>ADULT</E></L>
                  <L n="Genders"><E>FEMALE</E></L>
                  <E n="Species">HUMAN</E>
                  <E n="OccultType">HUMAN</E>
                </I>
                """));

        var loader = new Ts4CasModifierTuningCatalogLoader();
        var success = loader.TryLoadFromPackage(packagePath, out var catalog, out var error);

        Assert.True(success, error);
        Assert.NotNull(catalog);
        var entry = Assert.Single(catalog.ByModifierHash.Values);
        Assert.Equal("JawWidth", entry.DisplayName);
        Assert.Equal(1.5f, entry.ScaleRules[0].Scale);
        Assert.Contains("FEMALE", entry.ScaleRules[0].Restriction.Genders);
        Assert.Equal(0x83C650E19CA34E33ul, entry.ModifierHash);
    }

    [Fact]
    public void RegionTonePeltParsers_ParseExpectedFields()
    {
        var regionParser = new Ts4RegionMapParser();
        var toneParser = new Ts4ToneParser();
        var peltParser = new Ts4PeltLayerParser();

        Assert.True(
            regionParser.TryParse(
                new DbpfResourceKey(Sims4ResourceTypeRegistry.RegionMap, 0, 1),
                BuildRegionMapFixture(),
                out var region,
                out var regionError),
            regionError);
        var meshBlock = Assert.Single(region.MeshBlocks);
        Assert.True(meshBlock.IsReplacement);
        Assert.Equal(Sims4ResourceTypeRegistry.Geom, meshBlock.MeshRefs[0].Type);

        Assert.True(
            toneParser.TryParse(
                new DbpfResourceKey(Sims4ResourceTypeRegistry.Tone, 0, 2),
                BuildToneFixture(),
                out var tone,
                out var toneError),
            toneError);
        Assert.Equal(11u, tone.Version);
        Assert.Single(tone.SkinSets);
        Assert.Single(tone.Overlays);
        Assert.Equal((ulong)0x260A5, tone.TuningInstance);

        Assert.True(
            peltParser.TryParse(
                new DbpfResourceKey(Sims4ResourceTypeRegistry.PeltLayer, 0, 3),
                BuildPeltLayerFixture(),
                out var pelt,
                out var peltError),
            peltError);
        Assert.Equal(8u, pelt.Version);
        Assert.Equal((ulong)0x0000000000000555, pelt.TextureInstance);
        Assert.Single(pelt.CategoryTags);
    }

    [Fact]
    public void RigBondParsers_ParseExpectedFields()
    {
        var rigParser = new Ts4RigParser();
        var bondParser = new Ts4BondParser();

        Assert.True(
            rigParser.TryParse(
                new DbpfResourceKey(Sims4ResourceTypeRegistry.Rig, 0, 4),
                BuildRigFixture(),
                out var rig,
                out var rigError),
            rigError);
        Assert.Equal(2, rig.Bones.Count);
        Assert.Equal("Root", rig.Bones[0].Name);
        Assert.Equal(0x11111111u, rig.Bones[0].Hash);

        Assert.True(
            bondParser.TryParse(
                new DbpfResourceKey(Sims4ResourceTypeRegistry.BoneDelta, 0, 5),
                BuildBondFixture(),
                out var bond,
                out var bondError),
            bondError);
        Assert.Equal(2, bond.Adjustments.Count);
        Assert.Equal(0x11111111u, bond.Adjustments[0].SlotHash);
    }

    private static byte[] BuildRegionMapFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(3u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(44u);
        writer.Write(21u);
        writer.Write(1);
        writer.Write(1);
        writer.Write(5u);
        writer.Write(0.25f);
        writer.Write((byte)1);
        writer.Write(1u);
        WriteItg(writer, Sims4ResourceTypeRegistry.Geom, 0x10000000, 0x7000000000000001);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildToneFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(11u);
        writer.Write((byte)1);
        writer.Write(0x111ul);
        writer.Write(0x222ul);
        writer.Write(1.2f);
        writer.Write(0.3f);
        writer.Write(0.4f);
        writer.Write(1);
        writer.Write(0x00002010u);
        writer.Write(0x333ul);
        writer.Write((ushort)30);
        writer.Write((ushort)40);
        writer.Write(255u);
        writer.Write(1);
        writer.Write((ushort)0x0045);
        writer.Write(0x51u);
        writer.Write((byte)1);
        writer.Write(unchecked((int)0xFF112233));
        writer.Write(5f);
        writer.Write(0x260A5ul);
        writer.Write((ushort)2);
        writer.Write(-0.1f);
        writer.Write(0.1f);
        writer.Write(0.01f);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildPeltLayerFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(8u);
        writer.Write(77u);
        writer.Write(2.5f);
        writer.Write((byte)3);
        writer.Write(0x444ul);
        writer.Write(0x12345678u);
        writer.Write(new byte[] { 1, 2, 3, 4, 5 });
        writer.Write(0x555ul);
        writer.Write(0x666ul);
        writer.Write(1u);
        writer.Write((ushort)0x20);
        writer.Write(0x1234u);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildRigFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(3);
        writer.Write(1);
        writer.Write(2);
        WriteRigBone(writer, "Root", -1, 0x11111111u);
        WriteRigBone(writer, "Spine", 0, 0x22222222u);
        writer.Write(0);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildBondFixture()
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
        writer.Write(2u);
        WriteBondAdjustment(writer, 0x11111111u);
        WriteBondAdjustment(writer, 0x33333333u);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteRigBone(BinaryWriter writer, string name, int parentIndex, uint hash)
    {
        for (var i = 0; i < 10; i++)
        {
            writer.Write(i == 6 ? 1f : 0f);
        }

        writer.Write(name.Length);
        writer.Write(name.ToCharArray());
        writer.Write(0);
        writer.Write(parentIndex);
        writer.Write(hash);
        writer.Write(0u);
    }

    private static void WriteBondAdjustment(BinaryWriter writer, uint slotHash)
    {
        writer.Write(slotHash);
        writer.Write(1f);
        writer.Write(2f);
        writer.Write(3f);
        writer.Write(1f);
        writer.Write(1f);
        writer.Write(1f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(1f);
    }

    private static void WriteItg(BinaryWriter writer, uint type, uint group, ulong instance)
    {
        writer.Write(instance);
        writer.Write(type);
        writer.Write(group);
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
