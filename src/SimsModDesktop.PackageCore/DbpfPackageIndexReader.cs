using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Frozen;
using Microsoft.Win32.SafeHandles;

namespace SimsModDesktop.PackageCore;

public static class DbpfPackageIndexReader
{
    private const uint DbpfSignature = 1179664964u;
    private const int HeaderLength = 96;

    public static DbpfPackageIndex ReadPackageIndex(string filePath, DbpfReadOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        options ??= DbpfReadOptions.Default;

        using var handle = File.OpenHandle(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            FileOptions.RandomAccess);

        Span<byte> header = stackalloc byte[HeaderLength];
        ReadExactly(handle, 0, header);

        if (options.ValidateSignature &&
            BinaryPrimitives.ReadUInt32LittleEndian(header) != DbpfSignature)
        {
            throw new InvalidDataException("Not a DBPF package file.");
        }

        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(36, 4));
        var indexPositionLow = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(40, 4));
        var indexRecordSize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(44, 4));
        var indexPosition = BinaryPrimitives.ReadUInt64LittleEndian(header.Slice(64, 8));
        var finalIndexPosition = indexPosition != 0 ? (long)indexPosition : indexPositionLow;
        var fileLength = RandomAccess.GetLength(handle);
        var fileInfo = new FileInfo(filePath);
        var fingerprint = new DbpfPackageFingerprint(fileInfo.Length, fileInfo.LastWriteTimeUtc.Ticks);

        if (entryCount == 0)
        {
            return new DbpfPackageIndex
            {
                FilePath = filePath,
                Fingerprint = fingerprint,
                Entries = Array.Empty<DbpfIndexEntry>(),
                TypeBuckets = new Dictionary<uint, DbpfTypeBucket>().ToFrozenDictionary()
            };
        }

        Span<byte> flagsBytes = stackalloc byte[4];
        ReadExactly(handle, finalIndexPosition, flagsBytes);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(flagsBytes);
        var constantType = (flags & 0x1) != 0;
        var constantGroup = (flags & 0x2) != 0;
        var constantInstanceEx = (flags & 0x4) != 0;

        uint fixedType = 0;
        uint fixedGroup = 0;
        uint fixedInstanceEx = 0;
        var cursorOffset = finalIndexPosition + 4;
        var constantWords = 0;

        if (constantType)
        {
            Span<byte> buffer = stackalloc byte[4];
            ReadExactly(handle, cursorOffset, buffer);
            fixedType = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            cursorOffset += 4;
            constantWords++;
        }

        if (constantGroup)
        {
            Span<byte> buffer = stackalloc byte[4];
            ReadExactly(handle, cursorOffset, buffer);
            fixedGroup = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            cursorOffset += 4;
            constantWords++;
        }

        if (constantInstanceEx)
        {
            Span<byte> buffer = stackalloc byte[4];
            ReadExactly(handle, cursorOffset, buffer);
            fixedInstanceEx = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
            cursorOffset += 4;
            constantWords++;
        }

        var overheadBytes = 4 + (constantWords * 4);
        var entryCountInt = checked((int)entryCount);
        var variableBytesPerEntry = 32 - (constantWords * 4);
        var totalVariableBytes = ResolveIndexVariableByteCount(
            filePath,
            fileLength,
            finalIndexPosition,
            indexRecordSize,
            entryCountInt,
            overheadBytes,
            variableBytesPerEntry,
            options.StrictIndexValidation);

        if (variableBytesPerEntry < 20 || variableBytesPerEntry > 32 || totalVariableBytes <= 0)
        {
            throw new InvalidDataException("Unsupported DBPF index record size.");
        }

        var rented = options.UsePooledBuffers
            ? ArrayPool<byte>.Shared.Rent(totalVariableBytes)
            : new byte[totalVariableBytes];

        try
        {
            var indexBytes = rented.AsSpan(0, totalVariableBytes);
            ReadExactly(handle, finalIndexPosition + overheadBytes, indexBytes);

            var entries = new DbpfIndexEntry[entryCountInt];
            for (var entryIndex = 0; entryIndex < entryCountInt; entryIndex++)
            {
                var entryBytes = indexBytes.Slice(entryIndex * variableBytesPerEntry, variableBytesPerEntry);
                var cursor = 0;

                var type = constantType
                    ? fixedType
                    : ReadUInt32(entryBytes, ref cursor);
                var group = constantGroup
                    ? fixedGroup
                    : ReadUInt32(entryBytes, ref cursor);
                var instanceEx = constantInstanceEx
                    ? fixedInstanceEx
                    : ReadUInt32(entryBytes, ref cursor);

                var instanceLow = ReadUInt32(entryBytes, ref cursor);
                var dataOffset = ReadUInt32(entryBytes, ref cursor);
                var sizeAndCompression = ReadUInt32(entryBytes, ref cursor);
                var uncompressedSize = ReadUInt32(entryBytes, ref cursor);
                var compressionType = ReadUInt16(entryBytes, ref cursor);

                entries[entryIndex] = new DbpfIndexEntry(
                    type,
                    group,
                    ((ulong)instanceEx << 32) | instanceLow,
                    dataOffset,
                    unchecked((int)(sizeAndCompression & 0x7FFFFFFF)),
                    unchecked((int)uncompressedSize),
                    compressionType,
                    compressionType == CompressionCodecs.Deleted);
            }

            return new DbpfPackageIndex
            {
                FilePath = filePath,
                Fingerprint = fingerprint,
                Entries = entries,
                TypeBuckets = BuildTypeBuckets(entries)
            };
        }
        finally
        {
            if (options.UsePooledBuffers)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static FrozenDictionary<uint, DbpfTypeBucket> BuildTypeBuckets(DbpfIndexEntry[] entries)
    {
        var typeMaps = new Dictionary<uint, Dictionary<ulong, List<int>>>();
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (entry.IsDeleted)
            {
                continue;
            }

            if (!typeMaps.TryGetValue(entry.Type, out var instanceMap))
            {
                instanceMap = new Dictionary<ulong, List<int>>();
                typeMaps[entry.Type] = instanceMap;
            }

            if (!instanceMap.TryGetValue(entry.Instance, out var indexes))
            {
                indexes = new List<int>();
                instanceMap[entry.Instance] = indexes;
            }

            indexes.Add(index);
        }

        var frozen = new Dictionary<uint, DbpfTypeBucket>(typeMaps.Count);
        foreach (var pair in typeMaps)
        {
            var instances = new Dictionary<ulong, int[]>(pair.Value.Count);
            foreach (var instancePair in pair.Value)
            {
                instances[instancePair.Key] = instancePair.Value.ToArray();
            }

            frozen[pair.Key] = new DbpfTypeBucket
            {
                InstanceToEntryIndexes = instances.ToFrozenDictionary()
            };
        }

        return frozen.ToFrozenDictionary();
    }

    private static int ResolveIndexVariableByteCount(
        string filePath,
        long fileLength,
        long finalIndexPosition,
        uint indexRecordSize,
        int entryCount,
        int overheadBytes,
        int variableBytesPerEntry,
        bool strictValidation)
    {
        if (entryCount <= 0)
        {
            return 0;
        }

        var availableBytes = checked((int)Math.Min(int.MaxValue, fileLength - finalIndexPosition - overheadBytes));
        var recordSizeInt = checked((int)indexRecordSize);

        if (recordSizeInt >= overheadBytes)
        {
            var candidateTotal = recordSizeInt - overheadBytes;
            if (candidateTotal > 0 &&
                candidateTotal % entryCount == 0 &&
                candidateTotal / entryCount == variableBytesPerEntry &&
                candidateTotal <= availableBytes)
            {
                return candidateTotal;
            }
        }

        if (recordSizeInt == variableBytesPerEntry)
        {
            var candidateTotal = checked(variableBytesPerEntry * entryCount);
            if (candidateTotal <= availableBytes)
            {
                return candidateTotal;
            }
        }

        if (recordSizeInt == 32)
        {
            var candidatePerEntry = recordSizeInt - (overheadBytes - 4);
            if (candidatePerEntry == variableBytesPerEntry)
            {
                var candidateTotal = checked(candidatePerEntry * entryCount);
                if (candidateTotal <= availableBytes)
                {
                    return candidateTotal;
                }
            }
        }

        if (!strictValidation)
        {
            return Math.Min(availableBytes, checked(variableBytesPerEntry * entryCount));
        }

        throw new InvalidDataException($"Unsupported DBPF index record size for '{filePath}'.");
    }

    private static void ReadExactly(SafeFileHandle handle, long offset, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = RandomAccess.Read(handle, buffer.Slice(total), offset + total);
            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected end of stream.");
            }

            total += read;
        }
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> source, ref int cursor)
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(cursor, 4));
        cursor += 4;
        return value;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> source, ref int cursor)
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(cursor, 2));
        cursor += 2;
        return value;
    }
}
