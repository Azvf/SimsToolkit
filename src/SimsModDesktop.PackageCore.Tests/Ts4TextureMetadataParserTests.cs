using SimsModDesktop.PackageCore;

namespace SimsModDesktop.PackageCore.Tests;

public sealed class Ts4TextureMetadataParserTests
{
    [Fact]
    public void TryParse_Lrle_ParsesMip0AlphaStats()
    {
        var parser = new Ts4TextureMetadataParser();
        var key = new DbpfResourceKey(Sims4ResourceTypeRegistry.Lrle, 0, 0x7100000000000001);
        var bytes = BuildLrleFixture();

        var success = parser.TryParse(key, bytes, out var metadata, out var error);

        Assert.True(success, error);
        Assert.NotNull(metadata);
        Assert.Equal(Ts4TextureContainerKind.Lrle, metadata.ContainerKind);
        Assert.Equal(4, metadata.Width);
        Assert.Equal(4, metadata.Height);
        Assert.Equal(1, metadata.MipCount);
        Assert.True(metadata.Mip0Decoded);
        Assert.Equal(0, metadata.AlphaMin);
        Assert.Equal(0, metadata.AlphaMax);
        Assert.Equal(0, metadata.AlphaNonZeroPixelCount);
    }

    [Fact]
    public void TryParse_Rle2Dxt5_ParsesMip0AlphaStats()
    {
        var parser = new Ts4TextureMetadataParser();
        var key = new DbpfResourceKey(Sims4ResourceTypeRegistry.Rle2, 0, 0x7100000000000002);
        var bytes = BuildRle2Dxt5Fixture();

        var success = parser.TryParse(key, bytes, out var metadata, out var error);

        Assert.True(success, error);
        Assert.NotNull(metadata);
        Assert.Equal(Ts4TextureContainerKind.Rle2, metadata.ContainerKind);
        Assert.True(metadata.Mip0Decoded);
        Assert.Equal(255, metadata.AlphaMin);
        Assert.Equal(255, metadata.AlphaMax);
        Assert.Equal(16, metadata.AlphaNonZeroPixelCount);
    }

    [Fact]
    public void TryParse_Rle2L8_ReturnsDecodeErrorWithoutFailingParse()
    {
        var parser = new Ts4TextureMetadataParser();
        var key = new DbpfResourceKey(Sims4ResourceTypeRegistry.Rle2, 0, 0x7100000000000003);
        var bytes = BuildRle2L8Fixture();

        var success = parser.TryParse(key, bytes, out var metadata, out var error);

        Assert.True(success, error);
        Assert.NotNull(metadata);
        Assert.False(metadata.Mip0Decoded);
        Assert.Contains("not decoded", metadata.DecodeError, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] BuildLrleFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x454C524Cu); // LRLE
        writer.Write(0u); // version
        writer.Write((ushort)4); // width
        writer.Write((ushort)4); // height
        writer.Write(1u); // mip count
        writer.Write(0); // mip0 offset in mip blob
        writer.Write((byte)0x40); // instruction: zero-run of 16 pixels
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildRle2Dxt5Fixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x35545844u); // DXT5
        writer.Write(0x32454C52u); // RLE2
        writer.Write((ushort)4); // width
        writer.Write((ushort)4); // height
        writer.Write((ushort)1); // mip count
        writer.Write((ushort)0);

        var commandOffset = 36;
        var offset2 = 38;
        var offset3 = 42;
        var offset0 = 46;
        var offset1 = 46;
        writer.Write(commandOffset);
        writer.Write(offset2);
        writer.Write(offset3);
        writer.Write(offset0);
        writer.Write(offset1);

        writer.Write((ushort)0x0006); // count=1, op=2
        writer.Write(new byte[] { 0xFF, 0xFF, 0x00, 0x00 }); // color0/color1
        writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // color indices
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildRle2L8Fixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x2020384Cu); // L8
        writer.Write(0x32454C52u); // RLE2
        writer.Write((ushort)4); // width
        writer.Write((ushort)4); // height
        writer.Write((ushort)1); // mip count
        writer.Write((ushort)0);
        writer.Write(24); // command offset
        writer.Write(24); // data offset
        writer.Flush();
        return stream.ToArray();
    }
}
