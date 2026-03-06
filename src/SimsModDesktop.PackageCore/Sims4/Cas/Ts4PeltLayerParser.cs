namespace SimsModDesktop.PackageCore;

public readonly record struct Ts4PeltLayerCategoryTag(ushort Category, uint Value);

public sealed class Ts4PeltLayer
{
    public required uint Version { get; init; }
    public required uint Unknown { get; init; }
    public required float SortOrder { get; init; }
    public required byte Usage { get; init; }
    public required ulong LinkedPeltLayer { get; init; }
    public required uint NameKey { get; init; }
    public required ulong TextureInstance { get; init; }
    public required ulong ThumbnailInstance { get; init; }
    public required IReadOnlyList<Ts4PeltLayerCategoryTag> CategoryTags { get; init; }
}

public sealed class Ts4PeltLayerParser : ITS4ResourceParser<Ts4PeltLayer>
{
    public bool TryParse(DbpfResourceKey key, ReadOnlySpan<byte> bytes, out Ts4PeltLayer result, out string? error)
    {
        result = null!;
        error = null;

        if (key.Type != Sims4ResourceTypeRegistry.PeltLayer)
        {
            error = "Resource is not PELT_LAYER.";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            var version = reader.ReadUInt32();
            var unknown = reader.ReadUInt32();
            var sortOrder = reader.ReadSingle();
            var usage = reader.ReadByte();
            var linkedPeltLayer = version > 5 ? reader.ReadUInt64() : 0UL;
            var nameKey = reader.ReadUInt32();
            if (version >= 8)
            {
                _ = reader.ReadBytes(5);
            }

            var textureKey = reader.ReadUInt64();
            var thumbnailKey = reader.ReadUInt64();
            var tagCount = reader.ReadUInt32();
            var tags = new List<Ts4PeltLayerCategoryTag>((int)tagCount);
            for (var i = 0u; i < tagCount; i++)
            {
                tags.Add(new Ts4PeltLayerCategoryTag(reader.ReadUInt16(), reader.ReadUInt32()));
            }

            result = new Ts4PeltLayer
            {
                Version = version,
                Unknown = unknown,
                SortOrder = sortOrder,
                Usage = usage,
                LinkedPeltLayer = linkedPeltLayer,
                NameKey = nameKey,
                TextureInstance = textureKey,
                ThumbnailInstance = thumbnailKey,
                CategoryTags = tags
            };
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse PELT_LAYER: {ex.Message}";
            return false;
        }
    }
}
