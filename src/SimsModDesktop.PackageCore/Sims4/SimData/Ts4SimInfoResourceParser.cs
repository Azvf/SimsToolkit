namespace SimsModDesktop.PackageCore;

public sealed class Ts4SimInfoResourceParser : ITS4ResourceParser<Ts4SimInfoResource>
{
    public bool TryParse(DbpfResourceKey key, ReadOnlySpan<byte> bytes, out Ts4SimInfoResource result, out string? error)
    {
        result = null!;
        error = null;

        if (key.Type != Sims4ResourceTypeRegistry.SimInfo)
        {
            error = "Resource is not SIM_INFO.";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            var version = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            var tgiTablePosition = reader.BaseStream.Position + offset;

            var physique = new float[8];
            for (var i = 0; i < physique.Length; i++)
            {
                physique[i] = reader.ReadSingle();
            }

            var ageFlags = reader.ReadUInt32();
            var genderFlags = reader.ReadUInt32();
            var species = version > 18 ? reader.ReadUInt32() : 0u;
            if (version > 18)
            {
                _ = reader.ReadUInt32();
            }

            if (version >= 32)
            {
                SkipPronouns(reader);
            }

            var skinToneRef = reader.ReadUInt64();
            var skinToneShift = version >= 28 ? reader.ReadSingle() : 0f;

            if (version >= 24)
            {
                var peltCount = reader.ReadByte();
                for (var i = 0; i < peltCount; i++)
                {
                    _ = reader.ReadUInt64();
                    _ = reader.ReadUInt32();
                }
            }

            var tgiRefs = ReadTgiReferenceTable(reader, tgiTablePosition);
            reader.BaseStream.Position = 8 + (8 * sizeof(float)) + 8 + (version > 18 ? 8 : 0);
            if (version >= 32)
            {
                SkipPronouns(reader);
            }

            _ = reader.ReadUInt64();
            if (version >= 28)
            {
                _ = reader.ReadSingle();
            }

            if (version >= 24)
            {
                var peltCount = reader.ReadByte();
                for (var i = 0; i < peltCount; i++)
                {
                    _ = reader.ReadUInt64();
                    _ = reader.ReadUInt32();
                }
            }

            var sculpts = ReadTgiReferenceList(reader, tgiRefs, Sims4ResourceTypeRegistry.Sculpt);
            var faceModifiers = ReadModifierList(reader, tgiRefs);
            var bodyModifiers = ReadModifierList(reader, tgiRefs);

            _ = reader.ReadUInt32();
            _ = reader.ReadSingle();
            _ = reader.ReadUInt64();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();

            var outfits = ReadOutfits(reader, tgiRefs);

            var geneticSculpts = ReadTgiReferenceList(reader, tgiRefs, Sims4ResourceTypeRegistry.Sculpt);
            var geneticFaceModifiers = ReadModifierList(reader, tgiRefs);
            var geneticBodyModifiers = ReadModifierList(reader, tgiRefs);

            var geneticPhysique = new float[4];
            for (var i = 0; i < geneticPhysique.Length; i++)
            {
                geneticPhysique[i] = reader.ReadSingle();
            }

            var geneticParts = ReadGeneticParts(reader, tgiRefs);
            if (version >= 32)
            {
                _ = ReadGeneticParts(reader, tgiRefs);
            }

            _ = reader.ReadUInt32();
            _ = reader.ReadSingle();
            _ = reader.ReadByte();
            _ = reader.ReadUInt64();
            if (version >= 32)
            {
                _ = reader.ReadBytes(3);
            }

            var traitCount = reader.ReadByte();
            var traits = new ulong[traitCount];
            for (var i = 0; i < traitCount; i++)
            {
                traits[i] = reader.ReadUInt64();
            }

            result = new Ts4SimInfoResource
            {
                Version = version,
                Physique = physique,
                AgeGenderAgeFlags = ageFlags,
                AgeGenderGenderFlags = genderFlags,
                Species = species,
                SkinToneRef = skinToneRef,
                SkinToneShift = skinToneShift,
                Sculpts = sculpts,
                FaceModifiers = faceModifiers,
                BodyModifiers = bodyModifiers,
                Outfits = outfits,
                GeneticSculpts = geneticSculpts,
                GeneticFaceModifiers = geneticFaceModifiers,
                GeneticBodyModifiers = geneticBodyModifiers,
                GeneticPhysique = geneticPhysique,
                GeneticParts = geneticParts,
                TraitRefs = traits
            };

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse SIM_INFO: {ex.Message}";
            return false;
        }
    }

    private static IReadOnlyList<DbpfResourceKey> ReadTgiReferenceTable(BinaryReader reader, long tgiTablePosition)
    {
        var current = reader.BaseStream.Position;
        reader.BaseStream.Position = tgiTablePosition;

        var count = reader.ReadByte();
        var refs = new DbpfResourceKey[count];
        for (var i = 0; i < count; i++)
        {
            refs[i] = Ts4BinaryReaders.ReadIgt(reader);
        }

        reader.BaseStream.Position = current;
        return refs;
    }

    private static void SkipPronouns(BinaryReader reader)
    {
        var pronounCount = reader.ReadInt32();
        if (pronounCount < 0)
        {
            throw new InvalidDataException("Invalid pronoun count.");
        }

        for (var i = 0; i < pronounCount; i++)
        {
            var grammaticalCase = reader.ReadUInt32();
            if (grammaticalCase > 0)
            {
                _ = reader.ReadString();
            }
        }
    }

    private static IReadOnlyList<DbpfResourceKey> ReadTgiReferenceList(BinaryReader reader, IReadOnlyList<DbpfResourceKey> refs, uint type)
    {
        var count = reader.ReadByte();
        var values = new List<DbpfResourceKey>(count);
        for (var i = 0; i < count; i++)
        {
            var index = reader.ReadByte();
            if (index < refs.Count)
            {
                var key = refs[index];
                values.Add(new DbpfResourceKey(type, key.Group, key.Instance));
            }
        }

        return values;
    }

    private static IReadOnlyList<Ts4SimModifierValue> ReadModifierList(BinaryReader reader, IReadOnlyList<DbpfResourceKey> refs)
    {
        var count = reader.ReadByte();
        var values = new List<Ts4SimModifierValue>(count);
        for (var i = 0; i < count; i++)
        {
            var index = reader.ReadByte();
            var weight = reader.ReadSingle();
            if (index < refs.Count)
            {
                var key = refs[index];
                values.Add(new Ts4SimModifierValue(new DbpfResourceKey(Sims4ResourceTypeRegistry.SimModifier, key.Group, key.Instance), weight));
            }
        }

        return values;
    }

    private static IReadOnlyList<Ts4OutfitEntry> ReadOutfits(BinaryReader reader, IReadOnlyList<DbpfResourceKey> refs)
    {
        var count = reader.ReadUInt32();
        var values = new List<Ts4OutfitEntry>((int)count);
        for (var i = 0; i < count; i++)
        {
            var category = reader.ReadByte();
            _ = reader.ReadUInt32();
            var outfitDescCount = reader.ReadUInt32();
            for (var j = 0; j < outfitDescCount; j++)
            {
                var outfitId = reader.ReadUInt64();
                var outfitFlags = reader.ReadUInt64();
                var created = reader.ReadUInt64();
                _ = reader.ReadBoolean();
                var partCount = reader.ReadUInt32();
                var parts = new List<Ts4OutfitPartRef>((int)partCount);
                for (var p = 0; p < partCount; p++)
                {
                    var tgiIndex = reader.ReadByte();
                    var bodyType = reader.ReadUInt32();
                    var colorShift = reader.ReadUInt64();
                    if (tgiIndex < refs.Count)
                    {
                        var part = refs[tgiIndex];
                        parts.Add(new Ts4OutfitPartRef(new DbpfResourceKey(Sims4ResourceTypeRegistry.CasPart, part.Group, part.Instance), bodyType, colorShift));
                    }
                }

                values.Add(new Ts4OutfitEntry
                {
                    OutfitId = outfitId,
                    OutfitFlags = outfitFlags,
                    CreatedTicks = created,
                    Category = category,
                    Parts = parts
                });
            }
        }

        return values;
    }

    private static IReadOnlyList<Ts4OutfitPartRef> ReadGeneticParts(BinaryReader reader, IReadOnlyList<DbpfResourceKey> refs)
    {
        var count = reader.ReadByte();
        var parts = new List<Ts4OutfitPartRef>(count);
        for (var i = 0; i < count; i++)
        {
            var tgiIndex = reader.ReadByte();
            var bodyType = reader.ReadUInt32();
            if (tgiIndex < refs.Count)
            {
                var part = refs[tgiIndex];
                parts.Add(new Ts4OutfitPartRef(new DbpfResourceKey(Sims4ResourceTypeRegistry.CasPart, part.Group, part.Instance), bodyType, 0));
            }
        }

        return parts;
    }
}
