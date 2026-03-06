namespace SimsModDesktop.PackageCore;

public sealed class Ts4Rig
{
    public required int Version { get; init; }
    public required int MinorVersion { get; init; }
    public required IReadOnlyList<Ts4RigBone> Bones { get; init; }
}

public sealed class Ts4RigBone
{
    public required int Index { get; init; }
    public required string Name { get; init; }
    public required int ParentIndex { get; init; }
    public required uint Hash { get; init; }
    public required uint Flags { get; init; }
}

public sealed class Ts4RigParser : ITS4ResourceParser<Ts4Rig>
{
    public bool TryParse(DbpfResourceKey key, ReadOnlySpan<byte> bytes, out Ts4Rig result, out string? error)
    {
        result = null!;
        error = null;

        if (key.Type != Sims4ResourceTypeRegistry.Rig)
        {
            error = "Resource is not RIG.";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            var version = reader.ReadInt32();
            var minorVersion = reader.ReadInt32();
            var boneCount = reader.ReadInt32();
            if (boneCount < 0)
            {
                throw new InvalidDataException("Invalid RIG bone count.");
            }

            var bones = new List<Ts4RigBone>(boneCount);
            for (var index = 0; index < boneCount; index++)
            {
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();

                var boneNameLength = reader.ReadInt32();
                if (boneNameLength < 0 || boneNameLength > 4096)
                {
                    throw new InvalidDataException("Invalid RIG bone name length.");
                }

                var chars = reader.ReadChars(boneNameLength);
                if (chars.Length != boneNameLength)
                {
                    throw new EndOfStreamException("Unexpected end of stream while reading RIG bone name.");
                }

                _ = reader.ReadInt32(); // opposingBoneIndex
                var parentIndex = reader.ReadInt32();
                var boneHash = reader.ReadUInt32();
                var flags = reader.ReadUInt32();

                bones.Add(new Ts4RigBone
                {
                    Index = index,
                    Name = new string(chars),
                    ParentIndex = parentIndex,
                    Hash = boneHash,
                    Flags = flags
                });
            }

            var ikChainCount = reader.ReadInt32();
            if (ikChainCount < 0)
            {
                throw new InvalidDataException("Invalid RIG IK chain count.");
            }

            for (var i = 0; i < ikChainCount; i++)
            {
                var boneListLength = reader.ReadInt32();
                if (boneListLength < 0)
                {
                    throw new InvalidDataException("Invalid RIG IK chain bone length.");
                }

                SkipBytes(reader, checked((boneListLength * sizeof(int)) + (3 * sizeof(int))));
            }

            result = new Ts4Rig
            {
                Version = version,
                MinorVersion = minorVersion,
                Bones = bones
            };
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse RIG: {ex.Message}";
            return false;
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
