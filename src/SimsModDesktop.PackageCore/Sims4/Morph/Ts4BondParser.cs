namespace SimsModDesktop.PackageCore;

public sealed class Ts4Bond
{
    public required uint ContextVersion { get; init; }
    public required uint Version { get; init; }
    public required IReadOnlyList<Ts4BondAdjustment> Adjustments { get; init; }
}

public readonly record struct Ts4BondAdjustment(
    uint SlotHash,
    float OffsetX,
    float OffsetY,
    float OffsetZ,
    float ScaleX,
    float ScaleY,
    float ScaleZ,
    float QuatX,
    float QuatY,
    float QuatZ,
    float QuatW);

public sealed class Ts4BondParser : ITS4ResourceParser<Ts4Bond>
{
    public bool TryParse(DbpfResourceKey key, ReadOnlySpan<byte> bytes, out Ts4Bond result, out string? error)
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
            var adjustCount = reader.ReadUInt32();
            var adjustments = new List<Ts4BondAdjustment>((int)adjustCount);
            for (var i = 0u; i < adjustCount; i++)
            {
                adjustments.Add(new Ts4BondAdjustment(
                    reader.ReadUInt32(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle(),
                    reader.ReadSingle()));
            }

            result = new Ts4Bond
            {
                ContextVersion = contextVersion,
                Version = version,
                Adjustments = adjustments
            };
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse BOND: {ex.Message}";
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
