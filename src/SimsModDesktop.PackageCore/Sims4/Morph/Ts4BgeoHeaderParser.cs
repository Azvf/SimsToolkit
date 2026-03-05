using System.Text;

namespace SimsModDesktop.PackageCore;

public sealed class Ts4BgeoHeader
{
    public required uint ContextVersion { get; init; }
    public required uint Version { get; init; }
    public required uint LodCount { get; init; }
    public required uint TotalVertexCount { get; init; }
    public required uint TotalVectorCount { get; init; }
}

public sealed class Ts4BgeoHeaderParser : ITS4ResourceParser<Ts4BgeoHeader>
{
    private static readonly uint BgeoTag = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("BGEO"), 0);

    public bool TryParse(DbpfResourceKey key, ReadOnlySpan<byte> bytes, out Ts4BgeoHeader result, out string? error)
    {
        result = null!;
        error = null;

        if (key.Type != Sims4ResourceTypeRegistry.BlendGeometry)
        {
            error = "Resource is not BGEO.";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            var contextVersion = reader.ReadUInt32();
            var publicCount = reader.ReadUInt32();
            var externalCount = reader.ReadUInt32();
            var delayCount = reader.ReadUInt32();
            var objectCount = reader.ReadUInt32();

            SkipTgiBlock(reader, publicCount);
            SkipTgiBlock(reader, externalCount);
            SkipTgiBlock(reader, delayCount);
            SkipBytes(reader, checked((int)objectCount * 8));

            var tag = reader.ReadUInt32();
            if (tag != BgeoTag)
            {
                error = "Invalid BGEO tag.";
                return false;
            }

            var version = reader.ReadUInt32();
            var lodCount = reader.ReadUInt32();
            var totalVertexCount = reader.ReadUInt32();
            var totalVectorCount = reader.ReadUInt32();

            result = new Ts4BgeoHeader
            {
                ContextVersion = contextVersion,
                Version = version,
                LodCount = lodCount,
                TotalVertexCount = totalVertexCount,
                TotalVectorCount = totalVectorCount
            };
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse BGEO header: {ex.Message}";
            return false;
        }
    }

    private static void SkipTgiBlock(BinaryReader reader, uint count)
    {
        for (var index = 0u; index < count; index++)
        {
            _ = Ts4BinaryReaders.ReadItg(reader);
        }
    }

    private static void SkipBytes(BinaryReader reader, int count)
    {
        if (count <= 0)
        {
            return;
        }

        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
        {
            throw new EndOfStreamException("Unexpected end of stream.");
        }
    }
}
