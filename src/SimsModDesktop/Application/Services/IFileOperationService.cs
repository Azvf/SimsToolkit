using System.Runtime.InteropServices;

namespace SimsModDesktop.Application.Services;

/// <summary>
/// 跨平台文件操作服务接口
/// </summary>
public interface IFileOperationService
{
    /// <summary>
    /// 将文件或目录移动到回收站
    /// </summary>
    /// <param name="path">文件或目录路径</param>
    /// <returns>操作是否成功</returns>
    Task<bool> MoveToRecycleBinAsync(string path);

    /// <summary>
    /// 删除文件
    /// </summary>
    /// <param name="path">文件路径</param>
    /// <param name="permanent">是否永久删除（跳过回收站）</param>
    /// <returns>操作是否成功</returns>
    Task<bool> DeleteFileAsync(string path, bool permanent = false);

    /// <summary>
    /// 删除目录
    /// </summary>
    /// <param name="path">目录路径</param>
    /// <param name="permanent">是否永久删除（跳过回收站）</param>
    /// <returns>操作是否成功</returns>
    Task<bool> DeleteDirectoryAsync(string path, bool permanent = false);

    /// <summary>
    /// 规范化路径格式，确保跨平台兼容性
    /// </summary>
    /// <param name="path">原始路径</param>
    /// <returns>规范化后的路径</returns>
    string NormalizePath(string path);

    /// <summary>
    /// 安全地组合多个路径段
    /// </summary>
    /// <param name="paths">路径段数组</param>
    /// <returns>组合后的完整路径</returns>
    string CombinePaths(params string[] paths);

    /// <summary>
    /// 获取当前平台的回收站信息
    /// </summary>
    /// <returns>回收站信息，如果平台不支持则返回null</returns>
    RecycleBinInfo? GetRecycleBinInfo();

    /// <summary>
    /// 检查当前平台是否支持回收站功能
    /// </summary>
    bool IsRecycleBinSupported { get; }

    /// <summary>
    /// 获取当前平台类型
    /// </summary>
    PlatformID CurrentPlatform { get; }

    /// <summary>
    /// 检查路径是否为绝对路径
    /// </summary>
    /// <param name="path">要检查的路径</param>
    /// <returns>是否为绝对路径</returns>
    bool IsPathRooted(string path);

    /// <summary>
    /// 获取相对路径
    /// </summary>
    /// <param name="relativeTo">基础路径</param>
    /// <param name="path">目标路径</param>
    /// <returns>相对路径</returns>
    string GetRelativePath(string relativeTo, string path);
}

/// <summary>
/// 回收站信息
/// </summary>
public sealed record RecycleBinInfo
{
    /// <summary>
    /// 回收站是否可用
    /// </summary>
    public bool IsAvailable { get; init; }

    /// <summary>
    /// 回收站路径
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// 回收站大小限制（字节）
    /// </summary>
    public long? SizeLimit { get; init; }

    /// <summary>
    /// 当前已使用空间（字节）
    /// </summary>
    public long? CurrentUsage { get; init; }

    /// <summary>
    /// 回收站描述信息
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// 文件操作结果
/// </summary>
public sealed record FileOperationResult
{
    /// <summary>
    /// 操作是否成功
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 操作消息
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// 异常信息（如果失败）
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// 操作影响的文件/目录路径
    /// </summary>
    public IReadOnlyList<string> AffectedPaths { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static FileOperationResult CreateSuccess(string? message = null, IEnumerable<string>? affectedPaths = null) => new()
    {
        Success = true,
        Message = message,
        AffectedPaths = affectedPaths?.ToArray() ?? Array.Empty<string>()
    };

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static FileOperationResult CreateFailure(string message, Exception? exception = null) => new()
    {
        Success = false,
        Message = message,
        Exception = exception,
        AffectedPaths = Array.Empty<string>()
    };
}