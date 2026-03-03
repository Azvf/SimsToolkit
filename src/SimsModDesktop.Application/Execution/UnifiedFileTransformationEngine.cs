using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Services;

namespace SimsModDesktop.Application.Execution;

/// <summary>
/// Unified file transformation engine with Windows-first production-ready behavior.
/// </summary>
public sealed class UnifiedFileTransformationEngine : IFileTransformationEngine
{
    private readonly IReadOnlyDictionary<TransformationMode, ITransformationModeHandler> _handlers;

    public UnifiedFileTransformationEngine(
        ILogger<UnifiedFileTransformationEngine> logger,
        IFileOperationService fileOperationService,
        IHashComputationService hashComputationService,
        IConfigurationProvider configurationProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(fileOperationService);
        ArgumentNullException.ThrowIfNull(hashComputationService);
        ArgumentNullException.ThrowIfNull(configurationProvider);

        _handlers = new ITransformationModeHandler[]
        {
            new FlattenTransformationModeHandler(logger, fileOperationService, hashComputationService, configurationProvider),
            new NormalizeTransformationModeHandler(logger, fileOperationService, hashComputationService, configurationProvider),
            new MergeTransformationModeHandler(logger, fileOperationService, hashComputationService, configurationProvider),
            new OrganizeTransformationModeHandler(logger, fileOperationService, hashComputationService, configurationProvider)
        }.ToDictionary(handler => handler.Mode);
    }

    public IReadOnlyList<TransformationMode> SupportedModes => _handlers.Keys.OrderBy(mode => mode).ToArray();

    public TransformationEngineInfo EngineInfo => new()
    {
        Name = "UnifiedFileTransformationEngine",
        Version = "0.3.0",
        Description = "Windows-first unified engine for flatten/normalize/merge/organize with mode handlers.",
        SupportedModes = SupportedModes,
        IsCrossPlatform = true,
        PerformanceInfo = new EnginePerformanceInfo
        {
            SupportsParallelProcessing = true,
            RecommendedMaxWorkerCount = 8,
            MemoryOptimization = MemoryOptimizationLevel.Medium,
            IOOptimization = IOOptimizationLevel.Medium
        }
    };

    public async Task<TransformationResult> TransformAsync(
        TransformationOptions options,
        TransformationMode mode,
        IProgress<TransformationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var validation = ValidateOptions(options);
        if (!validation.IsValid)
        {
            return new TransformationResult
            {
                Success = false,
                ErrorMessage = string.Join("; ", validation.Errors),
                Warnings = validation.Warnings
            };
        }

        if (!_handlers.TryGetValue(mode, out var handler))
        {
            return new TransformationResult
            {
                Success = false,
                ErrorMessage = $"Unsupported mode: {mode}"
            };
        }

        return await handler.TransformAsync(options, progress, cancellationToken);
    }

    public ValidationResult ValidateOptions(TransformationOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(options.SourcePath))
        {
            errors.Add("SourcePath is required");
        }
        else if (!Directory.Exists(options.SourcePath))
        {
            errors.Add($"Source path does not exist: {options.SourcePath}");
        }

        if (options.WorkerCount is <= 0)
        {
            errors.Add("WorkerCount must be greater than 0 when specified");
        }

        if (options.PrefixHashBytes is <= 0)
        {
            errors.Add("PrefixHashBytes must be greater than 0 when specified");
        }

        if (options.FileExtensions is { Length: > 0 })
        {
            var invalid = options.FileExtensions.Where(e => string.IsNullOrWhiteSpace(e)).ToArray();
            if (invalid.Length > 0)
            {
                errors.Add("FileExtensions contains empty values");
            }
        }

        if (options.ExcludePatterns is { Length: > 0 })
        {
            foreach (var pattern in options.ExcludePatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                try
                {
                    _ = Regex.IsMatch(string.Empty, pattern);
                }
                catch (ArgumentException ex)
                {
                    errors.Add($"Invalid exclude regex '{pattern}': {ex.Message}");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(options.TargetPath))
        {
            var fullSource = Path.GetFullPath(options.SourcePath);
            var fullTarget = Path.GetFullPath(options.TargetPath);
            if (fullSource.Equals(fullTarget, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("TargetPath equals SourcePath; operation may overwrite files in place.");
            }
        }

        return errors.Count > 0 ? ValidationResult.Failure(errors, warnings) : ValidationResult.Success();
    }
}
