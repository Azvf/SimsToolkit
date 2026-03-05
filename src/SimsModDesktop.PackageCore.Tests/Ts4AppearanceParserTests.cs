using System.Text;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.PackageCore.Tests;

public sealed class Ts4AppearanceParserTests
{
    [Fact]
    public void Sims4CasPartExtendedParser_ParsesLodAndRegionMap()
    {
        var key = new DbpfResourceKey(Sims4ResourceTypeRegistry.CasPart, 0, 0x1000000000000001);
        var bytes = BuildCaspFixture(
            diffuseInstance: 0x7100000000000001,
            normalInstance: 0x7100000000000002,
            specularInstance: 0x7100000000000003,
            meshInstance: 0x7200000000000001,
            regionMapInstance: 0x7300000000000001);

        var success = Sims4CasPartExtendedParser.TryParse(key, bytes, out var result, out var error);

        Assert.True(success, error);
        Assert.NotNull(result);
        Assert.NotEmpty(result.LodEntries);
        Assert.Single(result.LodEntries[0].MeshParts);
        Assert.NotEmpty(result.BaseInfo.TextureRefs.AllDistinct);
    }

    [Fact]
    public void Ts4SimInfoResourceParser_ParsesCoreSections()
    {
        var parser = new Ts4SimInfoResourceParser();
        var key = new DbpfResourceKey(Sims4ResourceTypeRegistry.SimInfo, 0, 0x1111);
        var bytes = BuildSimInfoFixture();

        var success = parser.TryParse(key, bytes, out var result, out var error);

        Assert.True(success, error);
        Assert.NotNull(result);
        Assert.Equal(32u, result.Version);
        Assert.Equal(1u, result.Species);
        Assert.Single(result.Sculpts);
        Assert.Single(result.FaceModifiers);
        Assert.Single(result.BodyModifiers);
        Assert.Single(result.Outfits);
        Assert.Single(result.Outfits[0].Parts);
        Assert.Single(result.GeneticSculpts);
        Assert.Single(result.GeneticFaceModifiers);
        Assert.Empty(result.GeneticBodyModifiers);
        Assert.Single(result.GeneticParts);
        Assert.Equal(2, result.TraitRefs.Count);
    }

    [Fact]
    public void MorphHeaderParsers_ParseExpectedFields()
    {
        var bgeoParser = new Ts4BgeoHeaderParser();
        var dmapParser = new Ts4DmapHeaderParser();
        var bondParser = new Ts4BondHeaderParser();

        Assert.True(
            bgeoParser.TryParse(
                new DbpfResourceKey(Sims4ResourceTypeRegistry.BlendGeometry, 0, 0x1),
                BuildBgeoHeaderFixture(),
                out var bgeo,
                out var bgeoError),
            bgeoError);
        Assert.Equal(3u, bgeo.ContextVersion);
        Assert.Equal(0x00000600u, bgeo.Version);
        Assert.Equal(1u, bgeo.LodCount);

        Assert.True(
            dmapParser.TryParse(
                new DbpfResourceKey(Sims4ResourceTypeRegistry.DeformerMap, 0, 0x2),
                BuildDmapHeaderFixture(),
                out var dmap,
                out var dmapError),
            dmapError);
        Assert.Equal(7u, dmap.Version);
        Assert.Equal(2u, dmap.Width);
        Assert.Equal(2u, dmap.Height);
        Assert.True(dmap.Species > 0);

        Assert.True(
            bondParser.TryParse(
                new DbpfResourceKey(Sims4ResourceTypeRegistry.BoneDelta, 0, 0x3),
                BuildBondHeaderFixture(),
                out var bond,
                out var bondError),
            bondError);
        Assert.Equal(3u, bond.ContextVersion);
        Assert.Equal(1u, bond.Version);
        Assert.Equal(1u, bond.BoneAdjustCount);
    }

    [Fact]
    public void MorphHeaderParsers_InvalidPayload_ReturnFalse()
    {
        var bgeoParser = new Ts4BgeoHeaderParser();
        var success = bgeoParser.TryParse(
            new DbpfResourceKey(Sims4ResourceTypeRegistry.BlendGeometry, 0, 0x4),
            [0x00, 0x01, 0x02],
            out _,
            out var error);

        Assert.False(success);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    private static byte[] BuildSimInfoFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(32u);
        writer.Write(0u);
        for (var index = 0; index < 8; index++)
        {
            writer.Write((float)(index + 1));
        }

        writer.Write(0x00002010u);
        writer.Write(0x00001000u);
        writer.Write(1u);
        writer.Write(0u);
        writer.Write(1);
        writer.Write(1u);
        writer.Write("they");
        writer.Write(0xAAul);
        writer.Write(0.5f);
        writer.Write((byte)1);
        writer.Write(0xBBul);
        writer.Write(0x12345678u);

        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write((byte)1);
        writer.Write((byte)1);
        writer.Write(0.75f);
        writer.Write((byte)1);
        writer.Write((byte)1);
        writer.Write(0.25f);

        writer.Write(3u);
        writer.Write(0.6f);
        writer.Write(0xCCul);
        writer.Write(0u);
        writer.Write(0u);

        writer.Write(1u);
        writer.Write((byte)2);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write(0xD1ul);
        writer.Write(0xD2ul);
        writer.Write(0xD3ul);
        writer.Write(true);
        writer.Write(1u);
        writer.Write((byte)2);
        writer.Write(5u);
        writer.Write(0xD4ul);

        writer.Write((byte)1);
        writer.Write((byte)3);
        writer.Write((byte)1);
        writer.Write((byte)4);
        writer.Write(0.9f);
        writer.Write((byte)0);
        writer.Write(0.1f);
        writer.Write(0.2f);
        writer.Write(0.3f);
        writer.Write(0.4f);

        writer.Write((byte)1);
        writer.Write((byte)2);
        writer.Write(7u);
        writer.Write((byte)0);

        writer.Write(4u);
        writer.Write(0.4f);
        writer.Write((byte)1);
        writer.Write(0xE1ul);
        writer.Write((byte)1);
        writer.Write((byte)2);
        writer.Write((byte)3);
        writer.Write((byte)2);
        writer.Write(0xF1ul);
        writer.Write(0xF2ul);

        var tgiTablePosition = stream.Position;
        writer.Write((byte)5);
        WriteIgt(writer, Sims4ResourceTypeRegistry.Sculpt, 0x10000000, 0x3000000000000001);
        WriteIgt(writer, Sims4ResourceTypeRegistry.SimModifier, 0x10000000, 0x3000000000000002);
        WriteIgt(writer, Sims4ResourceTypeRegistry.CasPart, 0x10000000, 0x3000000000000003);
        WriteIgt(writer, Sims4ResourceTypeRegistry.Sculpt, 0x10000000, 0x3000000000000004);
        WriteIgt(writer, Sims4ResourceTypeRegistry.SimModifier, 0x10000000, 0x3000000000000005);

        stream.Position = sizeof(uint);
        writer.Write(checked((uint)(tgiTablePosition - 8)));

        writer.Flush();
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
}
