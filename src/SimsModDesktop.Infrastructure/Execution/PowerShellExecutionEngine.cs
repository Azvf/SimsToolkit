using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Cli;

namespace SimsModDesktop.Infrastructure.Execution;

/// <summary>
/// PowerShell执行引擎适配器，包装现有的PowerShell执行功能
/// </summary>
public sealed class PowerShellExecutionEngine : IExecutionEngine
{
    private readonly ISimsPowerShellRunner _powerShellRunner;
    private readonly ILogger<PowerShellExecutionEngine> _logger;

    public PowerShellExecutionEngine(
        ISimsPowerShellRunner powerShellRunner,
        ILogger<PowerShellExecutionEngine> logger)
    {
        _powerShellRunner = powerShellRunner ?? throw new ArgumentNullException(nameof(powerShellRunner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsSupportedOnCurrentPlatform => true; // PowerShell在所有主流平台都可用

    /// <inheritdoc />
    public string EngineName => "PowerShell";

    /// <inheritdoc />
    public string EngineVersion => GetPowerShellVersion();

    /// <inheritdoc />
    public async Task<SimsExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        IProgress<SimsProgressUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _logger.LogInformation("Starting PowerShell execution for action: {Action}", plan.Action);

        try
        {
            // 验证执行计划
            var validationResult = ValidatePlan(plan);
            if (!validationResult.IsValid)
            {
                throw new InvalidOperationException($"Invalid execution plan: {string.Join(", ", validationResult.Errors)}");
            }

            // 将通用的ExecutionPlan转换为具体的SimsProcessCommand
            var command = ConvertToSimsProcessCommand(plan);

            // 执行并收集结果
            var outputs = new List<string>();
            var progressUpdates = new List<SimsProgressUpdate>();

            var result = await _powerShellRunner.RunAsync(
                command,
                output => outputs.Add(output),
                progressUpdate =>
                {
                    progressUpdates.Add(progressUpdate);
                    progress?.Report(progressUpdate);
                },
                cancellationToken);

            _logger.LogInformation("PowerShell execution completed with exit code: {ExitCode}", result.ExitCode);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PowerShell execution failed for action: {Action}", plan.Action);
            throw;
        }
    }

    /// <inheritdoc />
    public ValidationResult ValidatePlan(ExecutionPlan plan)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // 基础验证
        if (plan.Action == default)
        {
            errors.Add("Action cannot be None");
        }

        // 根据具体计划类型进行验证
        switch (plan)
        {
            case FileTransformationPlan transformationPlan:
                ValidateFileTransformationPlan(transformationPlan, errors, warnings);
                break;

            case DependencyAnalysisPlan analysisPlan:
                ValidateDependencyAnalysisPlan(analysisPlan, errors, warnings);
                break;

            default:
                errors.Add($"Unsupported execution plan type: {plan.GetType().Name}");
                break;
        }

        // 超时验证
        if (plan.TimeoutMilliseconds.HasValue && plan.TimeoutMilliseconds <= 0)
        {
            errors.Add("Timeout must be greater than 0");
        }

        return errors.Count > 0 
            ? ValidationResult.Failure(errors, warnings) 
            : ValidationResult.Success();
    }

    #region 私有方法

    private void ValidateFileTransformationPlan(FileTransformationPlan plan, List<string> errors, List<string> warnings)
    {
        // 源路径验证
        if (string.IsNullOrWhiteSpace(plan.SourcePath))
        {
            errors.Add("SourcePath is required");
        }
        else if (!Directory.Exists(plan.SourcePath))
        {
            errors.Add($"Source path does not exist: {plan.SourcePath}");
        }

        // 目标路径验证（如果需要）
        if (!string.IsNullOrWhiteSpace(plan.TargetPath))
        {
            try
            {
                // 检查目标路径的父目录是否存在
                var parentDir = Path.GetDirectoryName(plan.TargetPath);
                if (!string.IsNullOrWhiteSpace(parentDir) && !Directory.Exists(parentDir))
                {
                    warnings.Add($"Target parent directory does not exist and will be created: {parentDir}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Invalid target path: {ex.Message}");
            }
        }

        // 工作线程数验证
        if (plan.WorkerCount.HasValue)
        {
            if (plan.WorkerCount <= 0)
            {
                errors.Add("WorkerCount must be greater than 0");
            }
            else if (plan.WorkerCount > 64)
            {
                warnings.Add($"WorkerCount {plan.WorkerCount} is very high and may impact performance");
            }
        }

        // 前缀哈希字节数验证
        if (plan.PrefixHashBytes.HasValue)
        {
            if (plan.PrefixHashBytes <= 0)
            {
                errors.Add("PrefixHashBytes must be greater than 0");
            }
            else if (plan.PrefixHashBytes > 1024 * 1024) // 1MB
            {
                warnings.Add($"PrefixHashBytes {plan.PrefixHashBytes} is very large and may impact performance");
            }
        }
    }

    private void ValidateDependencyAnalysisPlan(DependencyAnalysisPlan plan, List<string> errors, List<string> warnings)
    {
        // Tray路径验证
        if (string.IsNullOrWhiteSpace(plan.TrayPath))
        {
            errors.Add("TrayPath is required");
        }
        else if (!Directory.Exists(plan.TrayPath))
        {
            errors.Add($"Tray path does not exist: {plan.TrayPath}");
        }

        // Mods路径验证
        if (string.IsNullOrWhiteSpace(plan.ModsPath))
        {
            errors.Add("ModsPath is required");
        }
        else if (!Directory.Exists(plan.ModsPath))
        {
            errors.Add($"Mods path does not exist: {plan.ModsPath}");
        }

        // 输出路径验证
        if (!string.IsNullOrWhiteSpace(plan.OutputPath))
        {
            try
            {
                var parentDir = Path.GetDirectoryName(plan.OutputPath);
                if (!string.IsNullOrWhiteSpace(parentDir) && !Directory.Exists(parentDir))
                {
                    warnings.Add($"Output parent directory does not exist and will be created: {parentDir}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Invalid output path: {ex.Message}");
            }
        }
    }

    private SimsProcessCommand ConvertToSimsProcessCommand(ExecutionPlan plan)
    {
        // 这里需要根据具体的执行计划类型转换为相应的SimsProcessCommand
        // 这是一个简化版本，实际需要根据具体功能实现完整的转换逻辑

        var arguments = new List<string> { "-Action", plan.Action.ToString().ToLowerInvariant() };

        if (plan.WhatIf)
        {
            arguments.Add("-WhatIf");
        }

        // 根据计划类型添加特定参数
        switch (plan)
        {
            case FileTransformationPlan transformationPlan:
                arguments.AddRange(ConvertTransformationArguments(transformationPlan));
                break;

            case DependencyAnalysisPlan analysisPlan:
                arguments.AddRange(ConvertAnalysisArguments(analysisPlan));
                break;
        }

        return new SimsProcessCommand { Arguments = arguments };
    }

    private List<string> ConvertTransformationArguments(FileTransformationPlan plan)
    {
        var args = new List<string>();

        // 基础参数
        args.AddRange(new[] { "-SourcePath", plan.SourcePath });

        if (!string.IsNullOrWhiteSpace(plan.TargetPath))
        {
            args.AddRange(new[] { "-TargetPath", plan.TargetPath });
        }

        // 转换模式
        args.AddRange(new[] { "-Mode", plan.Mode.ToString() });

        // 冲突解决策略
        args.AddRange(new[] { "-ConflictStrategy", plan.ConflictStrategy.ToString() });

        // 其他可选参数
        if (plan.FileExtensions?.Length > 0)
        {
            args.AddRange(new[] { "-FileExtensions", string.Join(",", plan.FileExtensions) });
        }

        if (plan.WorkerCount.HasValue)
        {
            args.AddRange(new[] { "-WorkerCount", plan.WorkerCount.Value.ToString() });
        }

        if (plan.PrefixHashBytes.HasValue)
        {
            args.AddRange(new[] { "-PrefixHashBytes", plan.PrefixHashBytes.Value.ToString() });
        }

        if (plan.VerifyContent)
        {
            args.Add("-VerifyContent");
        }

        if (!plan.Recursive)
        {
            args.Add("-NoRecursive");
        }

        return args;
    }

    private List<string> ConvertAnalysisArguments(DependencyAnalysisPlan plan)
    {
        var args = new List<string>();

        args.AddRange(new[] { "-TrayPath", plan.TrayPath });
        args.AddRange(new[] { "-ModsPath", plan.ModsPath });
        args.AddRange(new[] { "-AnalysisMode", plan.Mode.ToString() });

        if (!string.IsNullOrWhiteSpace(plan.OutputPath))
        {
            args.AddRange(new[] { "-OutputPath", plan.OutputPath });
        }

        args.AddRange(new[] { "-OutputFormat", plan.OutputFormat.ToString() });

        return args;
    }

    private string GetPowerShellVersion()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pwsh",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return output;
        }
        catch
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = "-Command $PSVersionTable.PSVersion",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                return output;
            }
            catch
            {
                return "Unknown";
            }
        }
    }

    #endregion
}
