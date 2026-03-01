using System.Collections.Frozen;
using System.Buffers;

namespace SimsModDesktop.PackageCore;

public readonly record struct DbpfResourceKey(uint Type, uint Group, ulong Instance);

public readonly record struct TypeInstanceKey(uint Type, ulong Instance);

public readonly record struct DbpfPackageFingerprint(long Length, long LastWriteUtcTicks);

public readonly record struct DbpfIndexEntry(
    uint Type,
    uint Group,
    ulong Instance,
    long DataOffset,
    int CompressedSize,
    int UncompressedSize,
    ushort CompressionType,
    bool IsDeleted);

public sealed class DbpfTypeBucket
{
    public required FrozenDictionary<ulong, int[]> InstanceToEntryIndexes { get; init; }
}

public sealed class DbpfPackageIndex
{
    public required string FilePath { get; init; }
    public required DbpfPackageFingerprint Fingerprint { get; init; }
    public required DbpfIndexEntry[] Entries { get; init; }
    public required FrozenDictionary<uint, DbpfTypeBucket> TypeBuckets { get; init; }
}

public readonly record struct ResourceLocation(
    string FilePath,
    int EntryIndex,
    DbpfIndexEntry Entry);

public sealed class DbpfReadOptions
{
    public static DbpfReadOptions Default { get; } = new();

    public bool UsePooledBuffers { get; init; } = true;
    public bool ValidateSignature { get; init; } = true;
    public bool StrictIndexValidation { get; init; } = true;
}

public sealed class DbpfCatalogBuildOptions
{
    public int MaxDegreeOfParallelism { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
    public bool EnablePersistentCache { get; init; } = true;
    public string? CacheFilePath { get; init; }
    public IReadOnlyCollection<uint>? SupportedInstanceTypes { get; init; }
}

public sealed class DbpfCatalogIssue
{
    public required string FilePath { get; init; }
    public required string Message { get; init; }
}

public sealed class DbpfCatalogSnapshot
{
    public required string RootPath { get; init; }
    public required IReadOnlyList<DbpfPackageIndex> Packages { get; init; }
    public required FrozenDictionary<DbpfResourceKey, ResourceLocation[]> ExactIndex { get; init; }
    public required FrozenDictionary<TypeInstanceKey, ResourceLocation[]> TypeInstanceIndex { get; init; }
    public required FrozenDictionary<ulong, ResourceLocation[]> SupportedInstanceIndex { get; init; }
    public required IReadOnlyList<DbpfCatalogIssue> Issues { get; init; }
}

public interface IDbpfPackageCatalog
{
    Task<DbpfCatalogSnapshot> BuildSnapshotAsync(
        string rootPath,
        DbpfCatalogBuildOptions? options = null,
        CancellationToken cancellationToken = default);
}

public interface IDbpfResourceReader
{
    DbpfPackageReadSession OpenSession(string packagePath);
}

public sealed class DbpfResourceReadResult
{
    public bool Success { get; init; }
    public byte[] Bytes { get; init; } = Array.Empty<byte>();
    public string? Error { get; init; }
}

public sealed class DbpfPackageReadSession : IDisposable
{
    private readonly FileStream _stream;

    internal DbpfPackageReadSession(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        _stream = File.Open(packagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    }

    public bool TryReadBytes(
        DbpfIndexEntry entry,
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
            _stream.Seek(entry.DataOffset, SeekOrigin.Begin);
            if (entry.CompressionType == CompressionCodecs.None)
            {
                bytes = new byte[entry.CompressedSize];
                FillExactly(_stream, bytes);
                return true;
            }

            if (entry.CompressionType == CompressionCodecs.Zlib)
            {
                var compressed = new byte[entry.CompressedSize];
                FillExactly(_stream, compressed);
                using var input = new MemoryStream(compressed, writable: false);
                using var zlib = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress, leaveOpen: false);
                using var output = new MemoryStream(entry.UncompressedSize > 0 ? entry.UncompressedSize : 0);
                zlib.CopyTo(output);
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

    public bool TryReadInto(
        DbpfIndexEntry entry,
        IBufferWriter<byte> destination,
        out string? error)
    {
        if (!TryReadBytes(entry, out var bytes, out error))
        {
            return false;
        }

        var span = destination.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        destination.Advance(bytes.Length);
        return true;
    }

    public void Dispose()
    {
        _stream.Dispose();
    }

    private static void FillExactly(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected end of stream.");
            }

            total += read;
        }
    }
}

public sealed class DbpfResourceReader : IDbpfResourceReader
{
    public DbpfPackageReadSession OpenSession(string packagePath)
    {
        return new DbpfPackageReadSession(packagePath);
    }
}

internal static class CompressionCodecs
{
    public const ushort None = 0;
    public const ushort Deleted = 65504;
    public const ushort Zlib = 23106;
}
