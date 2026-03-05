namespace SimsModDesktop.PackageCore;

public sealed class Ts4MorphLinkGraphBuilder
{
    public Ts4MorphLinkGraph Build(
        IReadOnlyDictionary<DbpfResourceKey, byte[]> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var smodLinks = new Dictionary<ulong, Ts4MorphReference>();
        var sculptLinks = new Dictionary<ulong, Ts4MorphReference>();
        var issues = new List<string>();

        foreach (var pair in resources)
        {
            var key = pair.Key;
            var bytes = pair.Value;
            if (bytes.Length == 0)
            {
                continue;
            }

            if (key.Type == Sims4ResourceTypeRegistry.SimModifier)
            {
                if (TryParseSmod(key, bytes, out var reference, out var error))
                {
                    smodLinks[key.Instance] = reference;
                }
                else
                {
                    issues.Add(error ?? $"Failed to parse SMOD {key.Type:X8}:{key.Group:X8}:{key.Instance:X16}");
                }
            }
            else if (key.Type == Sims4ResourceTypeRegistry.Sculpt)
            {
                if (TryParseSculpt(key, bytes, out var reference, out var error))
                {
                    sculptLinks[key.Instance] = reference;
                }
                else
                {
                    issues.Add(error ?? $"Failed to parse Sculpt {key.Type:X8}:{key.Group:X8}:{key.Instance:X16}");
                }
            }
        }

        return new Ts4MorphLinkGraph
        {
            SimModifierLinks = smodLinks,
            SculptLinks = sculptLinks,
            Issues = issues,
            ReferencedResources = Array.Empty<Ts4MorphReferencedResourceHealth>()
        };
    }

    private static bool TryParseSmod(DbpfResourceKey sourceKey, ReadOnlySpan<byte> bytes, out Ts4MorphReference reference, out string? error)
    {
        reference = null!;
        error = null;

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            _ = reader.ReadUInt32(); // context version
            var publicCount = reader.ReadUInt32();
            var externalCount = reader.ReadUInt32();
            var bgeoCount = reader.ReadUInt32();
            var objectCount = reader.ReadUInt32();

            for (var i = 0; i < publicCount; i++)
            {
                _ = Ts4BinaryReaders.ReadItg(reader);
            }

            for (var i = 0; i < externalCount; i++)
            {
                _ = Ts4BinaryReaders.ReadItg(reader);
            }

            var bgeoRefs = new List<DbpfResourceKey>((int)bgeoCount);
            for (var i = 0; i < bgeoCount; i++)
            {
                bgeoRefs.Add(Ts4BinaryReaders.ReadItg(reader));
            }

            for (var i = 0; i < objectCount; i++)
            {
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt32();
            }

            var version = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            if (version >= 144)
            {
                _ = reader.ReadUInt32();
            }

            _ = reader.ReadUInt32();
            var boneDeltaRef = Ts4BinaryReaders.ReadItg(reader);
            var dmapShapeRef = Ts4BinaryReaders.ReadItg(reader);
            var dmapNormalRef = Ts4BinaryReaders.ReadItg(reader);

            reference = new Ts4MorphReference
            {
                SourceKey = sourceKey,
                BgeoRefs = bgeoRefs,
                BoneDeltaRef = boneDeltaRef.Instance == 0 ? null : boneDeltaRef,
                DmapShapeRef = dmapShapeRef.Instance == 0 ? null : dmapShapeRef,
                DmapNormalRef = dmapNormalRef.Instance == 0 ? null : dmapNormalRef
            };

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryParseSculpt(DbpfResourceKey sourceKey, ReadOnlySpan<byte> bytes, out Ts4MorphReference reference, out string? error)
    {
        reference = null!;
        error = null;

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            _ = reader.ReadUInt32(); // context version
            var publicCount = reader.ReadUInt32();
            var externalCount = reader.ReadUInt32();
            var bgeoCount = reader.ReadUInt32();
            var objectCount = reader.ReadUInt32();

            for (var i = 0; i < publicCount; i++)
            {
                _ = Ts4BinaryReaders.ReadItg(reader);
            }

            for (var i = 0; i < externalCount; i++)
            {
                _ = Ts4BinaryReaders.ReadItg(reader);
            }

            var bgeoRefs = new List<DbpfResourceKey>((int)bgeoCount);
            for (var i = 0; i < bgeoCount; i++)
            {
                bgeoRefs.Add(Ts4BinaryReaders.ReadItg(reader));
            }

            for (var i = 0; i < objectCount; i++)
            {
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt32();
            }

            var version = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            if (version > 0x60)
            {
                _ = reader.ReadUInt32();
            }

            _ = reader.ReadUInt32();
            _ = Ts4BinaryReaders.ReadItg(reader); // texture
            if (version > 0x60)
            {
                _ = Ts4BinaryReaders.ReadItg(reader); // spec
                _ = Ts4BinaryReaders.ReadItg(reader); // bump
            }

            _ = reader.ReadByte();
            var dmapShapeRef = Ts4BinaryReaders.ReadItg(reader);
            var dmapNormalRef = Ts4BinaryReaders.ReadItg(reader);
            DbpfResourceKey? boneDeltaRef = null;
            if (version > 0x60)
            {
                var boneRef = Ts4BinaryReaders.ReadItg(reader);
                boneDeltaRef = boneRef.Instance == 0 ? null : boneRef;
                _ = reader.ReadUInt32();
            }

            reference = new Ts4MorphReference
            {
                SourceKey = sourceKey,
                BgeoRefs = bgeoRefs,
                BoneDeltaRef = boneDeltaRef,
                DmapShapeRef = dmapShapeRef.Instance == 0 ? null : dmapShapeRef,
                DmapNormalRef = dmapNormalRef.Instance == 0 ? null : dmapNormalRef
            };

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
