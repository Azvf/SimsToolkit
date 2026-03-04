using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Services;
using SimsModDesktop.Application.Validation;

namespace SimsModDesktop.Application.Execution;

public sealed class ExecutionCoordinator : IExecutionCoordinator
{
    private const string UnifiedExecutable = "UnifiedFileTransformationEngine";
    private const string FindDupExecutable = "FindDuplicateFiles";

    private readonly IFileTransformationEngine _transformationEngine;
    private readonly IHashComputationService _hashComputationService;
    private readonly IFileOperationService _fileOperationService;
    private readonly IActionInputValidator<FindDupInput> _findDupValidator;
    private readonly ILogger<ExecutionCoordinator> _logger;

    public ExecutionCoordinator(
        IFileTransformationEngine transformationEngine,
        IHashComputationService hashComputationService,
        IFileOperationService fileOperationService,
        IActionInputValidator<FindDupInput> findDupValidator,
        ILogger<ExecutionCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(transformationEngine);
        ArgumentNullException.ThrowIfNull(hashComputationService);
        ArgumentNullException.ThrowIfNull(fileOperationService);
        ArgumentNullException.ThrowIfNull(findDupValidator);
        ArgumentNullException.ThrowIfNull(logger);
        _transformationEngine = transformationEngine;
        _hashComputationService = hashComputationService;
        _fileOperationService = fileOperationService;
        _findDupValidator = findDupValidator;
        _logger = logger;
    }

    public async Task<SimsExecutionResult> ExecuteAsync(
        ISimsExecutionInput input,
        Action<string> onOutput,
        Action<SimsProgressUpdate>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(onOutput);

        if (input is FindDupInput findDup)
        {
            return await ExecuteFindDuplicatesAsync(findDup, onOutput, onProgress, cancellationToken);
        }

        if (!TryBuildTransformationRequest(input, out var mode, out var options, out var mapError))
        {
            throw new InvalidOperationException(mapError);
        }

        var validation = _transformationEngine.ValidateOptions(options);
        if (!validation.IsValid)
        {
            var message = string.Join("; ", validation.Errors);
            throw new InvalidOperationException($"Unified engine validation failed: {message}");
        }

        try
        {
            onOutput($"[toolkit] executing {mode} via {UnifiedExecutable}");
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
                onOutput($"[toolkit] success: {result.Message ?? "completed"}");
                return executionResult;
            }

            onOutput($"[toolkit] failed: {result.ErrorMessage ?? result.Message ?? "unknown error"}");
            return executionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unified execution failed for action {Action}", input.Action);
            onOutput($"[toolkit] exception: {ex.Message}");
            throw new InvalidOperationException($"Unified engine crashed: {ex.Message}", ex);
        }
    }

    private async Task<SimsExecutionResult> ExecuteFindDuplicatesAsync(
        FindDupInput input,
        Action<string> onOutput,
        Action<SimsProgressUpdate>? onProgress,
        CancellationToken cancellationToken)
    {
        if (!_findDupValidator.TryValidate(input, out var validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        var rootPath = input.FindDupRootPath!;
        var searchOption = input.FindDupRecurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var allowedExtensions = BuildExtensionFilter(input.Shared);

        onOutput($"[finddup] scanning {rootPath}");

        var files = Directory.EnumerateFiles(rootPath, "*", searchOption)
            .Where(path => allowedExtensions.Count == 0 || allowedExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new FileInfo(path))
            .ToArray();

        onOutput($"[finddup] candidate files: {files.Length}");
        if (files.Length == 0)
        {
            return new SimsExecutionResult
            {
                ExitCode = 0,
                Executable = FindDupExecutable,
                Arguments = [rootPath, "files=0", "duplicates=0"]
            };
        }

        var candidates = files
            .GroupBy(info => info.Length)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToArray();

        if (candidates.Length == 0)
        {
            onOutput("[finddup] no duplicate size groups found");
            await TryWriteFindDupCsvAsync(input.FindDupOutputCsv, Array.Empty<FindDupCsvRow>(), onOutput, cancellationToken);

            return new SimsExecutionResult
            {
                ExitCode = 0,
                Executable = FindDupExecutable,
                Arguments = [rootPath, $"files={files.Length}", "duplicates=0"]
            };
        }

        var hashProgress = onProgress is null
            ? null
            : new Progress<HashProgress>(value =>
            {
                onProgress(new SimsProgressUpdate
                {
                    Stage = "FindDuplicates",
                    Current = value.ProcessedCount,
                    Total = value.TotalCount,
                    Percent = value.PercentComplete,
                    Detail = value.CurrentFile ?? string.Empty
                });
            });

        var hashResults = await _hashComputationService.ComputeFileHashesAsync(
            new HashBatchRequest
            {
                FilePaths = candidates.Select(info => info.FullName).ToArray(),
                WorkerCount = input.Shared.HashWorkerCount
            },
            hashProgress,
            cancellationToken);

        var hashFailures = hashResults.Where(result => !result.IsSuccess).ToArray();
        foreach (var failure in hashFailures)
        {
            onOutput($"[finddup] hash failed: {failure.FilePath} - {failure.Exception?.Message}");
        }

        var duplicateGroups = hashResults
            .Where(result => result.IsSuccess)
            .GroupBy(result => result.Hash, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var csvRows = new List<FindDupCsvRow>();
        var deletedFiles = 0;
        var deleteFailures = 0;
        var groupId = 1;

        foreach (var group in duplicateGroups)
        {
            var ordered = group
                .OrderBy(result => result.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var item in ordered)
            {
                csvRows.Add(new FindDupCsvRow(
                    item.FilePath,
                    item.Hash,
                    item.FileSize,
                    groupId,
                    ordered.Length));
            }

            if (input.FindDupCleanup)
            {
                foreach (var duplicate in ordered.Skip(1))
                {
                    if (input.WhatIf)
                    {
                        onOutput($"[finddup] whatif delete {duplicate.FilePath}");
                        continue;
                    }

                    var deleted = await _fileOperationService.DeleteFileAsync(duplicate.FilePath);
                    if (deleted)
                    {
                        deletedFiles++;
                        onOutput($"[finddup] deleted {duplicate.FilePath}");
                    }
                    else
                    {
                        deleteFailures++;
                        onOutput($"[finddup] failed to delete {duplicate.FilePath}");
                    }
                }
            }

            groupId++;
        }

        onOutput($"[finddup] duplicate groups: {duplicateGroups.Length}");
        onOutput($"[finddup] duplicate files: {csvRows.Count}");
        if (input.FindDupCleanup)
        {
            onOutput(input.WhatIf
                ? "[finddup] cleanup preview completed"
                : $"[finddup] deleted {deletedFiles} duplicate files");
        }

        await TryWriteFindDupCsvAsync(input.FindDupOutputCsv, csvRows, onOutput, cancellationToken);

        var exitCode = hashFailures.Length > 0 || deleteFailures > 0 ? 1 : 0;
        return new SimsExecutionResult
        {
            ExitCode = exitCode,
            Executable = FindDupExecutable,
            Arguments =
            [
                rootPath,
                $"files={files.Length}",
                $"groups={duplicateGroups.Length}",
                $"duplicates={csvRows.Count}",
                $"deleted={deletedFiles}"
            ]
        };
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

    private static HashSet<string> BuildExtensionFilter(SharedFileOpsInput input)
    {
        if (!input.ModFilesOnly || input.ModExtensions.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return input.ModExtensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task TryWriteFindDupCsvAsync(
        string? outputPath,
        IReadOnlyList<FindDupCsvRow> rows,
        Action<string> onOutput,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream);
        await writer.WriteLineAsync("FilePath,Md5Hash,FileSize,GroupId,FileCount");

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(",",
                EscapeCsv(row.FilePath),
                EscapeCsv(row.Hash),
                row.FileSize.ToString(),
                row.GroupId.ToString(),
                row.FileCount.ToString()));
        }

        await writer.FlushAsync();
        onOutput("Exported to: " + outputPath);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return value;
    }

    private sealed record FindDupCsvRow(string FilePath, string Hash, long FileSize, int GroupId, int FileCount);
}
