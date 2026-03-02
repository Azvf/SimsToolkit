using System.IO.Compression;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Services;

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

        var archives = Directory.EnumerateFiles(
                sourcePath,
                "*",
                recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
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
                        var deleted = await FileOperationService.DeleteFileAsync(archive, permanent: true);
                        if (!deleted)
                        {
                            warnings.Add($"Unable to delete archive after extraction: {archive}");
                        }
                    }
                }

                processed.Add(archive);
            }
            catch (InvalidDataException ex)
            {
                Logger.LogWarning(ex, "Invalid archive {Archive}", archive);
                failed.Add(archive);
                warnings.Add($"Invalid archive skipped: {archive}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to organize archive {Archive}", archive);
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

    private static string? ResolveDirectoryConflict(
        string sourceArchive,
        string destination,
        ConflictResolutionStrategy strategy,
        bool whatIf)
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
}
