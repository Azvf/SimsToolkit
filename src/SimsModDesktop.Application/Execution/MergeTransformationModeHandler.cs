using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Services;

namespace SimsModDesktop.Application.Execution;

internal sealed class MergeTransformationModeHandler : TransformationModeHandlerBase
{
    public MergeTransformationModeHandler(
        ILogger logger,
        IFileOperationService fileOperationService,
        IHashComputationService hashComputationService,
        IConfigurationProvider configurationProvider)
        : base(logger, fileOperationService, hashComputationService, configurationProvider)
    {
    }

    public override TransformationMode Mode => TransformationMode.Merge;

    public override async Task<TransformationResult> TransformAsync(
        TransformationOptions options,
        IProgress<TransformationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var mergeOptions = options.ModeOptions?.Merge;
        if (mergeOptions?.SourcePaths is not { Length: > 0 })
        {
            return new TransformationResult
            {
                Success = false,
                ErrorMessage = "Merge mode requires ModeOptions.Merge.SourcePaths"
            };
        }

        var targetPath = Path.GetFullPath(options.TargetPath ?? options.SourcePath);
        Directory.CreateDirectory(targetPath);

        var failOnOverlap = await GetConfigBoolAsync("Merge.FailOnOverlappingPaths", false, cancellationToken);
        var warnings = new List<string>();
        var processed = new List<string>();
        var failed = new List<string>();
        var skipped = new List<string>();
        var conflicts = new List<FileConflict>();

        var sourcePaths = mergeOptions.SourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var source in sourcePaths)
        {
            if (!Directory.Exists(source))
            {
                failed.Add(source);
                continue;
            }

            if (IsPathOverlapping(source, targetPath))
            {
                var warning = $"Merge source overlaps target: {source} -> {targetPath}";
                warnings.Add(warning);
                if (failOnOverlap)
                {
                    return new TransformationResult
                    {
                        Success = false,
                        ErrorMessage = warning,
                        Warnings = warnings
                    };
                }
            }
        }

        var allFiles = new List<(string SourceRoot, FileInfo File)>();
        foreach (var source in sourcePaths.Where(Directory.Exists))
        {
            try
            {
                allFiles.AddRange(EnumerateMergeFiles(source, options, mergeOptions.ModFilesOnly));
            }
            catch (UnauthorizedAccessException ex)
            {
                Logger.LogWarning(ex, "Access denied while scanning merge source {Source}", source);
                failed.Add(source);
                warnings.Add($"Access denied while scanning source: {source}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to scan merge source {Source}", source);
                failed.Add(source);
                warnings.Add($"Failed to scan source: {source}");
            }
        }

        var current = 0;
        foreach (var item in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current++;

            try
            {
                var relativePath = Path.GetRelativePath(item.SourceRoot, item.File.FullName);
                var destination = Path.Combine(targetPath, relativePath);

                if (IsSamePath(item.File.FullName, destination))
                {
                    skipped.Add(item.File.FullName);
                    ReportSimpleProgress(progress, "Merge", current, allFiles.Count, item.File.FullName);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                if (File.Exists(destination))
                {
                    var resolution = await ResolveConflictAsync(item.File.FullName, destination, options, cancellationToken);
                    if (resolution is null)
                    {
                        skipped.Add(item.File.FullName);
                        conflicts.Add(new FileConflict
                        {
                            Type = ConflictType.NameConflict,
                            SourcePath = item.File.FullName,
                            TargetPath = destination,
                            Description = "Merge conflict skipped"
                        });
                        ReportSimpleProgress(progress, "Merge", current, allFiles.Count, item.File.FullName);
                        continue;
                    }

                    destination = resolution;
                }

                if (!options.WhatIf)
                {
                    if (options.KeepSource)
                    {
                        File.Copy(item.File.FullName, destination, overwrite: false);
                    }
                    else
                    {
                        File.Move(item.File.FullName, destination);
                    }
                }

                processed.Add(item.File.FullName);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Merge failed for {Path}", item.File.FullName);
                failed.Add(item.File.FullName);
            }

            ReportSimpleProgress(progress, "Merge", current, allFiles.Count, item.File.FullName);
        }

        return new TransformationResult
        {
            Success = failed.Count == 0,
            Message = failed.Count == 0 ? "Merge completed." : "Merge completed with errors.",
            ProcessedFiles = processed.Count,
            SkippedFiles = skipped.Count,
            FailedFiles = failed.Count,
            TotalFiles = allFiles.Count,
            ProcessedFileList = processed,
            SkippedFileList = skipped,
            FailedFileList = failed,
            Conflicts = conflicts,
            Warnings = warnings
        };
    }

    private static IEnumerable<(string SourceRoot, FileInfo File)> EnumerateMergeFiles(
        string sourceRoot,
        TransformationOptions options,
        bool modFilesOnly)
    {
        var mergeModeOptions = new ModeSpecificOptions
        {
            Merge = new MergeOptions
            {
                SourcePaths = [sourceRoot],
                ModFilesOnly = modFilesOnly,
                SkipPruneEmptyDirs = false
            }
        };

        var childOptions = options with
        {
            SourcePath = sourceRoot,
            ModeOptions = mergeModeOptions
        };

        foreach (var file in EnumerateCandidateFiles(sourceRoot, childOptions))
        {
            yield return (sourceRoot, file);
        }
    }
}
