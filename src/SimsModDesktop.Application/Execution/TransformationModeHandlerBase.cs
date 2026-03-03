using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Services;

namespace SimsModDesktop.Application.Execution;

internal interface ITransformationModeHandler
{
    TransformationMode Mode { get; }

    Task<TransformationResult> TransformAsync(
        TransformationOptions options,
        IProgress<TransformationProgress>? progress,
        CancellationToken cancellationToken);
}

internal abstract class TransformationModeHandlerBase : ITransformationModeHandler
{
    protected TransformationModeHandlerBase(
        ILogger logger,
        IFileOperationService fileOperationService,
        IHashComputationService hashComputationService,
        IConfigurationProvider configurationProvider)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        FileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        HashComputationService = hashComputationService ?? throw new ArgumentNullException(nameof(hashComputationService));
        ConfigurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
    }

    public abstract TransformationMode Mode { get; }

    protected ILogger Logger { get; }
    protected IFileOperationService FileOperationService { get; }
    protected IHashComputationService HashComputationService { get; }
    protected IConfigurationProvider ConfigurationProvider { get; }

    public abstract Task<TransformationResult> TransformAsync(
        TransformationOptions options,
        IProgress<TransformationProgress>? progress,
        CancellationToken cancellationToken);

    protected async Task<string?> ResolveConflictAsync(
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
                    var same = await HashComputationService.AreFilesIdenticalAsync(sourcePath, targetPath, cancellationToken);
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

    protected async Task<bool> GetConfigBoolAsync(string key, bool defaultValue, CancellationToken cancellationToken)
    {
        var configured = await ConfigurationProvider.GetConfigurationAsync<bool?>(key, cancellationToken);
        return configured ?? defaultValue;
    }

    protected async Task<string> GetConfigStringAsync(string key, string defaultValue, CancellationToken cancellationToken)
    {
        var configured = await ConfigurationProvider.GetConfigurationAsync<string>(key, cancellationToken);
        return string.IsNullOrWhiteSpace(configured) ? defaultValue : configured;
    }

    protected static void ReportSimpleProgress(
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

    protected static IEnumerable<FileInfo> EnumerateCandidateFiles(string sourcePath, TransformationOptions options)
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

    protected static HashSet<string>? NormalizeExtensions(string[]? extensions)
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

    protected static string GetUniquePath(string destination)
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

    protected static bool IsPathOverlapping(string source, string target)
    {
        var normalizedSource = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedTarget = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalizedSource.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase)
               || normalizedTarget.StartsWith(normalizedSource, StringComparison.OrdinalIgnoreCase);
    }

    protected static bool IsSamePath(string left, string right)
    {
        var a = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var b = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
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
}
