namespace SimsModDesktop.PackageCore;

public sealed class Ts4DmapHeader
{
    public required uint Version { get; init; }
    public required uint Width { get; init; }
    public required uint Height { get; init; }
    public required uint AgeGender { get; init; }
    public required uint Species { get; init; }
    public required byte Physique { get; init; }
    public required byte ShapeOrNormals { get; init; }
    public required bool HasRobeChannel { get; init; }
}

public sealed class Ts4DmapHeaderParser : ITS4ResourceParser<Ts4DmapHeader>
{
    public bool TryParse(DbpfResourceKey key, ReadOnlySpan<byte> bytes, out Ts4DmapHeader result, out string? error)
    {
        result = null!;
        error = null;

        if (key.Type != Sims4ResourceTypeRegistry.DeformerMap)
        {
            error = "Resource is not DMAP.";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            var version = reader.ReadUInt32();
            var doubledWidth = reader.ReadUInt32();
            var height = reader.ReadUInt32();
            var ageGender = reader.ReadUInt32();
            var species = version > 5 ? reader.ReadUInt32() : 0u;
            var physique = reader.ReadByte();
            var shapeOrNormals = reader.ReadByte();

            _ = reader.ReadUInt32(); // minCol
            _ = reader.ReadUInt32(); // maxCol
            _ = reader.ReadUInt32(); // minRow
            _ = reader.ReadUInt32(); // maxRow
            var robeChannel = reader.ReadByte();

            result = new Ts4DmapHeader
            {
                Version = version,
                Width = doubledWidth / 2,
                Height = height,
                AgeGender = ageGender,
                Species = species,
                Physique = physique,
                ShapeOrNormals = shapeOrNormals,
                // TS4 DMAP robe channel flag is commonly encoded as: 0=none, 1=present, 2=drop.
                // Treat any non-drop value as available to avoid false negatives across game variants.
                HasRobeChannel = robeChannel != 2
            };
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse DMAP header: {ex.Message}";
            return false;
        }
    }
}
