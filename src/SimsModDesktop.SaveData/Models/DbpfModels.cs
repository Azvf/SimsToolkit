namespace SimsModDesktop.SaveData.Models;

public sealed record IndexedPackageFile
{
    public required string FilePath { get; init; }
    public long Length { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }
    public required PackageIndexEntry[] Entries { get; init; }
}

public sealed record PackageIndexEntry
{
    public uint Type { get; init; }
    public uint Group { get; init; }
    public ulong Instance { get; init; }
    public bool IsDeleted { get; init; }
    public long DataOffset { get; init; }
    public int CompressedSize { get; init; }
    public int UncompressedSize { get; init; }
    public ushort CompressionType { get; init; }
}
