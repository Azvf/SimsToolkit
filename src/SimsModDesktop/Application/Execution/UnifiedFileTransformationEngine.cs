
using System.Collections.Concurrent;
using System.IO.Compression;
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
    private static readonly string[] DefaultArchiveExtensions = [".zip"];

    private readonly ILogger<UnifiedFileTransformationEngine> _logger;
    private readonly IFileOperationService _fileOperationService;
    private readonly IHashComputationService _hashComputationService;
    private readonly IConfigurationProvider _configurationProvider;

    public UnifiedFileTransformationEngine(
        ILogger<UnifiedFileTransformationEngine> logger,
        IFileOperationService fileOperationService,
        IHashComputationService hashComputationService,
        IConfigurationProvider configurationProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _hashComputationService = hashComputationService ?? throw new ArgumentNullException(nameof(hashComputationService));
        _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
    }

    public IReadOnlyList<TransformationMode> SupportedModes =>
        [TransformationMode.Flatten, TransformationMode.Normalize, TransformationMode.Merge, TransformationMode.Organize];

    public TransformationEngineInfo EngineInfo => new()
    {
        Name = "UnifiedFileTransformationEngine",
        Version = "0.2.0",
        Description = "Windows-first unified engine for flatten/normalize/merge/organize.",
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

        return mode switch
        {
            TransformationMode.Flatten => await FlattenAsync(options, progress, cancellationToken),
            TransformationMode.Normalize => await NormalizeAsync(options, progress, cancellationToken),
            TransformationMode.Merge => await MergeAsync(options, progress, cancellationToken),
            TransformationMode.Organize => await OrganizeAsync(options, progress, cancellationToken),
            _ => new TransformationResult { Success = false, ErrorMessage = $"Unsupported mode: {mode}" }
        };
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

    private async Task<TransformationResult> FlattenAsync(
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
                await ProcessFlattenFileAsync(file, targetPath, options, processed, skipped, failed, conflicts, cancellationToken);
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

    private async Task ProcessFlattenFileAsync(
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

        try
        {
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
            _logger.LogError(ex, "Failed to flatten file {Path}", source.FullName);
            failed.Add(source.FullName);
        }
    }
    private async Task<string?> ResolveConflictAsync(
        string sourcePath,
        string targetPath,
        TransformationOptions options,
        CancellationToken cancellationToken)
    {
        switch (options.ConflictStrategy)
        {
            case ConflictResolutionStrategy.Skip:
            case ConflictResolutionStrategy.Prompt:
                return null;

            case ConflictResolutionStrategy.Overwrite:
                if (!options.WhatIf)
                {
                    File.Delete(targetPath);
                }
                return targetPath;

            case ConflictResolutionStrategy.KeepNewer:
            {
                var source = new FileInfo(sourcePath);
                var target = new FileInfo(targetPath);
                if (source.LastWriteTimeUtc <= target.LastWriteTimeUtc)
                {
                    return null;
                }

                if (!options.WhatIf)
                {
                    File.Delete(targetPath);
                }
                return targetPath;
            }

            case ConflictResolutionStrategy.KeepOlder:
            {
                var source = new FileInfo(sourcePath);
                var target = new FileInfo(targetPath);
                if (source.LastWriteTimeUtc >= target.LastWriteTimeUtc)
                {
                    return null;
                }

                if (!options.WhatIf)
                {
                    File.Delete(targetPath);
                }
                return targetPath;
            }

            case ConflictResolutionStrategy.HashCompare:
            {
                if (options.VerifyContent)
                {
                    var same = await _hashComputationService.AreFilesIdenticalAsync(sourcePath, targetPath, cancellationToken);
                    if (same)
                    {
                        return null;
                    }
                }

                if (!options.WhatIf)
                {
                    File.Delete(targetPath);
                }
                return targetPath;
            }

            default:
                return null;
        }
    }

    private async Task<TransformationResult> NormalizeAsync(
        TransformationOptions options,
        IProgress<TransformationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = EnumerateCandidateFiles(Path.GetFullPath(options.SourcePath), options).ToList();
        var normalizeOptions = options.ModeOptions?.Normalize;
        var replaceInvalidChars = await GetConfigBoolAsync("Normalize.ReplaceInvalidChars", true, cancellationToken);
        var casePolicy = await GetConfigStringAsync("Normalize.CasePolicy", "keep", cancellationToken);
        var autoRenameConflicts = normalizeOptions?.AutoRenameConflicts ?? true;
        var namingConvention = normalizeOptions?.NamingConvention;

        var processed = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();
        var conflicts = new List<FileConflict>();

        var total = files.Count;
        var current = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current++;

            try
            {
                var normalizedName = BuildNormalizedFileName(file, replaceInvalidChars, casePolicy, namingConvention);
                var destination = Path.Combine(file.DirectoryName!, normalizedName);

                if (IsSamePath(file.FullName, destination))
                {
                    skipped.Add(file.FullName);
                    ReportSimpleProgress(progress, "Normalize", current, total, file.FullName);
                    continue;
                }

                if (File.Exists(destination))
                {
                    if (autoRenameConflicts)
                    {
                        destination = GetUniquePath(destination);
                    }
                    else
                    {
                        var resolution = await ResolveConflictAsync(file.FullName, destination, options, cancellationToken);
                        if (resolution is null)
                        {
                            skipped.Add(file.FullName);
                            conflicts.Add(new FileConflict
                            {
                                Type = ConflictType.NameConflict,
                                SourcePath = file.FullName,
                                TargetPath = destination,
                                Description = "Normalize conflict skipped"
                            });
                            ReportSimpleProgress(progress, "Normalize", current, total, file.FullName);
                            continue;
                        }

                        destination = resolution;
                    }
                }

                if (!options.WhatIf)
                {
                    File.Move(file.FullName, destination);
                }

                processed.Add(file.FullName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Normalize failed for {Path}", file.FullName);
                failed.Add(file.FullName);
            }

            ReportSimpleProgress(progress, "Normalize", current, total, file.FullName);
        }

        return new TransformationResult
        {
            Success = failed.Count == 0,
            Message = failed.Count == 0 ? "Normalize completed." : "Normalize completed with errors.",
            ProcessedFiles = processed.Count,
            SkippedFiles = skipped.Count,
            FailedFiles = failed.Count,
            TotalFiles = total,
            ProcessedFileList = processed,
            SkippedFileList = skipped,
            FailedFileList = failed,
            Conflicts = conflicts
        };
    }

    private async Task<TransformationResult> MergeAsync(
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

        var allFiles = sourcePaths
            .Where(Directory.Exists)
            .SelectMany(source => EnumerateMergeFiles(source, options, mergeOptions.ModFilesOnly))
            .ToList();

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
                _logger.LogError(ex, "Merge failed for {Path}", item.File.FullName);
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
    private async Task<TransformationResult> OrganizeAsync(
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

        var archives = Directory.EnumerateFiles(sourcePath, "*", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(path => archiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToList();

        var processed = new List<string>();
        var failed = new List<string>();
        var skipped = new List<string>();
        var conflicts = new List<FileConflict>();
        var warnings = new List<string>();

        Directory.CreateDirectory(targetRoot);

        var current = 0;
        foreach (var archive in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current++;

            try
            {
                var destination = BuildOrganizeDestination(targetRoot, archive, organizeOptions);

                if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                {
                    var dirResolution = ResolveDirectoryConflict(archive, destination, options.ConflictStrategy, options.WhatIf);
                    if (dirResolution is null)
                    {
                        skipped.Add(archive);
                        conflicts.Add(new FileConflict
                        {
                            Type = ConflictType.NameConflict,
                            SourcePath = archive,
                            TargetPath = destination,
                            Description = "Organize destination exists and was skipped"
                        });
                        ReportSimpleProgress(progress, "Organize", current, archives.Count, archive);
                        continue;
                    }

                    destination = dirResolution;
                }

                if (!options.WhatIf)
                {
                    Directory.CreateDirectory(destination);
                    ZipFile.ExtractToDirectory(archive, destination, overwriteFiles: true);

                    if (cleanupMacArtifacts)
                    {
                        CleanupMacOsArtifacts(destination);
                    }

                    if (flattenTopLevel)
                    {
                        FlattenSingleTopLevelDirectory(destination);
                    }

                    if (organizeOptions?.KeepZip != true)
                    {
                        await _fileOperationService.DeleteFileAsync(archive, permanent: true);
                    }
                }

                processed.Add(archive);
            }
            catch (InvalidDataException ex)
            {
                _logger.LogWarning(ex, "Invalid archive {Archive}", archive);
                failed.Add(archive);
                warnings.Add($"Invalid archive skipped: {archive}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to organize archive {Archive}", archive);
                failed.Add(archive);
            }

            ReportSimpleProgress(progress, "Organize", current, archives.Count, archive);
        }

        return new TransformationResult
        {
            Success = failed.Count == 0,
            Message = failed.Count == 0 ? "Organize completed." : "Organize completed with errors.",
            ProcessedFiles = processed.Count,
            SkippedFiles = skipped.Count,
            FailedFiles = failed.Count,
            TotalFiles = archives.Count,
            ProcessedFileList = processed,
            SkippedFileList = skipped,
            FailedFileList = failed,
            Conflicts = conflicts,
            Warnings = warnings
        };
    }

    private static string BuildNormalizedFileName(FileInfo file, bool replaceInvalidChars, string casePolicy, string? namingConvention)
    {
        var stem = Path.GetFileNameWithoutExtension(file.Name).Trim();
        stem = Regex.Replace(stem, "\\s+", " ");

        if (replaceInvalidChars)
        {
            var invalid = Path.GetInvalidFileNameChars();
            stem = new string(stem.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }

        if (string.Equals(casePolicy, "lower", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem.ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(namingConvention) &&
            !Regex.IsMatch(stem, namingConvention, RegexOptions.CultureInvariant))
        {
            stem = Regex.Replace(stem, "[^a-zA-Z0-9._ -]", "_");
        }

        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "unnamed";
        }

        return stem + file.Extension;
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

    private static string? ResolveDirectoryConflict(string sourceArchive, string destination, ConflictResolutionStrategy strategy, bool whatIf)
    {
        switch (strategy)
        {
            case ConflictResolutionStrategy.Skip:
            case ConflictResolutionStrategy.Prompt:
                return null;
            case ConflictResolutionStrategy.Overwrite:
            case ConflictResolutionStrategy.HashCompare:
                if (!whatIf)
                {
                    Directory.Delete(destination, recursive: true);
                }
                return destination;
            case ConflictResolutionStrategy.KeepNewer:
            {
                var sourceTime = File.GetLastWriteTimeUtc(sourceArchive);
                var targetTime = Directory.GetLastWriteTimeUtc(destination);
                if (sourceTime <= targetTime)
                {
                    return null;
                }

                if (!whatIf)
                {
                    Directory.Delete(destination, recursive: true);
                }
                return destination;
            }
            case ConflictResolutionStrategy.KeepOlder:
            {
                var sourceTime = File.GetLastWriteTimeUtc(sourceArchive);
                var targetTime = Directory.GetLastWriteTimeUtc(destination);
                if (sourceTime >= targetTime)
                {
                    return null;
                }

                if (!whatIf)
                {
                    Directory.Delete(destination, recursive: true);
                }
                return destination;
            }
            default:
                return null;
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

    private static string GetUniquePath(string destination)
    {
        var directory = Path.GetDirectoryName(destination)!;
        var stem = Path.GetFileNameWithoutExtension(destination);
        var ext = Path.GetExtension(destination);

        var index = 1;
        var candidate = destination;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{stem} ({index}){ext}");
            index++;
        }

        return candidate;
    }

    private static bool IsPathOverlapping(string source, string target)
    {
        var normalizedSource = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedTarget = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalizedSource.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.StartsWith(normalizedSource, StringComparison.OrdinalIgnoreCase);
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

    private static void ReportSimpleProgress(
        IProgress<TransformationProgress>? progress,
        string stage,
        int current,
        int total,
        string file)
    {
        progress?.Report(new TransformationProgress
        {
            ProcessedCount = current,
            TotalCount = total,
            CurrentFile = file,
            CurrentStage = stage,
            StatusDetail = $"Processed {current}/{total}"
        });
    }

    private async Task<bool> GetConfigBoolAsync(string key, bool defaultValue, CancellationToken cancellationToken)
    {
        var configured = await _configurationProvider.GetConfigurationAsync<bool?>(key, cancellationToken);
        return configured ?? defaultValue;
    }

    private async Task<string> GetConfigStringAsync(string key, string defaultValue, CancellationToken cancellationToken)
    {
        var configured = await _configurationProvider.GetConfigurationAsync<string>(key, cancellationToken);
        return string.IsNullOrWhiteSpace(configured) ? defaultValue : configured;
    }

    private static IEnumerable<FileInfo> EnumerateCandidateFiles(string sourcePath, TransformationOptions options)
    {
        var searchOption = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var extensionFilter = NormalizeExtensions(options.FileExtensions);
        var excludePatterns = (options.ExcludePatterns ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => new Regex(p, RegexOptions.Compiled | RegexOptions.CultureInvariant))
            .ToArray();

        foreach (var path in Directory.EnumerateFiles(sourcePath, "*", searchOption))
        {
            var file = new FileInfo(path);
            if (extensionFilter is { Count: > 0 } && !extensionFilter.Contains(file.Extension))
            {
                continue;
            }

            if (excludePatterns.Any(regex => regex.IsMatch(path)))
            {
                continue;
            }

            var flattenModOnly = options.ModeOptions?.Flatten?.ModFilesOnly == true;
            var mergeModOnly = options.ModeOptions?.Merge?.ModFilesOnly == true;
            if ((flattenModOnly || mergeModOnly) && !IsModFileExtension(file.Extension))
            {
                continue;
            }

            yield return file;
        }
    }

    private static HashSet<string>? NormalizeExtensions(string[]? extensions)
    {
        if (extensions is not { Length: > 0 })
        {
            return null;
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in extensions)
        {
            if (string.IsNullOrWhiteSpace(ext))
            {
                continue;
            }

            normalized.Add(ext.StartsWith('.') ? ext : $".{ext}");
        }

        return normalized;
    }

    private static bool IsModFileExtension(string extension)
    {
        return extension.Equals(".package", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ts4script", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".trayitem", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".hhi", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".sgi", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".householdbinary", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bpi", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".blueprint", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSamePath(string left, string right)
    {
        var a = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var b = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
