using SimsModDesktop.Application.Services;

namespace SimsModDesktop.Application.Execution;

/// <summary>
/// 统一的文件转换引擎，支持扁平化、规范化、合并、组织等多种模式
/// </summary>
public interface IFileTransformationEngine
{
    /// <summary>
    /// 执行文件转换操作
    /// </summary>
    /// <param name="options">转换选项</param>
    /// <param name="mode">转换模式</param>
    /// <param name="progress">进度报告</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>转换结果</returns>
    Task<TransformationResult> TransformAsync(
        TransformationOptions options,
        TransformationMode mode,
        IProgress<TransformationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 验证转换选项的有效性
    /// </summary>
    /// <param name="options">转换选项</param>
    /// <returns>验证结果</returns>
    ValidationResult ValidateOptions(TransformationOptions options);

    /// <summary>
    /// 获取支持的转换模式
    /// </summary>
    IReadOnlyList<TransformationMode> SupportedModes { get; }

    /// <summary>
    /// 获取转换引擎信息
    /// </summary>
    TransformationEngineInfo EngineInfo { get; }
}

/// <summary>
/// 文件转换选项
/// </summary>
public sealed record TransformationOptions
{
    /// <summary>
    /// 源路径
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// 目标路径（某些模式需要）
    /// </summary>
    public string? TargetPath { get; init; }

    /// <summary>
    /// 文件扩展名过滤器（可选）
    /// </summary>
    public string[]? FileExtensions { get; init; }

    /// <summary>
    /// 排除模式（正则表达式）
    /// </summary>
    public string[]? ExcludePatterns { get; init; }

    /// <summary>
    /// 冲突解决策略
    /// </summary>
    public ConflictResolutionStrategy ConflictStrategy { get; init; } = ConflictResolutionStrategy.Prompt;

    /// <summary>
    /// 是否递归处理子目录
    /// </summary>
    public bool Recursive { get; init; } = true;

    /// <summary>
    /// 是否验证内容（哈希比对）
    /// </summary>
    public bool VerifyContent { get; init; } = false;

    /// <summary>
    /// 前缀哈希字节数（用于快速内容比对）
    /// </summary>
    public int? PrefixHashBytes { get; init; }

    /// <summary>
    /// 并行工作线程数
    /// </summary>
    public int? WorkerCount { get; init; }

    /// <summary>
    /// 是否模拟执行（不实际修改文件）
    /// </summary>
    public bool WhatIf { get; init; } = false;

    /// <summary>
    /// 是否保留源文件（复制而非移动）
    /// </summary>
    public bool KeepSource { get; init; } = false;

    /// <summary>
    /// 自定义参数
    /// </summary>
    public IReadOnlyDictionary<string, object>? CustomParameters { get; init; }

    /// <summary>
    /// 特定模式的选项
    /// </summary>
    public ModeSpecificOptions? ModeOptions { get; init; }
}

/// <summary>
/// 特定模式的选项
/// </summary>
public sealed record ModeSpecificOptions
{
    /// <summary>
    /// 组织模式选项
    /// </summary>
    public OrganizeOptions? Organize { get; init; }

    /// <summary>
    /// 扁平化模式选项
    /// </summary>
    public FlattenOptions? Flatten { get; init; }

    /// <summary>
    /// 合并模式选项
    /// </summary>
    public MergeOptions? Merge { get; init; }

    /// <summary>
    /// 规范化模式选项
    /// </summary>
    public NormalizeOptions? Normalize { get; init; }
}

/// <summary>
/// 组织模式选项
/// </summary>
public sealed record OrganizeOptions
{
    /// <summary>
    /// 压缩包名称模式
    /// </summary>
    public string? ZipNamePattern { get; init; }

    /// <summary>
    /// 压缩包扩展名
    /// </summary>
    public string[]? ArchiveExtensions { get; init; }

    /// <summary>
    /// 是否保留压缩包
    /// </summary>
    public bool KeepZip { get; init; } = false;

    /// <summary>
    /// 是否递归扫描源目录
    /// </summary>
    public bool RecurseSource { get; init; } = true;

    /// <summary>
    /// 是否包含散文件/散目录
    /// </summary>
    public bool IncludeLooseSources { get; init; } = true;

    /// <summary>
    /// 统一的目标文件夹名称（可选）
    /// </summary>
    public string? UnifiedTargetFolder { get; init; }
}

/// <summary>
/// 扁平化模式选项
/// </summary>
public sealed record FlattenOptions
{
    /// <summary>
    /// 是否扁平化到根目录
    /// </summary>
    public bool FlattenToRoot { get; init; } = false;

    /// <summary>
    /// 是否跳过清理空目录
    /// </summary>
    public bool SkipPruneEmptyDirs { get; init; } = false;

    /// <summary>
    /// 是否仅处理Mod文件
    /// </summary>
    public bool ModFilesOnly { get; init; } = false;
}

/// <summary>
/// 合并模式选项
/// </summary>
public sealed record MergeOptions
{
    /// <summary>
    /// 多个源路径
    /// </summary>
    public required string[] SourcePaths { get; init; }

    /// <summary>
    /// 是否跳过清理空目录
    /// </summary>
    public bool SkipPruneEmptyDirs { get; init; } = false;

    /// <summary>
    /// 是否仅处理Mod文件
    /// </summary>
    public bool ModFilesOnly { get; init; } = false;
}

/// <summary>
/// 规范化模式选项
/// </summary>
public sealed record NormalizeOptions
{
    /// <summary>
    /// 命名规范（正则表达式）
    /// </summary>
    public string? NamingConvention { get; init; }

    /// <summary>
    /// 是否自动重命名冲突
    /// </summary>
    public bool AutoRenameConflicts { get; init; } = true;
}

/// <summary>
/// 转换结果
/// </summary>
public sealed record TransformationResult
{
    /// <summary>
    /// 转换是否成功
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 处理的文件数
    /// </summary>
    public int ProcessedFiles { get; init; } = 0;

    /// <summary>
    /// 跳过的文件数
    /// </summary>
    public int SkippedFiles { get; init; } = 0;

    /// <summary>
    /// 失败的文件数
    /// </summary>
    public int FailedFiles { get; init; } = 0;

    /// <summary>
    /// 总文件数
    /// </summary>
    public int TotalFiles { get; init; } = 0;

    /// <summary>
    /// 处理的字节数
    /// </summary>
    public long ProcessedBytes { get; init; } = 0;

    /// <summary>
    /// 总字节数
    /// </summary>
    public long TotalBytes { get; init; } = 0;

    /// <summary>
    /// 耗时（毫秒）
    /// </summary>
    public long ElapsedMilliseconds { get; init; } = 0;

    /// <summary>
    /// 结果消息
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// 错误信息（如果失败）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 异常信息（如果失败）
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// 处理的文件列表
    /// </summary>
    public IReadOnlyList<string> ProcessedFileList { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 跳过的文件列表
    /// </summary>
    public IReadOnlyList<string> SkippedFileList { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 失败的文件列表
    /// </summary>
    public IReadOnlyList<string> FailedFileList { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 冲突文件列表
    /// </summary>
    public IReadOnlyList<FileConflict> Conflicts { get; init; } = Array.Empty<FileConflict>();

    /// <summary>
    /// 警告信息列表
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// 转换进度报告
/// </summary>
public sealed record TransformationProgress
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
    public long ProcessedBytes { get; init; } = 0;

    /// <summary>
    /// 总字节数
    /// </summary>
    public long TotalBytes { get; init; } = 0;

    /// <summary>
    /// 当前阶段
    /// </summary>
    public string? CurrentStage { get; init; }

    /// <summary>
    /// 详细状态信息
    /// </summary>
    public string? StatusDetail { get; init; }

    /// <summary>
    /// 是否正在处理
    /// </summary>
    public bool IsProcessing { get; init; } = true;

    /// <summary>
    /// 估计剩余时间（毫秒）
    /// </summary>
    public long? EstimatedRemainingMilliseconds { get; init; }
}

/// <summary>
/// 文件冲突信息
/// </summary>
public sealed record FileConflict
{
    /// <summary>
    /// 冲突类型
    /// </summary>
    public required ConflictType Type { get; init; }

    /// <summary>
    /// 源文件路径
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// 目标文件路径
    /// </summary>
    public required string TargetPath { get; init; }

    /// <summary>
    /// 冲突描述
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// 建议的解决方案
    /// </summary>
    public ConflictResolution? SuggestedResolution { get; init; }

    /// <summary>
    /// 源文件信息
    /// </summary>
    public FileInfo? SourceFileInfo { get; init; }

    /// <summary>
    /// 目标文件信息
    /// </summary>
    public FileInfo? TargetFileInfo { get; init; }
}

/// <summary>
/// 冲突类型
/// </summary>
public enum ConflictType
{
    NameConflict,         // 名称冲突
    ContentConflict,      // 内容冲突
    PermissionConflict,   // 权限冲突
    SpaceConflict,        // 空间不足
    LockConflict          // 文件锁定
}

/// <summary>
/// 冲突解决方案
/// </summary>
public sealed record ConflictResolution
{
    /// <summary>
    /// 解决策略
    /// </summary>
    public required ConflictResolutionStrategy Strategy { get; init; }

    /// <summary>
    /// 解决方案描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 建议的操作
    /// </summary>
    public string? SuggestedAction { get; init; }

    /// <summary>
    /// 是否需要用户确认
    /// </summary>
    public bool RequiresUserConfirmation { get; init; } = false;
}

/// <summary>
/// 转换引擎信息
/// </summary>
public sealed record TransformationEngineInfo
{
    /// <summary>
    /// 引擎名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 引擎版本
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// 引擎描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 支持的模式
    /// </summary>
    public required IReadOnlyList<TransformationMode> SupportedModes { get; init; }

    /// <summary>
    /// 是否跨平台
    /// </summary>
    public bool IsCrossPlatform { get; init; } = true;

    /// <summary>
    /// 性能特性
    /// </summary>
    public EnginePerformanceInfo? PerformanceInfo { get; init; }
}

/// <summary>
/// 引擎性能信息
/// </summary>
public sealed record EnginePerformanceInfo
{
    /// <summary>
    /// 支持并行处理
    /// </summary>
    public bool SupportsParallelProcessing { get; init; } = true;

    /// <summary>
    /// 推荐的最大工作线程数
    /// </summary>
    public int? RecommendedMaxWorkerCount { get; init; }

    /// <summary>
    /// 内存使用优化级别
    /// </summary>
    public MemoryOptimizationLevel MemoryOptimization { get; init; } = MemoryOptimizationLevel.High;

    /// <summary>
    /// I/O优化级别
    /// </summary>
    public IOOptimizationLevel IOOptimization { get; init; } = IOOptimizationLevel.High;
}

/// <summary>
/// 内存优化级别
/// </summary>
public enum MemoryOptimizationLevel
{
    Low,      // 低优化，高内存使用
    Medium,   // 中等优化
    High      // 高优化，低内存使用
}

/// <summary>
/// I/O优化级别
/// </summary>
public enum IOOptimizationLevel
{
    Low,      // 低优化，高I/O操作
    Medium,   // 中等优化
    High      // 高优化，低I/O操作
}