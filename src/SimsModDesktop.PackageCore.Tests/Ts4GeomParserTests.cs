using System.Buffers.Binary;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.PackageCore.Tests;

public sealed class Ts4GeomParserTests
{
    [Fact]
    public void TryParse_ValidGeom_ReturnsLightweightSummary()
    {
        var parser = new Ts4GeomParser();
        var key = new DbpfResourceKey(Sims4ResourceTypeRegistry.Geom, 0, 0x7000000000000001);
        var bytes = BuildGeomFixture();

        var success = parser.TryParse(key, bytes, out var geom, out var error);

        Assert.True(success, error);
        Assert.NotNull(geom);
        Assert.Equal(3, geom.ContextVersion);
        Assert.Equal(14, geom.Version);
        Assert.Equal(2, geom.VertexCount);
        Assert.Equal(2, geom.FaceCount);
        Assert.Equal(1, geom.SubMeshCount);
        Assert.Equal(2, geom.BoneHashes.Count);
        Assert.Single(geom.TgiTable);
    }

    [Fact]
    public void TryParse_InvalidMagic_ReturnsFalse()
    {
        var parser = new Ts4GeomParser();
        var key = new DbpfResourceKey(Sims4ResourceTypeRegistry.Geom, 0, 0x7000000000000002);
        var bytes = BuildGeomFixture();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44, 4), 0xDEADBEEFu);

        var success = parser.TryParse(key, bytes, out _, out var error);

        Assert.False(success);
        Assert.Contains("Invalid GEOM magic", error);
    }

    private static byte[] BuildGeomFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(3); // context version
        writer.Write(0); // count
        writer.Write(0); // ind3
        writer.Write(0); // extCount
        writer.Write(0); // intCount
        writer.Write(0u); // dummy tgi type
        writer.Write(0u); // dummy tgi group
        writer.Write(0ul); // dummy tgi instance
        writer.Write(0); // abspos
        writer.Write(0); // meshsize
        writer.Write(0x4D4F4547u); // GEOM
        writer.Write(14); // version
        writer.Write(0); // tgi offset
        writer.Write(0); // tgi size
        writer.Write(0u); // shader hash
        writer.Write(7); // merge group
        writer.Write(2); // sort order
        writer.Write(2); // vertex count
        writer.Write(1); // format count
        writer.Write(1); // dataType
        writer.Write(2); // subType
        writer.Write((byte)12); // bytes per vertex
        writer.Write(new byte[24]); // vertex payload
        writer.Write(1); // submesh count
        writer.Write((byte)2); // bytes per face point
        writer.Write(6); // face point count
        writer.Write(new byte[12]); // faces
        writer.Write(0); // uv stitch count
        writer.Write(0); // seam stitch count
        writer.Write(0); // slotray count
        writer.Write(2); // bone hash count
        writer.Write(0x11111111u);
        writer.Write(0x22222222u);
        writer.Write(1); // tgi table count
        writer.Write(Sims4ResourceTypeRegistry.Dds);
        writer.Write(0x10000000u);
        writer.Write(0x7100000000000001ul);
        writer.Flush();

        return stream.ToArray();
    }
}
