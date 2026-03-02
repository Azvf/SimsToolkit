using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Services;

namespace SimsModDesktop.Application.Execution;

internal sealed class FlattenTransformationModeHandler : TransformationModeHandlerBase
{
    public FlattenTransformationModeHandler(
        ILogger logger,
        IFileOperationService fileOperationService,
        IHashComputationService hashComputationService,
        IConfigurationProvider configurationProvider)
        : base(logger, fileOperationService, hashComputationService, configurationProvider)
    {
    }

    public override TransformationMode Mode => TransformationMode.Flatten;

    public override async Task<TransformationResult> TransformAsync(
        TransformationOptions options,
        IProgress<TransformationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var sourcePath = Path.GetFullPath(options.SourcePath);
        var targetPath = Path.GetFullPath(options.TargetPath ?? options.SourcePath);

        Directory.CreateDirectory(targetPath);

        var files = EnumerateCandidateFiles(sourcePath, options).ToList();
        var totalFiles = files.Count;
        var totalBytes = files.Sum(f => f.Length);

        if (totalFiles == 0)
        {
            return new TransformationResult
            {
                Success = true,
                Message = "No files matched the current filters.",
                TotalFiles = 0,
                TotalBytes = 0
            };
        }

        var processed = new ConcurrentBag<string>();
        var skipped = new ConcurrentBag<string>();
        var failed = new ConcurrentBag<string>();
        var conflicts = new ConcurrentBag<FileConflict>();

        long processedBytes = 0;
        int processedCount = 0;
        int workerCount = Math.Clamp(options.WorkerCount ?? Environment.ProcessorCount, 1, 32);

        using var semaphore = new SemaphoreSlim(workerCount);
        var tasks = files.Select(async file =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await ProcessFileAsync(file, targetPath, options, processed, skipped, failed, conflicts, cancellationToken);
            }
            finally
            {
                Interlocked.Add(ref processedBytes, file.Length);
                var current = Interlocked.Increment(ref processedCount);
                progress?.Report(new TransformationProgress
                {
                    ProcessedCount = current,
                    TotalCount = totalFiles,
                    CurrentFile = file.FullName,
                    ProcessedBytes = processedBytes,
                    TotalBytes = totalBytes,
                    CurrentStage = "Flatten",
                    StatusDetail = $"Processed {current}/{totalFiles}"
                });
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        return new TransformationResult
        {
            Success = failed.IsEmpty,
            Message = failed.IsEmpty ? "Flatten completed." : "Flatten completed with errors.",
            ProcessedFiles = processed.Count,
            SkippedFiles = skipped.Count,
            FailedFiles = failed.Count,
            TotalFiles = totalFiles,
            ProcessedBytes = processedBytes,
            TotalBytes = totalBytes,
            ElapsedMilliseconds = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            ProcessedFileList = processed.ToArray(),
            SkippedFileList = skipped.ToArray(),
            FailedFileList = failed.ToArray(),
            Conflicts = conflicts.ToArray()
        };
    }

    private async Task ProcessFileAsync(
        FileInfo source,
        string targetRoot,
        TransformationOptions options,
        ConcurrentBag<string> processed,
        ConcurrentBag<string> skipped,
        ConcurrentBag<string> failed,
        ConcurrentBag<FileConflict> conflicts,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var destination = Path.Combine(targetRoot, source.Name);
        if (IsSamePath(source.FullName, destination))
        {
            skipped.Add(source.FullName);
            return;
        }

        try
        {
            if (File.Exists(destination))
            {
                var resolution = await ResolveConflictAsync(source.FullName, destination, options, cancellationToken);
                if (resolution is null)
                {
                    skipped.Add(source.FullName);
                    conflicts.Add(new FileConflict
                    {
                        Type = ConflictType.NameConflict,
                        SourcePath = source.FullName,
                        TargetPath = destination,
                        Description = "Conflict skipped by strategy",
                        SuggestedResolution = new ConflictResolution
                        {
                            Strategy = ConflictResolutionStrategy.Skip,
                            Description = "Skipped due to conflict strategy"
                        }
                    });
                    return;
                }

                destination = resolution;
            }

            if (options.WhatIf)
            {
                processed.Add(source.FullName);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            if (options.KeepSource)
            {
                File.Copy(source.FullName, destination, overwrite: false);
            }
            else
            {
                File.Move(source.FullName, destination);
            }

            processed.Add(source.FullName);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to flatten file {Path}", source.FullName);
            failed.Add(source.FullName);
        }
    }
}
