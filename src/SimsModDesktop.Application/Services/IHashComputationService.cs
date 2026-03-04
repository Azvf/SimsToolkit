namespace SimsModDesktop.Application.Services;

public interface IHashComputationService
{
    Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default);

    Task<string> ComputeFileHashAsync(
        string filePath,
        HashAlgorithm algorithm,
        CancellationToken cancellationToken = default);

    Task<string> ComputeFilePrefixHashAsync(
        string filePath,
        int prefixBytes,
        CancellationToken cancellationToken = default);

    Task<bool> AreFilesIdenticalAsync(
        string path1,
        string path2,
        CancellationToken cancellationToken = default);

    Task<bool> AreFilePrefixesIdenticalAsync(
        string path1,
        string path2,
        int prefixBytes,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileHashResult>> ComputeFileHashesAsync(
        HashBatchRequest request,
        IProgress<HashProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileHashResult>> ComputeFileHashesAsync(
        IReadOnlyList<string> filePaths,
        IProgress<HashProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileHashResult>> ComputeFilePrefixHashesAsync(
        HashBatchRequest request,
        int prefixBytes,
        IProgress<HashProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileHashResult>> ComputeFilePrefixHashesAsync(
        IReadOnlyList<string> filePaths,
        int prefixBytes,
        IProgress<HashProgress>? progress = null,
        CancellationToken cancellationToken = default);

    IReadOnlyList<HashAlgorithm> SupportedAlgorithms { get; }
}

public sealed record HashBatchRequest
{
    public IReadOnlyList<string> FilePaths { get; init; } = Array.Empty<string>();
    public int? WorkerCount { get; init; }
}

public sealed record FileHashResult
{
    public required string FilePath { get; init; }
    public required string Hash { get; init; }
    public long FileSize { get; init; }
    public DateTime LastModified { get; init; }
    public long ElapsedMilliseconds { get; init; }
    public Exception? Exception { get; init; }
    public bool IsSuccess => Exception is null;
}

public sealed record HashProgress
{
    public required int ProcessedCount { get; init; }
    public required int TotalCount { get; init; }
    public string? CurrentFile { get; init; }
    public int PercentComplete => TotalCount > 0 ? (ProcessedCount * 100) / TotalCount : 0;
    public long ProcessedBytes { get; init; }
    public long TotalBytes { get; init; }
}

public enum HashAlgorithm
{
    MD5,
    SHA1,
    SHA256,
    SHA512
}
