namespace SimsModDesktop.PackageCore;

public static class Sims4CasPartExtendedParser
{
    public static bool TryParse(
        DbpfResourceKey resourceKey,
        ReadOnlySpan<byte> bytes,
        out Ts4CasPartExtended result,
        out string? error)
    {
        result = null!;
        error = null;

        if (!Sims4CasPartParser.TryParse(resourceKey, bytes, out var baseInfo, out error))
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            var version = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadInt32();
            _ = reader.ReadLengthPrefixedStringUtf16BigEndian();
            _ = reader.ReadSingle();
            _ = reader.ReadUInt16();
            _ = reader.ReadUInt32();
            var materialHash = reader.ReadUInt32();
            var parameterFlags = reader.ReadByte();

            var parameterFlags2 = (byte)0;
            if (version >= 39)
            {
                parameterFlags2 = reader.ReadByte();
            }

            if (version >= 50)
            {
                _ = reader.ReadUInt16();
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
                _ = version >= 37 ? reader.ReadUInt32() : reader.ReadUInt16();
            }

            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            if (version >= 0x2B)
            {
                _ = reader.ReadUInt32();
            }

            var textureSpace = reader.ReadByte();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            if (version >= 32)
            {
                _ = reader.ReadUInt32();
            }

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

            _ = reader.ReadByte();
            _ = reader.ReadByte();
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

            _ = reader.ReadByte();
            _ = reader.ReadByte();
            _ = reader.ReadInt32();

            var lodEntries = new List<Ts4CasPartLodEntry>();
            var lodCount = reader.ReadByte();
            var lodRawEntries = new List<(byte Lod, byte[] Indexes)>();
            for (var lodIndex = 0; lodIndex < lodCount; lodIndex++)
            {
                var lod = reader.ReadByte();
                _ = reader.ReadUInt32();
                var numAssets = reader.ReadByte();
                for (var index = 0; index < numAssets; index++)
                {
                    _ = reader.ReadUInt64();
                    _ = reader.ReadUInt32();
                }

                var indexCount = reader.ReadByte();
                var meshIndexes = reader.ReadBytes(indexCount);
                lodRawEntries.Add((lod, meshIndexes));
            }

            var numSlotKeys = reader.ReadByte();
            var slotKeys = reader.ReadBytes(numSlotKeys);

            _ = reader.ReadByte();
            _ = reader.ReadByte();
            _ = reader.ReadByte();
            var regionMapIndex = reader.ReadByte();

            var overrideCount = reader.ReadByte();
            var overrides = new List<Ts4CasPartOverrideEntry>(overrideCount);
            for (var index = 0; index < overrideCount; index++)
            {
                var overrideIndex = reader.ReadByte();
                var overrideValue = reader.ReadUInt32();
                overrides.Add(new Ts4CasPartOverrideEntry(overrideIndex, overrideValue));
            }

            _ = reader.ReadByte();
            _ = reader.ReadByte();
            if (version >= 27)
            {
                _ = reader.ReadUInt32();
            }

            if (version >= 29)
            {
                _ = reader.ReadByte();
            }

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
                var instance = reader.ReadUInt64();
                var group = reader.ReadUInt32();
                var type = reader.ReadUInt32();
                table.Add(new Sims4Tgi(type, group, instance));
            }

            foreach (var rawLod in lodRawEntries)
            {
                var meshes = new List<DbpfResourceKey>(rawLod.Indexes.Length);
                foreach (var meshIndex in rawLod.Indexes)
                {
                    if (meshIndex < table.Count)
                    {
                        meshes.Add(table[meshIndex].ToResourceKey());
                    }
                }

                lodEntries.Add(new Ts4CasPartLodEntry(rawLod.Lod, meshes));
            }

            DbpfResourceKey? regionMapRef = regionMapIndex < table.Count
                ? table[regionMapIndex].ToResourceKey()
                : null;

            result = new Ts4CasPartExtended
            {
                BaseInfo = baseInfo,
                ParameterFlags = parameterFlags,
                ParameterFlags2 = parameterFlags2,
                MaterialHash = materialHash,
                TextureSpace = textureSpace,
                RegionMapIndex = regionMapIndex,
                LodEntries = lodEntries,
                SlotKeys = slotKeys,
                Overrides = overrides,
                RegionMapRef = regionMapRef
            };

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse extended CASP: {ex.Message}";
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
