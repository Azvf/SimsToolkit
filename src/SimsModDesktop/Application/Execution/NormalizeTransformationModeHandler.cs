using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Services;

namespace SimsModDesktop.Application.Execution;

internal sealed class NormalizeTransformationModeHandler : TransformationModeHandlerBase
{
    public NormalizeTransformationModeHandler(
        ILogger logger,
        IFileOperationService fileOperationService,
        IHashComputationService hashComputationService,
        IConfigurationProvider configurationProvider)
        : base(logger, fileOperationService, hashComputationService, configurationProvider)
    {
    }

    public override TransformationMode Mode => TransformationMode.Normalize;

    public override async Task<TransformationResult> TransformAsync(
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
                Logger.LogError(ex, "Normalize failed for {Path}", file.FullName);
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

        if (!string.IsNullOrWhiteSpace(namingConvention)
            && !Regex.IsMatch(stem, namingConvention, RegexOptions.CultureInvariant))
        {
            stem = Regex.Replace(stem, "[^a-zA-Z0-9._ -]", "_");
        }

        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "unnamed";
        }

        return stem + file.Extension;
    }
}
