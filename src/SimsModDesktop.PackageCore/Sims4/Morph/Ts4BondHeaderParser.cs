namespace SimsModDesktop.PackageCore;

public sealed class Ts4BondHeader
{
    public required uint ContextVersion { get; init; }
    public required uint Version { get; init; }
    public required uint BoneAdjustCount { get; init; }
}

public sealed class Ts4BondHeaderParser : ITS4ResourceParser<Ts4BondHeader>
{
    public bool TryParse(DbpfResourceKey key, ReadOnlySpan<byte> bytes, out Ts4BondHeader result, out string? error)
    {
        result = null!;
        error = null;

        if (key.Type != Sims4ResourceTypeRegistry.BoneDelta)
        {
            error = "Resource is not BOND.";
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
            var privateKeyCount = objectCount > publicCount ? objectCount - publicCount : 0u;
            SkipTgiBlock(reader, privateKeyCount);
            SkipTgiBlock(reader, externalCount);
            SkipTgiBlock(reader, delayCount);

            SkipBytes(reader, checked((int)objectCount * 8));

            var version = reader.ReadUInt32();
            var boneAdjustCount = reader.ReadUInt32();

            result = new Ts4BondHeader
            {
                ContextVersion = contextVersion,
                Version = version,
                BoneAdjustCount = boneAdjustCount
            };
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse BOND header: {ex.Message}";
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
