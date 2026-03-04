using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Services;
using HashAlgorithmEnum = SimsModDesktop.Application.Services.HashAlgorithm;

namespace SimsModDesktop.Infrastructure.Services;

public sealed class CrossPlatformHashComputationService : IHashComputationService
{
    private readonly ILogger<CrossPlatformHashComputationService> _logger;

    public CrossPlatformHashComputationService(ILogger<CrossPlatformHashComputationService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<HashAlgorithmEnum> SupportedAlgorithms =>
        [HashAlgorithmEnum.MD5, HashAlgorithmEnum.SHA1, HashAlgorithmEnum.SHA256, HashAlgorithmEnum.SHA512];

    public Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return ComputeFileHashAsync(filePath, HashAlgorithmEnum.MD5, cancellationToken);
    }

    public async Task<string> ComputeFileHashAsync(
        string filePath,
        HashAlgorithmEnum algorithm,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            return await ComputeStreamHashAsync(stream, algorithm, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute hash for file: {FilePath}", filePath);
            throw;
        }
    }

    public async Task<string> ComputeFilePrefixHashAsync(
        string filePath,
        int prefixBytes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        if (prefixBytes <= 0)
        {
            throw new ArgumentException("Prefix bytes must be greater than 0", nameof(prefixBytes));
        }

        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            var buffer = new byte[Math.Min(prefixBytes, (int)Math.Max(0, stream.Length))];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new InvalidOperationException($"Failed to read prefix from file: {filePath}");
            }

            using var hashAlgorithm = CreateHashAlgorithm(HashAlgorithmEnum.MD5);
            var hashBytes = hashAlgorithm.ComputeHash(buffer, 0, bytesRead);
            return ConvertToHexString(hashBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute prefix hash for file: {FilePath}, prefixBytes: {PrefixBytes}", filePath, prefixBytes);
            throw;
        }
    }

    public async Task<bool> AreFilesIdenticalAsync(
        string path1,
        string path2,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path1))
        {
            throw new ArgumentException("First file path cannot be null or empty", nameof(path1));
        }

        if (string.IsNullOrWhiteSpace(path2))
        {
            throw new ArgumentException("Second file path cannot be null or empty", nameof(path2));
        }

        if (string.Equals(path1, path2, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!File.Exists(path1) || !File.Exists(path2))
        {
            return false;
        }

        try
        {
            var file1 = new FileInfo(path1);
            var file2 = new FileInfo(path2);
            if (file1.Length != file2.Length)
            {
                return false;
            }

            if (file1.LastWriteTimeUtc == file2.LastWriteTimeUtc)
            {
                return true;
            }

            var hash1 = await ComputeFileHashAsync(path1, cancellationToken).ConfigureAwait(false);
            var hash2 = await ComputeFileHashAsync(path2, cancellationToken).ConfigureAwait(false);
            return string.Equals(hash1, hash2, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compare files: {Path1} and {Path2}", path1, path2);
            return false;
        }
    }

    public async Task<bool> AreFilePrefixesIdenticalAsync(
        string path1,
        string path2,
        int prefixBytes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path1))
        {
            throw new ArgumentException("First file path cannot be null or empty", nameof(path1));
        }

        if (string.IsNullOrWhiteSpace(path2))
        {
            throw new ArgumentException("Second file path cannot be null or empty", nameof(path2));
        }

        if (prefixBytes <= 0)
        {
            throw new ArgumentException("Prefix bytes must be greater than 0", nameof(prefixBytes));
        }

        if (string.Equals(path1, path2, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!File.Exists(path1) || !File.Exists(path2))
        {
            return false;
        }

        try
        {
            var hash1 = await ComputeFilePrefixHashAsync(path1, prefixBytes, cancellationToken).ConfigureAwait(false);
            var hash2 = await ComputeFilePrefixHashAsync(path2, prefixBytes, cancellationToken).ConfigureAwait(false);
            return string.Equals(hash1, hash2, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compare file prefixes: {Path1} and {Path2}, prefixBytes: {PrefixBytes}", path1, path2, prefixBytes);
            return false;
        }
    }

    public Task<IReadOnlyList<FileHashResult>> ComputeFileHashesAsync(
        IReadOnlyList<string> filePaths,
        IProgress<HashProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ComputeFileHashesAsync(
            new HashBatchRequest
            {
                FilePaths = filePaths
            },
            progress,
            cancellationToken);
    }

    public Task<IReadOnlyList<FileHashResult>> ComputeFilePrefixHashesAsync(
        IReadOnlyList<string> filePaths,
        int prefixBytes,
        IProgress<HashProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ComputeFilePrefixHashesAsync(
            new HashBatchRequest
            {
                FilePaths = filePaths
            },
            prefixBytes,
            progress,
            cancellationToken);
    }

    public Task<IReadOnlyList<FileHashResult>> ComputeFileHashesAsync(
        HashBatchRequest request,
        IProgress<HashProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return ComputeBatchHashesAsync(request, prefixBytes: null, progress, cancellationToken);
    }

    public Task<IReadOnlyList<FileHashResult>> ComputeFilePrefixHashesAsync(
        HashBatchRequest request,
        int prefixBytes,
        IProgress<HashProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (prefixBytes <= 0)
        {
            throw new ArgumentException("Prefix bytes must be greater than 0", nameof(prefixBytes));
        }

        return ComputeBatchHashesAsync(request, prefixBytes, progress, cancellationToken);
    }

    private async Task<IReadOnlyList<FileHashResult>> ComputeBatchHashesAsync(
        HashBatchRequest request,
        int? prefixBytes,
        IProgress<HashProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var filePaths = request.FilePaths ?? Array.Empty<string>();
        if (filePaths.Count == 0)
        {
            return Array.Empty<FileHashResult>();
        }

        var workerCount = Math.Clamp(request.WorkerCount ?? Environment.ProcessorCount, 1, 64);
        var activeWorkers = Math.Min(workerCount, filePaths.Count);
        var totalBytes = ComputeTotalBytes(filePaths);
        var processedCount = 0;
        long processedBytes = 0;
        var queue = new ConcurrentQueue<(int Index, string Path)>(
            filePaths.Select((path, index) => (index, path)));
        var results = new FileHashResult[filePaths.Count];
        var kind = prefixBytes.HasValue ? "prefix" : "full";
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "hash.batch.start kind={Kind} fileCount={FileCount} workerCount={WorkerCount} prefixBytes={PrefixBytes}",
            kind,
            filePaths.Count,
            activeWorkers,
            prefixBytes ?? 0);

        var workers = Enumerable.Range(0, activeWorkers)
            .Select(_ => Task.Run(async () =>
            {
                while (queue.TryDequeue(out var item))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results[item.Index] = await ComputeBatchItemAsync(
                        item.Path,
                        prefixBytes,
                        filePaths.Count,
                        totalBytes,
                        progress,
                        cancellationToken,
                        () => Interlocked.Increment(ref processedCount),
                        delta => Interlocked.Add(ref processedBytes, delta),
                        () => Interlocked.Read(ref processedBytes)).ConfigureAwait(false);
                }
            }, cancellationToken))
            .ToArray();

        await Task.WhenAll(workers).ConfigureAwait(false);

        var successCount = results.Count(result => result.IsSuccess);
        var failureCount = results.Length - successCount;
        var elapsedMs = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        _logger.LogInformation(
            "hash.batch.done kind={Kind} fileCount={FileCount} workerCount={WorkerCount} successCount={SuccessCount} failureCount={FailureCount} elapsedMs={ElapsedMs}",
            kind,
            filePaths.Count,
            activeWorkers,
            successCount,
            failureCount,
            elapsedMs);

        return Array.AsReadOnly(results);
    }

    private async Task<FileHashResult> ComputeBatchItemAsync(
        string filePath,
        int? prefixBytes,
        int totalCount,
        long totalBytes,
        IProgress<HashProgress>? progress,
        CancellationToken cancellationToken,
        Func<int> incrementProcessedCount,
        Func<long, long> addProcessedBytes,
        Func<long> readProcessedBytes)
    {
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                var missingCurrent = incrementProcessedCount();
                progress?.Report(new HashProgress
                {
                    ProcessedCount = missingCurrent,
                    TotalCount = totalCount,
                    CurrentFile = filePath,
                    ProcessedBytes = readProcessedBytes(),
                    TotalBytes = totalBytes
                });
                return new FileHashResult
                {
                    FilePath = filePath,
                    Hash = string.Empty,
                    Exception = new FileNotFoundException($"File not found: {filePath}", filePath)
                };
            }

            var hash = prefixBytes.HasValue
                ? await ComputeFilePrefixHashAsync(filePath, prefixBytes.Value, cancellationToken).ConfigureAwait(false)
                : await ComputeFileHashAsync(filePath, cancellationToken).ConfigureAwait(false);
            var current = incrementProcessedCount();
            var bytes = addProcessedBytes(fileInfo.Length);
            progress?.Report(new HashProgress
            {
                ProcessedCount = current,
                TotalCount = totalCount,
                CurrentFile = filePath,
                ProcessedBytes = bytes,
                TotalBytes = totalBytes
            });

            return new FileHashResult
            {
                FilePath = filePath,
                Hash = hash,
                FileSize = fileInfo.Length,
                LastModified = fileInfo.LastWriteTimeUtc,
                ElapsedMilliseconds = (long)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var current = incrementProcessedCount();
            progress?.Report(new HashProgress
            {
                ProcessedCount = current,
                TotalCount = totalCount,
                CurrentFile = filePath,
                ProcessedBytes = readProcessedBytes(),
                TotalBytes = totalBytes
            });
            return new FileHashResult
            {
                FilePath = filePath,
                Hash = string.Empty,
                Exception = ex
            };
        }
    }

    private static long ComputeTotalBytes(IReadOnlyList<string> filePaths)
    {
        long total = 0;
        foreach (var filePath in filePaths)
        {
            if (!File.Exists(filePath))
            {
                continue;
            }

            total += new FileInfo(filePath).Length;
        }

        return total;
    }

    private async Task<string> ComputeStreamHashAsync(
        Stream stream,
        HashAlgorithmEnum algorithm,
        CancellationToken cancellationToken)
    {
        using var hashAlgorithm = CreateHashAlgorithm(algorithm);
        var buffer = new byte[8192];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            hashAlgorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
        }

        hashAlgorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return ConvertToHexString(hashAlgorithm.Hash ?? Array.Empty<byte>());
    }

    private static System.Security.Cryptography.HashAlgorithm CreateHashAlgorithm(HashAlgorithmEnum algorithm)
    {
        return algorithm switch
        {
            HashAlgorithmEnum.MD5 => MD5.Create(),
            HashAlgorithmEnum.SHA1 => SHA1.Create(),
            HashAlgorithmEnum.SHA256 => SHA256.Create(),
            HashAlgorithmEnum.SHA512 => SHA512.Create(),
            _ => throw new ArgumentException($"Unsupported hash algorithm: {algorithm}", nameof(algorithm))
        };
    }

    private static string ConvertToHexString(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
