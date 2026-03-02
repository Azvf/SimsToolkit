using System.Text;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.PackageCore.Tests;

public sealed class Sims4ParsersTests
{
    [Fact]
    public void Sims4BinaryReaderExtensions_ReadTgi_SupportsAllSequences()
    {
        var tgiBytes = BuildTgiBytes(0x11111111, 0x22222222, 0x3333333344444444, Sims4TgiSequence.Tgi);
        var igtBytes = BuildTgiBytes(0x11111111, 0x22222222, 0x3333333344444444, Sims4TgiSequence.Igt);
        var itgBytes = BuildTgiBytes(0x11111111, 0x22222222, 0x3333333344444444, Sims4TgiSequence.Itg);

        Assert.Equal("11111111:22222222:3333333344444444", ReadTgi(tgiBytes, Sims4TgiSequence.Tgi).ToKeyText());
        Assert.Equal("11111111:22222222:3333333344444444", ReadTgi(igtBytes, Sims4TgiSequence.Igt).ToKeyText());
        Assert.Equal("11111111:22222222:3333333344444444", ReadTgi(itgBytes, Sims4TgiSequence.Itg).ToKeyText());
    }

    [Fact]
    public void Sims4CasPartParser_ReadsCoreFieldsAndTextureRefs()
    {
        var resourceKey = new DbpfResourceKey(Sims4ResourceTypeRegistry.CasPart, 0x00000001, 0xABCDEF1200000001);
        var bytes = BuildCaspFixture();

        var success = Sims4CasPartParser.TryParse(resourceKey, bytes, out var result, out var error);

        Assert.True(success, error);
        Assert.NotNull(result);
        Assert.Equal(32u, result.Version);
        Assert.Equal("Localized Hair", result.PartNameRaw);
        Assert.Equal(2u, result.BodyTypeNumeric);
        Assert.Equal(0x00002010u, result.AgeGenderFlags);
        Assert.Equal(0x11111111u, result.TitleKey);
        Assert.Equal(0x22222222u, result.PartDescriptionKey);
        Assert.Equal(3, result.ResourceTable.Count);
        Assert.Equal(new DbpfResourceKey(0x00B2D882, 0x00000001, 0xAAAABBBBCCCCDDDD), result.TextureRefs.Diffuse);
        Assert.Equal(new DbpfResourceKey(0x00B2D882, 0x00000001, 0x1111222233334444), result.TextureRefs.Normal);
        Assert.Equal(new DbpfResourceKey(0x00B2D882, 0x00000001, 0x9999888877776666), result.TextureRefs.Specular);
    }

    [Fact]
    public void Sims4StblParser_ReadsTaggedFixture()
    {
        var bytes = BuildStblFixture((0x11111111u, "Localized Hair"));

        var success = Sims4StblParser.TryParse(bytes, out var table, out var error);

        Assert.True(success, error);
        Assert.NotNull(table);
        Assert.True(table.Entries.TryGetValue(0x11111111u, out var value));
        Assert.Equal("Localized Hair", value);
    }

    private static Sims4Tgi ReadTgi(byte[] bytes, Sims4TgiSequence sequence)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream);
        return reader.ReadTgi(sequence);
    }

    private static byte[] BuildTgiBytes(uint type, uint group, ulong instance, Sims4TgiSequence sequence)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        switch (sequence)
        {
            case Sims4TgiSequence.Tgi:
                writer.Write(type);
                writer.Write(group);
                writer.Write(instance);
                break;
            case Sims4TgiSequence.Igt:
                writer.Write(instance);
                writer.Write(group);
                writer.Write(type);
                break;
            case Sims4TgiSequence.Itg:
                writer.Write(instance);
                writer.Write(type);
                writer.Write(group);
                break;
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] BuildCaspFixture()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        using var beWriter = new BinaryWriter(stream, Encoding.BigEndianUnicode, leaveOpen: true);

        writer.Write(32u); // version
        writer.Write(0u); // offset
        writer.Write(0); // presetCount
        beWriter.Write("Localized Hair");
        writer.Write(1.0f); // sortPriority
        writer.Write((ushort)0);
        writer.Write(0xDEADBEEFu); // outfitId
        writer.Write(0u); // materialHash
        writer.Write((byte)0); // parameterFlags
        writer.Write(0ul); // exclude flags
        writer.Write(0u); // excludeModifierRegionFlags
        writer.Write(0); // tagCount
        writer.Write(0u); // price
        writer.Write(0x11111111u); // titleKey
        writer.Write(0x22222222u); // partDescKey
        writer.Write((byte)0); // textureSpace
        writer.Write(2u); // bodyType Hair
        writer.Write(0u); // bodySubType
        writer.Write(0x00002010u); // Adult + Female
        writer.Write(1u); // species Human
        writer.Write((byte)0); // Unused2
        writer.Write((byte)0); // usedColorCount
        writer.Write((byte)0); // buffResKey
        writer.Write((byte)0); // swatchIndex
        writer.Write(0ul); // voice effect
        writer.Write((byte)0); // usedMaterialCount
        writer.Write(0u); // occultBitField
        writer.Write((byte)0); // nakedKey
        writer.Write((byte)0); // parentKey
        writer.Write(0); // sortLayer
        writer.Write((byte)0); // lodCount
        writer.Write((byte)0); // numSlotKeys
        writer.Write((byte)0); // textureIndex
        writer.Write((byte)0); // shadowIndex
        writer.Write((byte)0); // compositionMethod
        writer.Write((byte)0); // regionMapIndex
        writer.Write((byte)0); // numOverrides
        writer.Write((byte)1); // normalMapIndex
        writer.Write((byte)2); // specularIndex
        writer.Write(0u); // uvOverride
        writer.Write((byte)0); // emissionIndex
        writer.Write((byte)3); // IGT count
        WriteIgt(writer, 0x00B2D882, 0x00000001, 0xAAAABBBBCCCCDDDD);
        WriteIgt(writer, 0x00B2D882, 0x00000001, 0x1111222233334444);
        WriteIgt(writer, 0x00B2D882, 0x00000001, 0x9999888877776666);

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteIgt(BinaryWriter writer, uint type, uint group, ulong instance)
    {
        writer.Write(instance);
        writer.Write(group);
        writer.Write(type);
    }

    private static byte[] BuildStblFixture(params (uint Key, string Value)[] entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("STBL"));
        writer.Write(1u);
        writer.Write(entries.Length);
        foreach (var entry in entries)
        {
            var bytes = Encoding.UTF8.GetBytes(entry.Value);
            writer.Write(entry.Key);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
