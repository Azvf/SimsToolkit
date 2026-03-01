using System.Buffers.Binary;
using System.IO.Compression;
using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.SaveData.Formats;

public static class DbpfPackageReader
{
    private const uint DbpfSignature = 1179664964u;
    private const ushort CompressionDeleted = 65504;
    private const ushort CompressionZlib = 23106;

    public static IndexedPackageFile ReadPackage(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> header = stackalloc byte[96];
        FillExactly(stream, header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != DbpfSignature)
        {
            throw new InvalidDataException("Not a DBPF package file.");
        }

        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(36, 4));
        var indexPositionLow = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(40, 4));
        var indexRecordSize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(44, 4));
        var indexPosition = BinaryPrimitives.ReadUInt64LittleEndian(header.Slice(64, 8));
        if (entryCount == 0)
        {
            var emptyFileInfo = new FileInfo(filePath);
            return new IndexedPackageFile
            {
                FilePath = filePath,
                Length = emptyFileInfo.Length,
                LastWriteTimeUtc = emptyFileInfo.LastWriteTimeUtc,
                Entries = Array.Empty<PackageIndexEntry>()
            };
        }

        var finalIndexPosition = indexPosition != 0 ? (long)indexPosition : indexPositionLow;
        stream.Seek(finalIndexPosition, SeekOrigin.Begin);

        Span<byte> flagsBytes = stackalloc byte[4];
        FillExactly(stream, flagsBytes);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(flagsBytes);
        var constantType = (flags & 0x1) != 0;
        var constantGroup = (flags & 0x2) != 0;
        var constantInstanceEx = (flags & 0x4) != 0;

        var template = new byte[32];
        var constantWords = 0;
        if (constantType)
        {
            FillExactly(stream, template.AsSpan(0, 4));
            constantWords++;
        }

        if (constantGroup)
        {
            FillExactly(stream, template.AsSpan(4, 4));
            constantWords++;
        }

        if (constantInstanceEx)
        {
            FillExactly(stream, template.AsSpan(8, 4));
            constantWords++;
        }

        var overheadBytes = 4 + (constantWords * 4);
        var entryCountInt = checked((int)entryCount);
        var variableBytesPerEntry = 32 - (constantWords * 4);
        var totalVariableBytes = ResolveIndexVariableByteCount(
            filePath,
            stream,
            finalIndexPosition,
            indexRecordSize,
            entryCountInt,
            overheadBytes,
            variableBytesPerEntry);

        if (variableBytesPerEntry < 20 || variableBytesPerEntry > 32 || totalVariableBytes <= 0)
        {
            throw new InvalidDataException("Unsupported DBPF index record size.");
        }

        var entries = new PackageIndexEntry[entryCount];
        var indexBlock = new byte[totalVariableBytes];
        FillExactly(stream, indexBlock);
        for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            var variableBuffer = new byte[variableBytesPerEntry];
            Buffer.BlockCopy(indexBlock, entryIndex * variableBytesPerEntry, variableBuffer, 0, variableBytesPerEntry);

            var fullEntry = new byte[32];
            Buffer.BlockCopy(template, 0, fullEntry, 0, template.Length);

            var cursor = 0;
            if (!constantType)
            {
                Buffer.BlockCopy(variableBuffer, cursor, fullEntry, 0, 4);
                cursor += 4;
            }

            if (!constantGroup)
            {
                Buffer.BlockCopy(variableBuffer, cursor, fullEntry, 4, 4);
                cursor += 4;
            }

            if (!constantInstanceEx)
            {
                Buffer.BlockCopy(variableBuffer, cursor, fullEntry, 8, 4);
                cursor += 4;
            }

            Buffer.BlockCopy(variableBuffer, cursor, fullEntry, 12, variableBytesPerEntry - cursor);

            var type = BinaryPrimitives.ReadUInt32LittleEndian(fullEntry.AsSpan(0, 4));
            var group = BinaryPrimitives.ReadUInt32LittleEndian(fullEntry.AsSpan(4, 4));
            var instanceEx = BinaryPrimitives.ReadUInt32LittleEndian(fullEntry.AsSpan(8, 4));
            var instanceLow = BinaryPrimitives.ReadUInt32LittleEndian(fullEntry.AsSpan(12, 4));
            var position = BinaryPrimitives.ReadUInt32LittleEndian(fullEntry.AsSpan(16, 4));
            var sizeAndCompression = BinaryPrimitives.ReadUInt32LittleEndian(fullEntry.AsSpan(20, 4));
            var sizeDecompressed = BinaryPrimitives.ReadUInt32LittleEndian(fullEntry.AsSpan(24, 4));
            var compressionType = BinaryPrimitives.ReadUInt16LittleEndian(fullEntry.AsSpan(28, 2));

            entries[entryIndex] = new PackageIndexEntry
            {
                Type = type,
                Group = group,
                Instance = ((ulong)instanceEx << 32) | instanceLow,
                IsDeleted = compressionType == CompressionDeleted,
                DataOffset = position,
                CompressedSize = unchecked((int)(sizeAndCompression & 0x7FFFFFFF)),
                UncompressedSize = unchecked((int)sizeDecompressed),
                CompressionType = compressionType
            };
        }

        var fileInfo = new FileInfo(filePath);
        return new IndexedPackageFile
        {
            FilePath = filePath,
            Length = fileInfo.Length,
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
            Entries = entries
        };
    }

    public static bool TryReadResourceBytes(
        string packagePath,
        PackageIndexEntry entry,
        out byte[] bytes,
        out string? error)
    {
        bytes = Array.Empty<byte>();
        error = null;

        if (entry.IsDeleted)
        {
            error = "Deleted package entry.";
            return false;
        }

        try
        {
            using var stream = File.Open(packagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(entry.DataOffset, SeekOrigin.Begin);
            if (entry.CompressionType == 0)
            {
                bytes = new byte[entry.CompressedSize];
                FillExactly(stream, bytes);
                return true;
            }

            if (entry.CompressionType == CompressionZlib)
            {
                Span<byte> zlibHeader = stackalloc byte[2];
                FillExactly(stream, zlibHeader);
                if (zlibHeader[0] != 120)
                {
                    error = "Invalid ZLIB signature.";
                    return false;
                }

                using var deflate = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true);
                using var output = new MemoryStream(entry.UncompressedSize > 0 ? entry.UncompressedSize : 0);
                deflate.CopyTo(output);
                bytes = output.ToArray();
                return true;
            }

            error = $"Unsupported compression type: {entry.CompressionType}.";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Failed to read package resource: {ex.Message}";
            return false;
        }
    }

    private static int ResolveIndexVariableByteCount(
        string filePath,
        FileStream stream,
        long finalIndexPosition,
        uint indexRecordSize,
        int entryCount,
        int overheadBytes,
        int variableBytesPerEntry)
    {
        if (entryCount <= 0)
        {
            return 0;
        }

        var availableBytes = checked((int)Math.Min(int.MaxValue, stream.Length - finalIndexPosition - overheadBytes));
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

        throw new InvalidDataException($"Unsupported DBPF index record size for '{filePath}'.");
    }

    private static void FillExactly(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer.Slice(total));
            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected end of stream.");
            }

            total += read;
        }
    }

    private static void FillExactly(Stream stream, byte[] buffer)
    {
        FillExactly(stream, buffer.AsSpan());
    }
}
