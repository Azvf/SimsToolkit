namespace SimsModDesktop.Application.Services;

/// <summary>
/// 文件哈希计算服务接口，提供跨平台的文件哈希计算功能
/// </summary>
public interface IHashComputationService
{
    /// <summary>
    /// 计算文件的完整MD5哈希
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文件哈希值（十六进制字符串）</returns>
    Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 计算文件的前缀哈希（用于快速内容比对）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="prefixBytes">前缀字节数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>前缀哈希值（十六进制字符串）</returns>
    Task<string> ComputeFilePrefixHashAsync(string filePath, int prefixBytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// 比较两个文件是否完全相同
    /// </summary>
    /// <param name="path1">第一个文件路径</param>
    /// <param name="path2">第二个文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>两个文件是否相同</returns>
    Task<bool> AreFilesIdenticalAsync(string path1, string path2, CancellationToken cancellationToken = default);

    /// <summary>
    /// 比较两个文件的前缀是否相同（快速比对）
    /// </summary>
    /// <param name="path1">第一个文件路径</param>
    /// <param name="path2">第二个文件路径</param>
    /// <param name="prefixBytes">前缀字节数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>前缀是否相同</returns>
    Task<bool> AreFilePrefixesIdenticalAsync(string path1, string path2, int prefixBytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量计算文件哈希（并行处理）
    /// </summary>
    /// <param name="filePaths">文件路径列表</param>
    /// <param name="progress">进度报告</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文件哈希结果列表</returns>
    Task<IReadOnlyList<FileHashResult>> ComputeFileHashesAsync(
        IReadOnlyList<string> filePaths, 
        IProgress<HashProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量计算文件前缀哈希（并行处理）
    /// </summary>
    /// <param name="filePaths">文件路径列表</param>
    /// <param name="prefixBytes">前缀字节数</param>
    /// <param name="progress">进度报告</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文件前缀哈希结果列表</returns>
    Task<IReadOnlyList<FileHashResult>> ComputeFilePrefixHashesAsync(
        IReadOnlyList<string> filePaths,
        int prefixBytes,
        IProgress<HashProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取支持的哈希算法
    /// </summary>
    IReadOnlyList<HashAlgorithm> SupportedAlgorithms { get; }

    /// <summary>
    /// 使用指定算法计算文件哈希
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="algorithm">哈希算法</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>文件哈希值</returns>
    Task<string> ComputeFileHashAsync(string filePath, HashAlgorithm algorithm, CancellationToken cancellationToken = default);
}

/// <summary>
/// 文件哈希结果
/// </summary>
public sealed record FileHashResult
{
    /// <summary>
    /// 文件路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 文件哈希值
    /// </summary>
    public required string Hash { get; init; }

    /// <summary>
    /// 文件大小（字节）
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// 最后修改时间
    /// </summary>
    public DateTime LastModified { get; init; }

    /// <summary>
    /// 计算耗时（毫秒）
    /// </summary>
    public long ElapsedMilliseconds { get; init; }

    /// <summary>
    /// 异常信息（如果计算失败）
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// 是否成功计算
    /// </summary>
    public bool IsSuccess => Exception == null;
}

/// <summary>
/// 哈希进度报告
/// </summary>
public sealed record HashProgress
{
    /// <summary>
    /// 已处理文件数
    /// </summary>
    public required int ProcessedCount { get; init; }

    /// <summary>
    /// 总文件数
    /// </summary>
    public required int TotalCount { get; init; }

    /// <summary>
    /// 当前处理的文件路径
    /// </summary>
    public string? CurrentFile { get; init; }

    /// <summary>
    /// 进度百分比
    /// </summary>
    public int PercentComplete => TotalCount > 0 ? (ProcessedCount * 100) / TotalCount : 0;

    /// <summary>
    /// 已处理字节数
    /// </summary>
    public long ProcessedBytes { get; init; }

    /// <summary>
    /// 总字节数
    /// </summary>
    public long TotalBytes { get; init; }
}

/// <summary>
/// 哈希算法枚举
/// </summary>
public enum HashAlgorithm
{
    MD5,          // MD5算法（快速，适合文件比对）
    SHA1,         // SHA1算法
    SHA256,       // SHA256算法（安全，适合完整性验证）
    SHA512        // SHA512算法（高安全级别）
}