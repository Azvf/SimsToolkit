using System.IO.Compression;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Services;
using SimsModDesktop.PackageCore.Performance;

namespace SimsModDesktop.Application.Execution;

internal sealed class OrganizeTransformationModeHandler : TransformationModeHandlerBase
{
    private static readonly string[] DefaultArchiveExtensions = [".zip"];

    public OrganizeTransformationModeHandler(
        ILogger logger,
        IFileOperationService fileOperationService,
        IHashComputationService hashComputationService,
        IConfigurationProvider configurationProvider)
        : base(logger, fileOperationService, hashComputationService, configurationProvider)
    {
    }

    public override TransformationMode Mode => TransformationMode.Organize;

    public override async Task<TransformationResult> TransformAsync(
        TransformationOptions options,
        IProgress<TransformationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var organizeOptions = options.ModeOptions?.Organize;
        var sourcePath = Path.GetFullPath(options.SourcePath);
        var targetRoot = Path.GetFullPath(options.TargetPath ?? options.SourcePath);
        var archiveExtensions = organizeOptions?.ArchiveExtensions ?? DefaultArchiveExtensions;
        var recurse = organizeOptions?.RecurseSource != false;
        var cleanupMacArtifacts = await GetConfigBoolAsync("Organize.CleanupMacOsArtifacts", true, cancellationToken);
        var flattenTopLevel = GetCustomBool(options.CustomParameters, "Organize.FlattenSingleTopLevelDirectory", true);
        var configuredWorkers = organizeOptions?.MaxParallelArchives ?? options.WorkerCount;
        var workerCount = PerformanceWorkerSizer.ResolveOrganizeWorkers(configuredWorkers);

        var archives = Directory.EnumerateFiles(
                sourcePath,
                "*",
                recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(path => archiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var processed = new ConcurrentBag<string>();
        var failed = new ConcurrentBag<string>();
        var skipped = new ConcurrentBag<string>();
        var conflicts = new ConcurrentBag<FileConflict>();
        var warnings = new ConcurrentBag<string>();

        Directory.CreateDirectory(targetRoot);

        var executionPlans = new List<OrganizeExecutionPlan>(archives.Count);
        foreach (var archive in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = BuildOrganizeDestination(targetRoot, archive, organizeOptions);
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            {
                var conflictPlan = ResolveDirectoryConflictPlan(archive, destination, options.ConflictStrategy);
                if (conflictPlan.Skip)
                {
                    skipped.Add(archive);
                    conflicts.Add(new FileConflict
                    {
                        Type = ConflictType.NameConflict,
                        SourcePath = archive,
                        TargetPath = destination,
                        Description = "Organize destination exists and was skipped"
                    });
                    continue;
                }

                executionPlans.Add(new OrganizeExecutionPlan(archive, conflictPlan.DestinationPath, conflictPlan.DeleteExisting));
            }
            else
            {
                executionPlans.Add(new OrganizeExecutionPlan(archive, destination, DeleteExisting: false));
            }
        }

        var completedCount = skipped.Count;
        var baselineWorkingSet = Process.GetCurrentProcess().WorkingSet64;
        var throttle = new PerformanceAdaptiveThrottle(
            targetWorkers: workerCount,
            minWorkers: 4,
            startedAtUtc: DateTime.UtcNow);
        var allowedWorkers = workerCount;
        var startedAt = DateTime.UtcNow;
        Logger.LogInformation(
            "organize.archive.batch start total={Total} processCount={ProcessCount} skipCount={SkipCount} workerCount={WorkerCount}",
            archives.Count,
            executionPlans.Count,
            skipped.Count,
            workerCount);

        using var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var monitorTask = Task.Run(async () =>
        {
            while (!monitorCts.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), monitorCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var decision = throttle.Update(
                    totalCompletedCount: completedCount,
                    nowUtc: DateTime.UtcNow,
                    workingSetBytes: Process.GetCurrentProcess().WorkingSet64,
                    baselineWorkingSetBytes: baselineWorkingSet);
                if (!decision.Changed)
                {
                    continue;
                }

                Interlocked.Exchange(ref allowedWorkers, decision.RecommendedWorkers);
                Logger.LogInformation(
                    "organize.dynamic.throttle workerCount={WorkerCount} reason={Reason}",
                    decision.RecommendedWorkers,
                    decision.Reason);
            }
        }, cancellationToken);

        var queue = new ConcurrentQueue<OrganizeExecutionPlan>(executionPlans);
        var workers = Enumerable.Range(0, workerCount)
            .Select(workerId => Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (workerId >= Volatile.Read(ref allowedWorkers))
                    {
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (!queue.TryDequeue(out var plan))
                    {
                        break;
                    }

                    try
                    {
                        if (!options.WhatIf)
                        {
                            if (plan.DeleteExisting && Directory.Exists(plan.DestinationPath))
                            {
                                Directory.Delete(plan.DestinationPath, recursive: true);
                            }

                            Directory.CreateDirectory(plan.DestinationPath);
                            ZipFile.ExtractToDirectory(plan.ArchivePath, plan.DestinationPath, overwriteFiles: true);

                            if (cleanupMacArtifacts)
                            {
                                CleanupMacOsArtifacts(plan.DestinationPath);
                            }

                            if (flattenTopLevel)
                            {
                                FlattenSingleTopLevelDirectory(plan.DestinationPath);
                            }

                            if (organizeOptions?.KeepZip != true)
                            {
                                var deleted = await FileOperationService.DeleteFileAsync(plan.ArchivePath, permanent: true).ConfigureAwait(false);
                                if (!deleted)
                                {
                                    warnings.Add($"Unable to delete archive after extraction: {plan.ArchivePath}");
                                }
                            }
                        }

                        processed.Add(plan.ArchivePath);
                    }
                    catch (InvalidDataException ex)
                    {
                        Logger.LogWarning(ex, "Invalid archive {Archive}", plan.ArchivePath);
                        failed.Add(plan.ArchivePath);
                        warnings.Add($"Invalid archive skipped: {plan.ArchivePath}");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Failed to organize archive {Archive}", plan.ArchivePath);
                        failed.Add(plan.ArchivePath);
                    }
                    finally
                    {
                        var current = Interlocked.Increment(ref completedCount);
                        ReportSimpleProgress(progress, "Organize", current, archives.Count, plan.ArchivePath);
                    }
                }
            }, cancellationToken))
            .ToArray();

        await Task.WhenAll(workers).ConfigureAwait(false);
        monitorCts.Cancel();
        try
        {
            await monitorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        Logger.LogInformation(
            "organize.archive.batch done total={Total} processed={Processed} failed={Failed} skipped={Skipped} elapsedMs={ElapsedMs}",
            archives.Count,
            processed.Count,
            failed.Count,
            skipped.Count,
            (DateTime.UtcNow - startedAt).TotalMilliseconds);

        return new TransformationResult
        {
            Success = failed.Count == 0,
            Message = failed.Count == 0 ? "Organize completed." : "Organize completed with errors.",
            ProcessedFiles = processed.Count,
            SkippedFiles = skipped.Count,
            FailedFiles = failed.Count,
            TotalFiles = archives.Count,
            ProcessedFileList = processed.ToArray(),
            SkippedFileList = skipped.ToArray(),
            FailedFileList = failed.ToArray(),
            Conflicts = conflicts.ToArray(),
            Warnings = warnings.ToArray()
        };
    }

    private static bool GetCustomBool(IReadOnlyDictionary<string, object>? customParameters, string key, bool defaultValue)
    {
        if (customParameters is null || !customParameters.TryGetValue(key, out var value) || value is null)
        {
            return defaultValue;
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : defaultValue;
    }

    private static string BuildOrganizeDestination(string targetRoot, string archivePath, OrganizeOptions? organizeOptions)
    {
        var archiveName = Path.GetFileNameWithoutExtension(archivePath);
        var name = archiveName;
        if (!string.IsNullOrWhiteSpace(organizeOptions?.ZipNamePattern))
        {
            name = organizeOptions.ZipNamePattern.Replace("{name}", archiveName, StringComparison.Ordinal);
        }

        name = string.IsNullOrWhiteSpace(name) ? archiveName : name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = archiveName;
        }

        if (!string.IsNullOrWhiteSpace(organizeOptions?.UnifiedTargetFolder))
        {
            return Path.Combine(targetRoot, organizeOptions.UnifiedTargetFolder, name);
        }

        return Path.Combine(targetRoot, name);
    }

    private static OrganizeDirectoryConflictPlan ResolveDirectoryConflictPlan(
        string sourceArchive,
        string destination,
        ConflictResolutionStrategy strategy)
    {
        switch (strategy)
        {
            case ConflictResolutionStrategy.Skip:
            case ConflictResolutionStrategy.Prompt:
                return new OrganizeDirectoryConflictPlan(Skip: true, destination, DeleteExisting: false);
            case ConflictResolutionStrategy.Overwrite:
            case ConflictResolutionStrategy.HashCompare:
                return new OrganizeDirectoryConflictPlan(Skip: false, destination, DeleteExisting: true);
            case ConflictResolutionStrategy.KeepNewer:
            {
                var sourceTime = File.GetLastWriteTimeUtc(sourceArchive);
                var targetTime = Directory.GetLastWriteTimeUtc(destination);
                if (sourceTime <= targetTime)
                {
                    return new OrganizeDirectoryConflictPlan(Skip: true, destination, DeleteExisting: false);
                }

                return new OrganizeDirectoryConflictPlan(Skip: false, destination, DeleteExisting: true);
            }
            case ConflictResolutionStrategy.KeepOlder:
            {
                var sourceTime = File.GetLastWriteTimeUtc(sourceArchive);
                var targetTime = Directory.GetLastWriteTimeUtc(destination);
                if (sourceTime >= targetTime)
                {
                    return new OrganizeDirectoryConflictPlan(Skip: true, destination, DeleteExisting: false);
                }

                return new OrganizeDirectoryConflictPlan(Skip: false, destination, DeleteExisting: true);
            }
            default:
                return new OrganizeDirectoryConflictPlan(Skip: true, destination, DeleteExisting: false);
        }
    }

    private static void CleanupMacOsArtifacts(string destination)
    {
        var macOsMetadataDir = Path.Combine(destination, "__MACOSX");
        if (Directory.Exists(macOsMetadataDir))
        {
            Directory.Delete(macOsMetadataDir, recursive: true);
        }
    }

    private static void FlattenSingleTopLevelDirectory(string destination)
    {
        var directories = Directory.GetDirectories(destination, "*", SearchOption.TopDirectoryOnly);
        var files = Directory.GetFiles(destination, "*", SearchOption.TopDirectoryOnly);
        if (directories.Length != 1 || files.Length > 0)
        {
            return;
        }

        var top = directories[0];
        foreach (var childDir in Directory.GetDirectories(top, "*", SearchOption.TopDirectoryOnly))
        {
            var moveTo = Path.Combine(destination, Path.GetFileName(childDir));
            if (!Directory.Exists(moveTo))
            {
                Directory.Move(childDir, moveTo);
            }
        }

        foreach (var childFile in Directory.GetFiles(top, "*", SearchOption.TopDirectoryOnly))
        {
            var moveTo = Path.Combine(destination, Path.GetFileName(childFile));
            if (!File.Exists(moveTo))
            {
                File.Move(childFile, moveTo);
            }
        }

        if (!Directory.EnumerateFileSystemEntries(top).Any())
        {
            Directory.Delete(top);
        }
    }

    private readonly record struct OrganizeExecutionPlan(
        string ArchivePath,
        string DestinationPath,
        bool DeleteExisting);

    private readonly record struct OrganizeDirectoryConflictPlan(
        bool Skip,
        string DestinationPath,
        bool DeleteExisting);
}
