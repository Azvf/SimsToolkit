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
        var useRound2Parallel = await GetConfigBoolAsync("Performance.Round2.OrganizeParallelEnabled", true, cancellationToken);
        var configuredWorkers = organizeOptions?.MaxParallelArchives ?? options.WorkerCount;
        var workerCount = useRound2Parallel
            ? PerformanceWorkerSizer.ResolveOrganizeWorkers(configuredWorkers)
            : 1;

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
        var startedAt = DateTime.UtcNow;
        Logger.LogInformation(
            "organize.archive.batch start total={Total} processCount={ProcessCount} skipCount={SkipCount} workerCount={WorkerCount} useRound2Parallel={UseRound2Parallel}",
            archives.Count,
            executionPlans.Count,
            skipped.Count,
            workerCount,
            useRound2Parallel);

        if (!useRound2Parallel)
        {
            foreach (var plan in executionPlans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteOrganizePlanAsync(
                    plan,
                    options,
                    organizeOptions,
                    cleanupMacArtifacts,
                    flattenTopLevel,
                    skipped,
                    processed,
                    failed,
                    conflicts,
                    warnings).ConfigureAwait(false);
                var current = Interlocked.Increment(ref completedCount);
                ReportSimpleProgress(progress, "Organize", current, archives.Count, plan.ArchivePath);
            }

            Logger.LogInformation(
                "organize.archive.batch done total={Total} processed={Processed} failed={Failed} skipped={Skipped} elapsedMs={ElapsedMs}",
                archives.Count,
                processed.Count,
                failed.Count,
                skipped.Count,
                (DateTime.UtcNow - startedAt).TotalMilliseconds);
            return BuildResult(archives.Count, processed, skipped, failed, conflicts, warnings);
        }

        var baselineWorkingSet = Process.GetCurrentProcess().WorkingSet64;
        var throttle = new PerformanceAdaptiveThrottle(
            targetWorkers: workerCount,
            minWorkers: 4,
            startedAtUtc: DateTime.UtcNow);
        var allowedWorkers = workerCount;
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
                    totalCompletedCount: Volatile.Read(ref completedCount),
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
        }, CancellationToken.None);

        var destinationLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);
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

                    var destinationLock = destinationLocks.GetOrAdd(
                        plan.DestinationPath,
                        _ => new SemaphoreSlim(1, 1));
                    await destinationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await ExecuteOrganizePlanAsync(
                            plan,
                            options,
                            organizeOptions,
                            cleanupMacArtifacts,
                            flattenTopLevel,
                            skipped,
                            processed,
                            failed,
                            conflicts,
                            warnings).ConfigureAwait(false);
                    }
                    finally
                    {
                        destinationLock.Release();
                        var current = Interlocked.Increment(ref completedCount);
                        ReportSimpleProgress(progress, "Organize", current, archives.Count, plan.ArchivePath);
                    }
                }
            }, CancellationToken.None))
            .ToArray();
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        finally
        {
            monitorCts.Cancel();
            try
            {
                await monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        Logger.LogInformation(
            "organize.archive.batch done total={Total} processed={Processed} failed={Failed} skipped={Skipped} elapsedMs={ElapsedMs}",
            archives.Count,
            processed.Count,
            failed.Count,
            skipped.Count,
            (DateTime.UtcNow - startedAt).TotalMilliseconds);
        return BuildResult(archives.Count, processed, skipped, failed, conflicts, warnings);
    }

    private async Task ExecuteOrganizePlanAsync(
        OrganizeExecutionPlan plan,
        TransformationOptions options,
        OrganizeOptions? organizeOptions,
        bool cleanupMacArtifacts,
        bool flattenTopLevel,
        ConcurrentBag<string> skipped,
        ConcurrentBag<string> processed,
        ConcurrentBag<string> failed,
        ConcurrentBag<FileConflict> conflicts,
        ConcurrentBag<string> warnings)
    {
        var effectivePlan = plan;
        if (Directory.Exists(plan.DestinationPath) && Directory.EnumerateFileSystemEntries(plan.DestinationPath).Any())
        {
            var conflictPlan = ResolveDirectoryConflictPlan(plan.ArchivePath, plan.DestinationPath, options.ConflictStrategy);
            if (conflictPlan.Skip)
            {
                skipped.Add(plan.ArchivePath);
                conflicts.Add(new FileConflict
                {
                    Type = ConflictType.NameConflict,
                    SourcePath = plan.ArchivePath,
                    TargetPath = plan.DestinationPath,
                    Description = "Organize destination exists and was skipped"
                });
                return;
            }

            effectivePlan = new OrganizeExecutionPlan(plan.ArchivePath, conflictPlan.DestinationPath, conflictPlan.DeleteExisting);
        }

        try
        {
            if (!options.WhatIf)
            {
                if (effectivePlan.DeleteExisting && Directory.Exists(effectivePlan.DestinationPath))
                {
                    Directory.Delete(effectivePlan.DestinationPath, recursive: true);
                }

                Directory.CreateDirectory(effectivePlan.DestinationPath);
                ZipFile.ExtractToDirectory(effectivePlan.ArchivePath, effectivePlan.DestinationPath, overwriteFiles: true);

                if (cleanupMacArtifacts)
                {
                    CleanupMacOsArtifacts(effectivePlan.DestinationPath);
                }

                if (flattenTopLevel)
                {
                    FlattenSingleTopLevelDirectory(effectivePlan.DestinationPath);
                }

                if (organizeOptions?.KeepZip != true)
                {
                    var deleted = await FileOperationService.DeleteFileAsync(effectivePlan.ArchivePath, permanent: true).ConfigureAwait(false);
                    if (!deleted)
                    {
                        warnings.Add($"Unable to delete archive after extraction: {effectivePlan.ArchivePath}");
                    }
                }
            }

            processed.Add(effectivePlan.ArchivePath);
        }
        catch (InvalidDataException ex)
        {
            Logger.LogWarning(ex, "Invalid archive {Archive}", effectivePlan.ArchivePath);
            failed.Add(effectivePlan.ArchivePath);
            warnings.Add($"Invalid archive skipped: {effectivePlan.ArchivePath}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to organize archive {Archive}", effectivePlan.ArchivePath);
            failed.Add(effectivePlan.ArchivePath);
        }
    }

    private static TransformationResult BuildResult(
        int total,
        ConcurrentBag<string> processed,
        ConcurrentBag<string> skipped,
        ConcurrentBag<string> failed,
        ConcurrentBag<FileConflict> conflicts,
        ConcurrentBag<string> warnings)
    {
        return new TransformationResult
        {
            Success = failed.Count == 0,
            Message = failed.Count == 0 ? "Organize completed." : "Organize completed with errors.",
            ProcessedFiles = processed.Count,
            SkippedFiles = skipped.Count,
            FailedFiles = failed.Count,
            TotalFiles = total,
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
