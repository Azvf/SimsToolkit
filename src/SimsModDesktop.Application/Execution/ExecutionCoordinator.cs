using SimsModDesktop.Application.Requests;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Models;
using SimsModDesktop.Application.Execution;

namespace SimsModDesktop.Application.Execution;

public sealed class ExecutionCoordinator : IExecutionCoordinator
{
    private const string UnifiedExecutable = "UnifiedFileTransformationEngine";

    private readonly ISimsPowerShellRunner _runner;
    private readonly IFileTransformationEngine _transformationEngine;
    private readonly IExecutionEngineRoutingPolicy _routingPolicy;
    private readonly ILogger<ExecutionCoordinator> _logger;
    private readonly IReadOnlyDictionary<SimsAction, IActionExecutionStrategy> _strategies;

    public ExecutionCoordinator(
        ISimsPowerShellRunner runner,
        IFileTransformationEngine transformationEngine,
        IExecutionEngineRoutingPolicy routingPolicy,
        ILogger<ExecutionCoordinator> logger,
        IEnumerable<IActionExecutionStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(transformationEngine);
        ArgumentNullException.ThrowIfNull(routingPolicy);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(strategies);

        var allStrategies = strategies.ToList();
        if (allStrategies.Count == 0)
        {
            throw new InvalidOperationException("No execution strategies were registered.");
        }

        var duplicateActions = allStrategies
            .GroupBy(strategy => strategy.Action)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.ToString())
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (duplicateActions.Length > 0)
        {
            throw new InvalidOperationException(
                "Duplicate execution strategies were registered: " + string.Join(", ", duplicateActions));
        }

        _runner = runner;
        _transformationEngine = transformationEngine;
        _routingPolicy = routingPolicy;
        _logger = logger;
        _strategies = allStrategies.ToDictionary(strategy => strategy.Action);
    }

    public async Task<SimsExecutionResult> ExecuteAsync(
        ISimsExecutionInput input,
        Action<string> onOutput,
        Action<SimsProgressUpdate>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(onOutput);

        var strategy = GetStrategy(input.Action);
        var routingDecision = await _routingPolicy.DecideAsync(input.Action, cancellationToken);
        if (routingDecision.UseUnifiedEngine)
        {
            var unifiedResult = await TryExecuteUnifiedAsync(
                input,
                routingDecision,
                onOutput,
                onProgress,
                cancellationToken);

            if (unifiedResult.Completed)
            {
                return unifiedResult.Result!;
            }
        }

        if (!strategy.TryValidate(input, out var validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        var command = strategy.BuildCommand(input);
        return await _runner.RunAsync(command, onOutput, onProgress, cancellationToken);
    }

    private IActionExecutionStrategy GetStrategy(SimsAction action)
    {
        if (_strategies.TryGetValue(action, out var strategy))
        {
            return strategy;
        }

        throw new InvalidOperationException($"Execution strategy is not registered: {action}.");
    }

    private async Task<UnifiedAttemptResult> TryExecuteUnifiedAsync(
        ISimsExecutionInput input,
        EngineRoutingDecision routingDecision,
        Action<string> onOutput,
        Action<SimsProgressUpdate>? onProgress,
        CancellationToken cancellationToken)
    {
        if (!TryBuildTransformationRequest(input, out var mode, out var options, out var mapError))
        {
            onOutput($"[unified] skipped: {mapError}");
            return routingDecision.EnableFallbackToPowerShell
                ? UnifiedAttemptResult.NotCompleted()
                : throw new InvalidOperationException($"Unified engine mapping failed: {mapError}");
        }

        var validation = _transformationEngine.ValidateOptions(options);
        if (!validation.IsValid)
        {
            var message = string.Join("; ", validation.Errors);
            onOutput($"[unified] validation failed: {message}");
            if (routingDecision.EnableFallbackToPowerShell && routingDecision.FallbackOnValidationFailure)
            {
                return UnifiedAttemptResult.NotCompleted();
            }

            throw new InvalidOperationException($"Unified engine validation failed: {message}");
        }

        try
        {
            onOutput($"[unified] executing {mode} via {UnifiedExecutable}");
            var progressBridge = onProgress is null
                ? null
                : new Progress<TransformationProgress>(value =>
                {
                    onProgress(new SimsProgressUpdate
                    {
                        Stage = value.CurrentStage ?? mode.ToString(),
                        Current = value.ProcessedCount,
                        Total = value.TotalCount,
                        Percent = value.PercentComplete,
                        Detail = value.StatusDetail ?? string.Empty
                    });
                });

            var result = await _transformationEngine.TransformAsync(options, mode, progressBridge, cancellationToken);
            var executionResult = BuildUnifiedExecutionResult(input.Action, options, result);
            if (result.Success)
            {
                onOutput($"[unified] success: {result.Message ?? "completed"}");
                return UnifiedAttemptResult.Success(executionResult);
            }

            onOutput($"[unified] failed: {result.ErrorMessage ?? result.Message ?? "unknown error"}");
            if (routingDecision.EnableFallbackToPowerShell)
            {
                return UnifiedAttemptResult.NotCompleted();
            }

            return UnifiedAttemptResult.Success(executionResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unified execution failed for action {Action}", input.Action);
            onOutput($"[unified] exception: {ex.Message}");
            if (routingDecision.EnableFallbackToPowerShell)
            {
                return UnifiedAttemptResult.NotCompleted();
            }

            throw new InvalidOperationException($"Unified engine crashed: {ex.Message}", ex);
        }
    }

    private static SimsExecutionResult BuildUnifiedExecutionResult(
        SimsAction action,
        TransformationOptions options,
        TransformationResult result)
    {
        var args = new List<string>
        {
            action.ToString(),
            options.SourcePath
        };
        if (!string.IsNullOrWhiteSpace(options.TargetPath))
        {
            args.Add(options.TargetPath);
        }

        args.Add($"processed={result.ProcessedFiles}");
        args.Add($"failed={result.FailedFiles}");

        return new SimsExecutionResult
        {
            ExitCode = result.Success ? 0 : 1,
            Executable = UnifiedExecutable,
            Arguments = args
        };
    }

    private bool TryBuildTransformationRequest(
        ISimsExecutionInput input,
        out TransformationMode mode,
        out TransformationOptions options,
        out string error)
    {
        mode = default;
        options = null!;
        error = string.Empty;

        switch (input)
        {
            case FlattenInput flatten:
                mode = TransformationMode.Flatten;
                options = BuildFlattenOptions(flatten);
                return true;

            case NormalizeInput normalize:
                mode = TransformationMode.Normalize;
                options = BuildNormalizeOptions(normalize);
                return true;

            case MergeInput merge:
                mode = TransformationMode.Merge;
                options = BuildMergeOptions(merge);
                return true;

            case OrganizeInput organize:
                mode = TransformationMode.Organize;
                options = BuildOrganizeOptions(organize);
                return true;

            default:
                error = $"Action {input.Action} does not map to unified transformation request.";
                return false;
        }
    }

    private TransformationOptions BuildFlattenOptions(FlattenInput input)
    {
        var sourcePath = input.FlattenRootPath ?? string.Empty;
        var conflict = input.Shared.VerifyContentOnNameConflict
            ? ConflictResolutionStrategy.HashCompare
            : ConflictResolutionStrategy.Prompt;

        return new TransformationOptions
        {
            SourcePath = sourcePath,
            TargetPath = input.FlattenToRoot ? sourcePath : null,
            FileExtensions = input.Shared.ModExtensions.ToArray(),
            ConflictStrategy = conflict,
            Recursive = true,
            VerifyContent = input.Shared.VerifyContentOnNameConflict,
            PrefixHashBytes = input.Shared.PrefixHashBytes,
            WorkerCount = input.Shared.HashWorkerCount,
            WhatIf = input.WhatIf,
            KeepSource = false,
            ModeOptions = new ModeSpecificOptions
            {
                Flatten = new FlattenOptions
                {
                    FlattenToRoot = input.FlattenToRoot,
                    SkipPruneEmptyDirs = input.Shared.SkipPruneEmptyDirs,
                    ModFilesOnly = input.Shared.ModFilesOnly
                }
            }
        };
    }

    private TransformationOptions BuildNormalizeOptions(NormalizeInput input)
    {
        return new TransformationOptions
        {
            SourcePath = input.NormalizeRootPath ?? string.Empty,
            Recursive = true,
            WhatIf = input.WhatIf,
            ModeOptions = new ModeSpecificOptions
            {
                Normalize = new NormalizeOptions
                {
                    AutoRenameConflicts = true
                }
            }
        };
    }

    private TransformationOptions BuildMergeOptions(MergeInput input)
    {
        var sourcePaths = input.MergeSourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        var sourcePath = sourcePaths.FirstOrDefault() ?? string.Empty;
        var conflict = input.Shared.VerifyContentOnNameConflict
            ? ConflictResolutionStrategy.HashCompare
            : ConflictResolutionStrategy.Prompt;

        return new TransformationOptions
        {
            SourcePath = sourcePath,
            TargetPath = input.MergeTargetPath ?? sourcePath,
            FileExtensions = input.Shared.ModExtensions.ToArray(),
            ConflictStrategy = conflict,
            Recursive = true,
            VerifyContent = input.Shared.VerifyContentOnNameConflict,
            PrefixHashBytes = input.Shared.PrefixHashBytes,
            WorkerCount = input.Shared.HashWorkerCount,
            WhatIf = input.WhatIf,
            KeepSource = false,
            ModeOptions = new ModeSpecificOptions
            {
                Merge = new MergeOptions
                {
                    SourcePaths = sourcePaths,
                    SkipPruneEmptyDirs = input.Shared.SkipPruneEmptyDirs,
                    ModFilesOnly = input.Shared.ModFilesOnly
                }
            }
        };
    }

    private TransformationOptions BuildOrganizeOptions(OrganizeInput input)
    {
        var sourcePath = input.SourceDir ?? string.Empty;
        var target = input.ModsRoot;
        var archiveExtensions = new[] { ".zip" };

        return new TransformationOptions
        {
            SourcePath = sourcePath,
            TargetPath = target,
            Recursive = true,
            WhatIf = input.WhatIf,
            KeepSource = input.KeepZip,
            ModeOptions = new ModeSpecificOptions
            {
                Organize = new OrganizeOptions
                {
                    ZipNamePattern = input.ZipNamePattern,
                    ArchiveExtensions = archiveExtensions,
                    KeepZip = input.KeepZip,
                    RecurseSource = true,
                    IncludeLooseSources = true,
                    UnifiedTargetFolder = string.IsNullOrWhiteSpace(input.UnifiedModsFolder)
                        ? null
                        : input.UnifiedModsFolder
                }
            }
        };
    }

    private readonly struct UnifiedAttemptResult
    {
        private UnifiedAttemptResult(bool completed, SimsExecutionResult? result)
        {
            Completed = completed;
            Result = result;
        }

        public bool Completed { get; }
        public SimsExecutionResult? Result { get; }

        public static UnifiedAttemptResult Success(SimsExecutionResult result) => new(true, result);
        public static UnifiedAttemptResult NotCompleted() => new(false, null);
    }
}
