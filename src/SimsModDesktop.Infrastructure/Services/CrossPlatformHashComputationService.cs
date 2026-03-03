using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Services;
using HashAlgorithmEnum = SimsModDesktop.Application.Services.HashAlgorithm;

namespace SimsModDesktop.Infrastructure.Services;

/// <summary>
/// 跨平台文件哈希计算服务实现
/// </summary>
public sealed class CrossPlatformHashComputationService : IHashComputationService
{
    private readonly ILogger<CrossPlatformHashComputationService> _logger;

    public CrossPlatformHashComputationService(ILogger<CrossPlatformHashComputationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<HashAlgorithmEnum> SupportedAlgorithms => 
        new[] { HashAlgorithmEnum.MD5, HashAlgorithmEnum.SHA1, HashAlgorithmEnum.SHA256, HashAlgorithmEnum.SHA512 };

    /// <inheritdoc />
    public async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await ComputeFileHashAsync(filePath, HashAlgorithmEnum.MD5, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<string> ComputeFileHashAsync(string filePath, HashAlgorithmEnum algorithm, CancellationToken cancellationToken = default)
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
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            return await ComputeStreamHashAsync(stream, algorithm, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute hash for file: {FilePath}", filePath);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> ComputeFilePrefixHashAsync(string filePath, int prefixBytes, CancellationToken cancellationToken = default)
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
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            
            // 读取前缀字节
            var prefixBuffer = new byte[Math.Min(prefixBytes, stream.Length)];
            var bytesRead = await stream.ReadAsync(prefixBuffer, 0, prefixBuffer.Length, cancellationToken);
            
            if (bytesRead == 0)
            {
                throw new InvalidOperationException($"Failed to read prefix from file: {filePath}");
            }

            // 计算前缀哈希
            using var hashAlgorithm = CreateHashAlgorithm(HashAlgorithmEnum.MD5);
            var hashBytes = hashAlgorithm.ComputeHash(prefixBuffer, 0, bytesRead);
            return ConvertToHexString(hashBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute prefix hash for file: {FilePath}, prefixBytes: {PrefixBytes}", filePath, prefixBytes);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> AreFilesIdenticalAsync(string path1, string path2, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path1))
        {
            throw new ArgumentException("First file path cannot be null or empty", nameof(path1));
        }

        if (string.IsNullOrWhiteSpace(path2))
        {
            throw new ArgumentException("Second file path cannot be null or empty", nameof(path2));
        }

        // 如果路径相同，直接返回true
        if (string.Equals(path1, path2, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 检查文件是否存在
        if (!File.Exists(path1) || !File.Exists(path2))
        {
            return false;
        }

        try
        {
            var fileInfo1 = new FileInfo(path1);
            var fileInfo2 = new FileInfo(path2);

            // 首先比较文件大小，如果不相同则直接返回false
            if (fileInfo1.Length != fileInfo2.Length)
            {
                return false;
            }

            // 比较最后修改时间，如果相同则很可能相同（优化策略）
            if (fileInfo1.LastWriteTimeUtc == fileInfo2.LastWriteTimeUtc)
            {
                return true;
            }

            // 计算并比较哈希值
            var hash1 = await ComputeFileHashAsync(path1, cancellationToken);
            var hash2 = await ComputeFileHashAsync(path2, cancellationToken);

            return string.Equals(hash1, hash2, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compare files: {Path1} and {Path2}", path1, path2);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> AreFilePrefixesIdenticalAsync(string path1, string path2, int prefixBytes, CancellationToken cancellationToken = default)
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

        // 如果路径相同，直接返回true
        if (string.Equals(path1, path2, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 检查文件是否存在
        if (!File.Exists(path1) || !File.Exists(path2))
        {
            return false;
        }

        try
        {
            // 计算并比较前缀哈希值
            var hash1 = await ComputeFilePrefixHashAsync(path1, prefixBytes, cancellationToken);
            var hash2 = await ComputeFilePrefixHashAsync(path2, prefixBytes, cancellationToken);

            return string.Equals(hash1, hash2, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compare file prefixes: {Path1} and {Path2}, prefixBytes: {PrefixBytes}", path1, path2, prefixBytes);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileHashResult>> ComputeFileHashesAsync(
        IReadOnlyList<string> filePaths, 
        IProgress<HashProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (filePaths == null || filePaths.Count == 0)
        {
            return Array.Empty<FileHashResult>();
        }

        var results = new List<FileHashResult>(filePaths.Count);
        var totalBytes = 0L;

        // 首先计算总字节数
        foreach (var filePath in filePaths)
        {
            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                totalBytes += fileInfo.Length;
            }
        }

        var processedBytes = 0L;
        var processedCount = 0;

        // 并行处理文件哈希计算
        var tasks = filePaths.Select(async filePath =>
        {
            try
            {
                var startTime = DateTimeOffset.UtcNow;
                var fileInfo = new FileInfo(filePath);

                if (!fileInfo.Exists)
                {
                    return new FileHashResult
                    {
                        FilePath = filePath,
                        Hash = string.Empty,
                        Exception = new FileNotFoundException($"File not found: {filePath}", filePath)
                    };
                }

                var hash = await ComputeFileHashAsync(filePath, cancellationToken);
                var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;

                Interlocked.Add(ref processedBytes, fileInfo.Length);
                Interlocked.Increment(ref processedCount);

                progress?.Report(new HashProgress
                {
                    ProcessedCount = processedCount,
                    TotalCount = filePaths.Count,
                    CurrentFile = filePath,
                    ProcessedBytes = processedBytes,
                    TotalBytes = totalBytes
                });

                return new FileHashResult
                {
                    FilePath = filePath,
                    Hash = hash,
                    FileSize = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    ElapsedMilliseconds = (long)elapsed
                };
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref processedCount);
                
                progress?.Report(new HashProgress
                {
                    ProcessedCount = processedCount,
                    TotalCount = filePaths.Count,
                    CurrentFile = filePath
                });

                return new FileHashResult
                {
                    FilePath = filePath,
                    Hash = string.Empty,
                    Exception = ex
                };
            }
        });

        var taskResults = await Task.WhenAll(tasks);
        results.AddRange(taskResults);

        return results.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileHashResult>> ComputeFilePrefixHashesAsync(
        IReadOnlyList<string> filePaths,
        int prefixBytes,
        IProgress<HashProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (filePaths == null || filePaths.Count == 0)
        {
            return Array.Empty<FileHashResult>();
        }

        if (prefixBytes <= 0)
        {
            throw new ArgumentException("Prefix bytes must be greater than 0", nameof(prefixBytes));
        }

        var results = new List<FileHashResult>(filePaths.Count);
        var processedCount = 0;

        // 并行处理文件前缀哈希计算
        var tasks = filePaths.Select(async filePath =>
        {
            try
            {
                var startTime = DateTimeOffset.UtcNow;
                var fileInfo = new FileInfo(filePath);

                if (!fileInfo.Exists)
                {
                    return new FileHashResult
                    {
                        FilePath = filePath,
                        Hash = string.Empty,
                        Exception = new FileNotFoundException($"File not found: {filePath}", filePath)
                    };
                }

                var hash = await ComputeFilePrefixHashAsync(filePath, prefixBytes, cancellationToken);
                var elapsed = (DateTimeOffset.UtcNow - startTime).TotalMilliseconds;

                Interlocked.Increment(ref processedCount);

                progress?.Report(new HashProgress
                {
                    ProcessedCount = processedCount,
                    TotalCount = filePaths.Count,
                    CurrentFile = filePath
                });

                return new FileHashResult
                {
                    FilePath = filePath,
                    Hash = hash,
                    FileSize = fileInfo.Length,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    ElapsedMilliseconds = (long)elapsed
                };
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref processedCount);
                
                progress?.Report(new HashProgress
                {
                    ProcessedCount = processedCount,
                    TotalCount = filePaths.Count,
                    CurrentFile = filePath
                });

                return new FileHashResult
                {
                    FilePath = filePath,
                    Hash = string.Empty,
                    Exception = ex
                };
            }
        });

        var taskResults = await Task.WhenAll(tasks);
        results.AddRange(taskResults);

        return results.AsReadOnly();
    }

    #region 私有方法

    private async Task<string> ComputeStreamHashAsync(Stream stream, HashAlgorithmEnum algorithm, CancellationToken cancellationToken)
    {
        using var hashAlgorithm = CreateHashAlgorithm(algorithm);
        
        // 使用异步方式读取流并计算哈希
        var buffer = new byte[8192]; // 8KB缓冲区
        int bytesRead;
        
        while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            hashAlgorithm.TransformBlock(buffer, 0, bytesRead, null, 0);
        }
        
        hashAlgorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return ConvertToHexString(hashAlgorithm.Hash ?? Array.Empty<byte>());
    }

    private System.Security.Cryptography.HashAlgorithm CreateHashAlgorithm(HashAlgorithmEnum algorithm)
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

    private string ConvertToHexString(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    #endregion
}