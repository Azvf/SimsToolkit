namespace SimsModDesktop.PackageCore;

public sealed class Ts4Geom
{
    public required int ContextVersion { get; init; }
    public required int Version { get; init; }
    public required uint ShaderHash { get; init; }
    public required int MergeGroup { get; init; }
    public required int SortOrder { get; init; }
    public required int VertexCount { get; init; }
    public required int FacePointCount { get; init; }
    public required int FaceCount { get; init; }
    public required int SubMeshCount { get; init; }
    public required byte BytesPerFacePoint { get; init; }
    public required IReadOnlyList<Ts4GeomVertexFormat> VertexFormats { get; init; }
    public required IReadOnlyList<uint> BoneHashes { get; init; }
    public required int UvStitchCount { get; init; }
    public required int SeamStitchCount { get; init; }
    public required int SlotrayIntersectionCount { get; init; }
    public required IReadOnlyList<DbpfResourceKey> TgiTable { get; init; }
}

public readonly record struct Ts4GeomVertexFormat(int DataType, int SubType, byte BytesPer);

public sealed class Ts4GeomParser : ITS4ResourceParser<Ts4Geom>
{
    private const uint GeomMagic = 0x4D4F4547; // "GEOM" little-endian

    public bool TryParse(DbpfResourceKey key, ReadOnlySpan<byte> bytes, out Ts4Geom result, out string? error)
    {
        result = null!;
        error = null;

        if (key.Type != Sims4ResourceTypeRegistry.Geom)
        {
            error = "Resource is not GEOM.";
            return false;
        }

        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var reader = new BinaryReader(stream);

            var contextVersion = reader.ReadInt32();
            _ = reader.ReadInt32(); // count
            _ = reader.ReadInt32(); // ind3
            _ = reader.ReadInt32(); // extCount
            _ = reader.ReadInt32(); // intCount
            _ = Ts4BinaryReaders.ReadTgi(reader); // dummy TGI
            _ = reader.ReadInt32(); // abspos
            _ = reader.ReadInt32(); // meshsize
            var magic = reader.ReadUInt32();
            if (magic != GeomMagic)
            {
                throw new InvalidDataException("Invalid GEOM magic.");
            }

            var version = reader.ReadInt32();
            _ = reader.ReadInt32(); // TGI offset
            _ = reader.ReadInt32(); // TGI size
            var shaderHash = reader.ReadUInt32();
            if (shaderHash != 0)
            {
                var mtnfSize = reader.ReadInt32();
                if (mtnfSize < 0)
                {
                    throw new InvalidDataException("Invalid GEOM MTNF size.");
                }

                SkipBytes(reader, mtnfSize);
            }

            var mergeGroup = reader.ReadInt32();
            var sortOrder = reader.ReadInt32();
            var vertexCount = reader.ReadInt32();
            var formatCount = reader.ReadInt32();
            if (vertexCount < 0 || formatCount < 0 || formatCount > 64)
            {
                throw new InvalidDataException("Invalid GEOM vertex section.");
            }

            var vertexFormats = new List<Ts4GeomVertexFormat>(formatCount);
            var bytesPerVertex = 0;
            for (var i = 0; i < formatCount; i++)
            {
                var dataType = reader.ReadInt32();
                var subType = reader.ReadInt32();
                var bytesPer = reader.ReadByte();
                vertexFormats.Add(new Ts4GeomVertexFormat(dataType, subType, bytesPer));
                bytesPerVertex = checked(bytesPerVertex + bytesPer);
            }

            SkipBytes(reader, checked((long)vertexCount * bytesPerVertex));

            var subMeshCount = reader.ReadInt32();
            var bytesPerFacePoint = reader.ReadByte();
            var facePointCount = reader.ReadInt32();
            if (subMeshCount < 0 || facePointCount < 0 || bytesPerFacePoint == 0)
            {
                throw new InvalidDataException("Invalid GEOM face section.");
            }

            var faceByteLength = checked((long)facePointCount * bytesPerFacePoint);
            SkipBytes(reader, faceByteLength);

            var uvStitchCount = 0;
            var seamStitchCount = 0;
            var slotrayCount = 0;
            if (version == 5)
            {
                _ = reader.ReadInt32(); // skcon index
            }
            else if (version >= 12)
            {
                uvStitchCount = reader.ReadInt32();
                if (uvStitchCount < 0)
                {
                    throw new InvalidDataException("Invalid GEOM UV stitch count.");
                }

                for (var i = 0; i < uvStitchCount; i++)
                {
                    _ = reader.ReadInt32(); // vertexIndex
                    var count = reader.ReadInt32();
                    if (count < 0)
                    {
                        throw new InvalidDataException("Invalid GEOM UV stitch pair count.");
                    }

                    SkipBytes(reader, checked((long)count * sizeof(float) * 2));
                }

                if (version >= 13)
                {
                    seamStitchCount = reader.ReadInt32();
                    if (seamStitchCount < 0)
                    {
                        throw new InvalidDataException("Invalid GEOM seam stitch count.");
                    }

                    SkipBytes(reader, checked((long)seamStitchCount * 6));
                }

                slotrayCount = reader.ReadInt32();
                if (slotrayCount < 0)
                {
                    throw new InvalidDataException("Invalid GEOM slotray intersection count.");
                }

                var slotraySize = version >= 14 ? 66 : 63;
                SkipBytes(reader, checked((long)slotrayCount * slotraySize));
            }

            var boneHashCount = reader.ReadInt32();
            if (boneHashCount < 0)
            {
                throw new InvalidDataException("Invalid GEOM bone hash count.");
            }

            var boneHashes = new uint[boneHashCount];
            for (var i = 0; i < boneHashCount; i++)
            {
                boneHashes[i] = reader.ReadUInt32();
            }

            if (version >= 15)
            {
                var geometryStateCount = reader.ReadInt32();
                if (geometryStateCount < 0)
                {
                    throw new InvalidDataException("Invalid GEOM geometry state count.");
                }

                SkipBytes(reader, checked((long)geometryStateCount * 20));
            }

            var tgiTable = Array.Empty<DbpfResourceKey>();
            if (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                var tgiCount = reader.ReadInt32();
                if (tgiCount < 0)
                {
                    throw new InvalidDataException("Invalid GEOM TGI table count.");
                }

                tgiTable = new DbpfResourceKey[tgiCount];
                for (var i = 0; i < tgiCount; i++)
                {
                    tgiTable[i] = Ts4BinaryReaders.ReadTgi(reader);
                }
            }

            result = new Ts4Geom
            {
                ContextVersion = contextVersion,
                Version = version,
                ShaderHash = shaderHash,
                MergeGroup = mergeGroup,
                SortOrder = sortOrder,
                VertexCount = vertexCount,
                FacePointCount = facePointCount,
                FaceCount = facePointCount / 3,
                SubMeshCount = subMeshCount,
                BytesPerFacePoint = bytesPerFacePoint,
                VertexFormats = vertexFormats,
                BoneHashes = boneHashes,
                UvStitchCount = uvStitchCount,
                SeamStitchCount = seamStitchCount,
                SlotrayIntersectionCount = slotrayCount,
                TgiTable = tgiTable
            };
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to parse GEOM: {ex.Message}";
            return false;
        }
    }

    private static void SkipBytes(BinaryReader reader, long count)
    {
        if (count <= 0)
        {
            return;
        }

        var remaining = reader.BaseStream.Length - reader.BaseStream.Position;
        if (count > remaining)
        {
            throw new EndOfStreamException("Unexpected end of stream.");
        }

        reader.BaseStream.Seek(count, SeekOrigin.Current);
    }
}
