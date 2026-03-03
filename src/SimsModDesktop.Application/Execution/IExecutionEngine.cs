
namespace SimsModDesktop.Application.Execution;

/// <summary>
/// 跨平台执行引擎抽象接口，支持多种后端实现
/// </summary>
public interface IExecutionEngine
{
    /// <summary>
    /// 执行给定的执行计划
    /// </summary>
    /// <param name="plan">执行计划</param>
    /// <param name="progress">进度报告回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    Task<SimsExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        IProgress<SimsProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 当前平台是否支持此执行引擎
    /// </summary>
    bool IsSupportedOnCurrentPlatform { get; }

    /// <summary>
    /// 执行引擎名称
    /// </summary>
    string EngineName { get; }

    /// <summary>
    /// 执行引擎版本
    /// </summary>
    string EngineVersion { get; }

    /// <summary>
    /// 验证执行计划的有效性
    /// </summary>
    /// <param name="plan">要验证的执行计划</param>
    /// <returns>验证结果</returns>
    ValidationResult ValidatePlan(ExecutionPlan plan);
}

/// <summary>
/// 验证结果
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public static ValidationResult Success() => new() { IsValid = true };
    
    public static ValidationResult Failure(IEnumerable<string> errors, IEnumerable<string>? warnings = null) => new() 
    { 
        IsValid = false, 
        Errors = errors.ToArray(),
        Warnings = warnings?.ToArray() ?? Array.Empty<string>()
    };
}

/// <summary>
/// 执行计划基类
/// </summary>
public abstract record ExecutionPlan
{
    /// <summary>
    /// 执行的操作类型
    /// </summary>
    public required SimsAction Action { get; init; }

    /// <summary>
    /// 是否模拟执行
    /// </summary>
    public bool WhatIf { get; init; }

    /// <summary>
    /// 超时时间（毫秒）
    /// </summary>
    public int? TimeoutMilliseconds { get; init; }

    /// <summary>
    /// 自定义参数
    /// </summary>
    public IReadOnlyDictionary<string, object>? CustomParameters { get; init; }
}

/// <summary>
/// 文件转换执行计划
/// </summary>
public sealed record FileTransformationPlan : ExecutionPlan
{
    /// <summary>
    /// 转换模式
    /// </summary>
    public required TransformationMode Mode { get; init; }

    /// <summary>
    /// 源路径
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// 目标路径
    /// </summary>
    public string? TargetPath { get; init; }

    /// <summary>
    /// 文件扩展名过滤器
    /// </summary>
    public string[]? FileExtensions { get; init; }

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
}

/// <summary>
/// 依赖分析执行计划
/// </summary>
public sealed record DependencyAnalysisPlan : ExecutionPlan
{
    /// <summary>
    /// Tray文件路径
    /// </summary>
    public required string TrayPath { get; init; }

    /// <summary>
    /// Mod文件路径
    /// </summary>
    public required string ModsPath { get; init; }

    /// <summary>
    /// 分析模式
    /// </summary>
    public AnalysisMode Mode { get; init; } = AnalysisMode.StrictS4TI;

    /// <summary>
    /// 输出格式
    /// </summary>
    public OutputFormat OutputFormat { get; init; } = OutputFormat.Csv;

    /// <summary>
    /// 输出文件路径
    /// </summary>
    public string? OutputPath { get; init; }
}

/// <summary>
/// 转换模式枚举
/// </summary>
public enum TransformationMode
{
    Flatten,      // 扁平化
    Normalize,    // 规范化
    Merge,        // 合并
    Organize      // 组织
}

/// <summary>
/// 分析模式
/// </summary>
public enum AnalysisMode
{
    StrictS4TI,   // 严格S4TI模式
    Legacy        // 传统模式
}

/// <summary>
/// 输出格式
/// </summary>
public enum OutputFormat
{
    Csv,          // CSV格式
    Json,         // JSON格式
    Xml           // XML格式
}
