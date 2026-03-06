namespace SimsModDesktop.PackageCore;

public sealed class Ts4RegionMap
{
    public required uint ContextVersion { get; init; }
    public required int Version { get; init; }
    public required IReadOnlyList<Ts4RegionMapMeshBlock> MeshBlocks { get; init; }
}

public sealed class Ts4RegionMapMeshBlock
{
    public required uint Region { get; init; }
    public required float Layer { get; init; }
    public required bool IsReplacement { get; init; }
    public required IReadOnlyList<DbpfResourceKey> MeshRefs { get; init; }
}

public sealed class Ts4RegionMapParser : ITS4ResourceParser<Ts4RegionMap>
{
    public bool TryParse(DbpfResourceKey key, ReadOnlySpan<byte> bytes, out Ts4RegionMap result, out string? error)
    {
        result = null!;
        error = null;

        if (key.Type != Sims4ResourceTypeRegistry.RegionMap)
        {
            error = "Resource is not RMAP.";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            var contextVersion = reader.ReadUInt32();
            var publicKeyCount = reader.ReadUInt32();
            var externalKeyCount = reader.ReadUInt32();
            var delayLoadKeyCount = reader.ReadUInt32();
            var objectCount = reader.ReadUInt32();

            SkipTgiBlock(reader, publicKeyCount);
            SkipTgiBlock(reader, externalKeyCount);
            SkipTgiBlock(reader, delayLoadKeyCount);
            SkipBytes(reader, checked((int)objectCount * 8));

            var version = reader.ReadInt32();
            var meshBlockCount = reader.ReadInt32();
            if (meshBlockCount < 0)
            {
                throw new InvalidDataException("Invalid RMAP mesh block count.");
            }

            var meshBlocks = new List<Ts4RegionMapMeshBlock>(meshBlockCount);
            for (var i = 0; i < meshBlockCount; i++)
            {
                var region = reader.ReadUInt32();
                var layer = reader.ReadSingle();
                var isReplacement = reader.ReadByte() != 0;
                var keyCount = reader.ReadUInt32();

                var refs = new List<DbpfResourceKey>((int)keyCount);
                for (var keyIndex = 0u; keyIndex < keyCount; keyIndex++)
                {
                    refs.Add(Ts4BinaryReaders.ReadItg(reader));
                }

                meshBlocks.Add(new Ts4RegionMapMeshBlock
                {
                    Region = region,
                    Layer = layer,
                    IsReplacement = isReplacement,
                    MeshRefs = refs
                });
            }

            result = new Ts4RegionMap
            {
                ContextVersion = contextVersion,
                Version = version,
                MeshBlocks = meshBlocks
            };
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse RMAP: {ex.Message}";
            return false;
        }
    }

    private static void SkipTgiBlock(BinaryReader reader, uint count)
    {
        for (var i = 0u; i < count; i++)
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
