namespace SimsModDesktop.PackageCore;

public static class Sims4CasPartParser
{
    public static bool TryParse(
        DbpfResourceKey resourceKey,
        ReadOnlySpan<byte> bytes,
        out Sims4CasPartInfo result,
        out string? error)
    {
        result = null!;
        error = null;

        if (resourceKey.Type != Sims4ResourceTypeRegistry.CasPart)
        {
            error = "Resource is not a CASP entry.";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 32)
            {
                error = "CASP payload is too small.";
                return false;
            }

            var version = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            _ = reader.ReadInt32(); // presetCount
            var partName = reader.ReadLengthPrefixedStringUtf16BigEndian();
            _ = reader.ReadSingle(); // sortPriority
            _ = reader.ReadUInt16(); // swatchOrder
            var outfitId = reader.ReadUInt32();
            _ = reader.ReadUInt32(); // materialHash
            _ = reader.ReadByte(); // parameterFlags

            if (version >= 39)
            {
                _ = reader.ReadByte(); // parameterFlags2
            }

            if (version >= 50)
            {
                _ = reader.ReadUInt16(); // layer id
            }

            if (version >= 51)
            {
                var excludeFlagsCount = reader.ReadInt32();
                for (var index = 0; index < excludeFlagsCount; index++)
                {
                    _ = reader.ReadUInt64();
                }
            }
            else
            {
                _ = reader.ReadUInt64();
                if (version >= 41)
                {
                    _ = reader.ReadUInt64();
                }
            }

            if (version > 36)
            {
                _ = reader.ReadUInt64();
            }
            else
            {
                _ = reader.ReadUInt32();
            }

            var tagCount = reader.ReadInt32();
            for (var index = 0; index < tagCount; index++)
            {
                _ = reader.ReadUInt16();
                if (version >= 37)
                {
                    _ = reader.ReadUInt32();
                }
                else
                {
                    _ = reader.ReadUInt16();
                }
            }

            _ = reader.ReadUInt32(); // price
            var titleKey = reader.ReadUInt32();
            var partDescriptionKey = reader.ReadUInt32();
            if (version >= 0x2B)
            {
                _ = reader.ReadUInt32();
            }

            _ = reader.ReadByte(); // textureSpace
            var bodyType = reader.ReadUInt32();
            var bodySubType = reader.ReadUInt32();
            var ageGenderFlags = reader.ReadUInt32();
            var species = version >= 32 ? reader.ReadUInt32() : 0u;
            if (version >= 34)
            {
                _ = reader.ReadUInt16();
                _ = reader.ReadByte();
                _ = reader.ReadBytes(9);
            }
            else
            {
                var unused2 = reader.ReadByte();
                if (unused2 > 0)
                {
                    _ = reader.ReadByte();
                }
            }

            var usedColorCount = reader.ReadByte();
            for (var index = 0; index < usedColorCount; index++)
            {
                _ = reader.ReadUInt32();
            }

            _ = reader.ReadByte(); // buffResKey
            _ = reader.ReadByte(); // swatchIndex
            if (version >= 28)
            {
                _ = reader.ReadUInt64();
            }

            if (version >= 30)
            {
                var usedMaterialCount = reader.ReadByte();
                if (usedMaterialCount > 0)
                {
                    _ = reader.ReadUInt32();
                    _ = reader.ReadUInt32();
                    _ = reader.ReadUInt32();
                }
            }

            if (version >= 31)
            {
                _ = reader.ReadUInt32();
            }

            if (version >= 0x2E)
            {
                _ = reader.ReadUInt64();
            }

            if (version >= 38)
            {
                _ = reader.ReadUInt64();
            }

            if (version >= 39)
            {
                _ = reader.ReadUInt64();
            }

            if (version >= 44)
            {
                SkipBytes(reader, sizeof(float) * 8);
            }

            if (version >= 0x2E)
            {
                var unknownCount = reader.ReadByte();
                SkipBytes(reader, unknownCount);
            }

            _ = reader.ReadByte(); // nakedKey
            _ = reader.ReadByte(); // parentKey
            _ = reader.ReadInt32(); // sortLayer

            var lodCount = reader.ReadByte();
            for (var lodIndex = 0; lodIndex < lodCount; lodIndex++)
            {
                _ = reader.ReadByte();
                _ = reader.ReadUInt32();
                var numAssets = reader.ReadByte();
                SkipBytes(reader, numAssets * 12);
                var indexCount = reader.ReadByte();
                SkipBytes(reader, indexCount);
            }

            var numSlotKeys = reader.ReadByte();
            SkipBytes(reader, numSlotKeys);

            var textureIndex = reader.ReadByte();
            var shadowIndex = reader.ReadByte();
            _ = reader.ReadByte(); // compositionMethod
            _ = reader.ReadByte(); // regionMapIndex

            var overrideCount = reader.ReadByte();
            SkipBytes(reader, overrideCount * 5);

            var normalMapIndex = reader.ReadByte();
            var specularIndex = reader.ReadByte();
            if (version >= 27)
            {
                _ = reader.ReadUInt32();
            }

            var emissionIndex = version >= 29 ? reader.ReadByte() : byte.MaxValue;
            if (version >= 42)
            {
                _ = reader.ReadByte();
            }

            if (version >= 49)
            {
                _ = reader.ReadByte();
            }

            if (version >= 52)
            {
                _ = reader.ReadByte();
            }

            var tableCount = reader.ReadByte();
            var table = new List<Sims4Tgi>(tableCount);
            for (var index = 0; index < tableCount; index++)
            {
                table.Add(ReadIgt(reader));
            }

            var textureRefs = BuildTextureRefs(table, textureIndex, shadowIndex, normalMapIndex, specularIndex, emissionIndex);
            result = new Sims4CasPartInfo
            {
                ResourceKey = resourceKey,
                Version = version,
                PartNameRaw = string.IsNullOrWhiteSpace(partName) ? null : partName.Trim(),
                TitleKey = titleKey,
                PartDescriptionKey = partDescriptionKey,
                BodyTypeNumeric = bodyType,
                BodySubTypeNumeric = bodySubType,
                SpeciesNumeric = species,
                AgeGenderFlags = ageGenderFlags,
                OutfitId = outfitId,
                TextureIndex = textureIndex,
                ShadowIndex = shadowIndex,
                NormalMapIndex = normalMapIndex,
                SpecularIndex = specularIndex,
                EmissionIndex = emissionIndex,
                ResourceTable = table,
                TextureRefs = textureRefs
            };
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse CASP: {ex.Message}";
            return false;
        }
    }

    private static Sims4Tgi ReadIgt(BinaryReader reader)
    {
        var instance = reader.ReadUInt64();
        var group = reader.ReadUInt32();
        var type = reader.ReadUInt32();
        return new Sims4Tgi(type, group, instance);
    }

    private static Sims4CasPartTextureRefs BuildTextureRefs(
        IReadOnlyList<Sims4Tgi> table,
        byte textureIndex,
        byte shadowIndex,
        byte normalMapIndex,
        byte specularIndex,
        byte emissionIndex)
    {
        var distinct = new List<DbpfResourceKey>();
        var set = new HashSet<DbpfResourceKey>();

        var diffuse = ResolveRef(table, textureIndex, distinct, set);
        var shadow = ResolveRef(table, shadowIndex, distinct, set);
        var normal = ResolveRef(table, normalMapIndex, distinct, set);
        var specular = ResolveRef(table, specularIndex, distinct, set);
        var emission = emissionIndex == byte.MaxValue ? null : ResolveRef(table, emissionIndex, distinct, set);

        return new Sims4CasPartTextureRefs
        {
            Diffuse = diffuse,
            Shadow = shadow,
            Normal = normal,
            Specular = specular,
            Emission = emission,
            AllDistinct = distinct
        };
    }

    private static DbpfResourceKey? ResolveRef(
        IReadOnlyList<Sims4Tgi> table,
        byte index,
        List<DbpfResourceKey> distinct,
        HashSet<DbpfResourceKey> set)
    {
        if (index >= table.Count)
        {
            return null;
        }

        var key = table[index].ToResourceKey();
        if (set.Add(key))
        {
            distinct.Add(key);
        }

        return key;
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
