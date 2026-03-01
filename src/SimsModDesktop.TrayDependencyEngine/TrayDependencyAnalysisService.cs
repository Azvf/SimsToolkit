using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SimsModDesktop.TrayDependencyEngine;

public sealed class TrayDependencyAnalysisService : ITrayDependencyAnalysisService
{
    private static readonly Regex TrayIdentityRegex = new(
        "^0x([0-9a-fA-F]{1,8})!0x([0-9a-fA-F]{1,16})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SupportedTrayExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".trayitem",
        ".blueprint",
        ".bpi",
        ".room",
        ".rmi",
        ".householdbinary",
        ".hhi",
        ".sgi"
    };

    private readonly IPackageIndexCache _packageIndexCache;
    private readonly TrayBundleLoader _bundleLoader = new();
    private readonly TraySearchExtractor _searchExtractor = new();
    private readonly DirectMatchEngine _directMatchEngine = new();
    private readonly DependencyExpandEngine _dependencyExpandEngine = new();
    private readonly ModFileExporter _fileExporter = new();

    public TrayDependencyAnalysisService(IPackageIndexCache packageIndexCache)
    {
        _packageIndexCache = packageIndexCache;
    }

    public Task<TrayDependencyAnalysisResult> AnalyzeAsync(
        TrayDependencyAnalysisRequest request,
        IProgress<TrayDependencyAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(async () =>
        {
            var issues = new List<TrayDependencyIssue>();
            Report(progress, TrayDependencyAnalysisStage.Preparing, 0, "Locating tray files...");

            if (!TryLocateTraySourceFiles(request.TrayPath, request.TrayItemKey, issues, out var traySourceFiles))
            {
                return BuildResult(Array.Empty<TrayDependencyAnalysisRow>(), Array.Empty<TrayDependencyAnalysisRow>(), issues);
            }

            var snapshot = await _packageIndexCache.GetSnapshotAsync(
                request.ModsRootPath,
                new Progress<TrayDependencyExportProgress>(update =>
                {
                    if (update.Stage != TrayDependencyExportStage.IndexingPackages)
                    {
                        return;
                    }

                    Report(progress, TrayDependencyAnalysisStage.IndexingPackages, update.Percent, update.Detail);
                }),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyAnalysisStage.ParsingTray, 40, "Parsing tray references...");

            if (!_bundleLoader.TryLoad(traySourceFiles, issues, out var bundle))
            {
                return BuildResult(Array.Empty<TrayDependencyAnalysisRow>(), Array.Empty<TrayDependencyAnalysisRow>(), issues);
            }

            var searchKeys = _searchExtractor.Extract(bundle, issues);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyAnalysisStage.MatchingDirectReferences, 55, "Matching direct references...");

            var directMatch = _directMatchEngine.Match(searchKeys, snapshot);
            issues.AddRange(directMatch.Issues);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyAnalysisStage.ExpandingDependencies, 75, "Expanding second-level references...");

            var expansion = _dependencyExpandEngine.Expand(directMatch, snapshot, cancellationToken);
            issues.AddRange(expansion.Issues);

            var matchedRows = BuildMatchedRows(searchKeys, directMatch, expansion, snapshot);
            matchedRows = ApplyMatchedFilters(matchedRows, request);

            var buildUnusedRows = request.ExportUnusedPackages || !string.IsNullOrWhiteSpace(request.UnusedOutputCsv);
            var unusedRows = buildUnusedRows
                ? BuildUnusedRows(snapshot, matchedRows)
                : Array.Empty<TrayDependencyAnalysisRow>();

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyAnalysisStage.WritingOutputs, 90, "Writing outputs...");

            string? outputCsvPath = null;
            if (!string.IsNullOrWhiteSpace(request.OutputCsv))
            {
                outputCsvPath = WriteRowsCsv(
                    request.OutputCsv!,
                    matchedRows,
                    issues,
                    "matched package CSV");
            }

            string? unusedOutputCsvPath = null;
            if (!string.IsNullOrWhiteSpace(request.UnusedOutputCsv))
            {
                unusedOutputCsvPath = WriteRowsCsv(
                    request.UnusedOutputCsv!,
                    unusedRows,
                    issues,
                    "unused package CSV");
            }

            string? matchedExportPath = null;
            var exportedMatchedPackageCount = 0;
            if (request.ExportMatchedPackages)
            {
                if (!TryResolveExportRoot(request.ExportTargetPath, issues, out var exportRoot))
                {
                    return BuildResult(matchedRows, unusedRows, issues, outputCsvPath, unusedOutputCsvPath);
                }

                matchedExportPath = Path.Combine(exportRoot, "MatchedPackages");
                _fileExporter.CopyFiles(
                    matchedRows.Select(row => row.PackagePath).ToArray(),
                    matchedExportPath,
                    issues,
                    out exportedMatchedPackageCount);
            }

            string? unusedExportPath = null;
            var exportedUnusedPackageCount = 0;
            if (request.ExportUnusedPackages)
            {
                if (!TryResolveExportRoot(request.ExportTargetPath, issues, out var exportRoot))
                {
                    return BuildResult(
                        matchedRows,
                        unusedRows,
                        issues,
                        outputCsvPath,
                        unusedOutputCsvPath,
                        matchedExportPath,
                        null,
                        exportedMatchedPackageCount,
                        0);
                }

                unusedExportPath = Path.Combine(exportRoot, "UnusedPackages");
                _fileExporter.CopyFiles(
                    unusedRows.Select(row => row.PackagePath).ToArray(),
                    unusedExportPath,
                    issues,
                    out exportedUnusedPackageCount);
            }

            Report(progress, TrayDependencyAnalysisStage.Completed, 100, "Completed.");
            return BuildResult(
                matchedRows,
                unusedRows,
                issues,
                outputCsvPath,
                unusedOutputCsvPath,
                matchedExportPath,
                unusedExportPath,
                exportedMatchedPackageCount,
                exportedUnusedPackageCount);
        }, cancellationToken);
    }

    private static bool TryLocateTraySourceFiles(
        string trayPath,
        string trayItemKey,
        List<TrayDependencyIssue> issues,
        out string[] traySourceFiles)
    {
        traySourceFiles = Array.Empty<string>();
        var normalizedTrayPath = (trayPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedTrayPath))
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Error,
                Kind = TrayDependencyIssueKind.TrayParseError,
                Message = "Tray path is required."
            });
            return false;
        }

        if (!Directory.Exists(normalizedTrayPath))
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Error,
                Kind = TrayDependencyIssueKind.TrayParseError,
                FilePath = normalizedTrayPath,
                Message = "Tray path does not exist."
            });
            return false;
        }

        var normalizedKey = NormalizeKey(trayItemKey);
        if (string.IsNullOrWhiteSpace(normalizedKey))
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Error,
                Kind = TrayDependencyIssueKind.TrayParseError,
                Message = "Tray item key is required."
            });
            return false;
        }

        traySourceFiles = Directory
            .EnumerateFiles(normalizedTrayPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => SupportedTrayExtensions.Contains(Path.GetExtension(path)))
            .Where(path => MatchesTrayItemKey(path, normalizedKey))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (traySourceFiles.Length == 0)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Error,
                Kind = TrayDependencyIssueKind.TrayParseError,
                FilePath = normalizedTrayPath,
                ResourceKey = normalizedKey,
                Message = "No tray files were found for the selected tray item key."
            });
            return false;
        }

        return true;
    }

    private static bool MatchesTrayItemKey(string path, string normalizedKey)
    {
        var baseName = Path.GetFileNameWithoutExtension(path);
        if (string.Equals(NormalizeKey(baseName), normalizedKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var match = TrayIdentityRegex.Match(baseName);
        if (!match.Success)
        {
            return false;
        }

        return string.Equals($"0x{match.Groups[2].Value.ToLowerInvariant()}", normalizedKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeKey(string? rawValue)
    {
        var value = (rawValue ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return "0x" + value[2..].ToLowerInvariant();
        }

        return "0x" + value.ToLowerInvariant();
    }

    private static TrayDependencyAnalysisRow[] BuildMatchedRows(
        TraySearchKeys keys,
        DirectMatchResult directMatch,
        DependencyExpandResult expansion,
        PackageIndexSnapshot snapshot)
    {
        var directCounts = directMatch.DirectMatches
            .GroupBy(match => match.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Key).Distinct().Count(), StringComparer.OrdinalIgnoreCase);
        var transitiveCounts = expansion.ExpandedMatches
            .GroupBy(match => match.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Key).Distinct().Count(), StringComparer.OrdinalIgnoreCase);
        var allPaths = directCounts.Keys
            .Concat(transitiveCounts.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var packageSizes = snapshot.Packages.ToDictionary(package => package.FilePath, package => package.Length, StringComparer.OrdinalIgnoreCase);
        var totalSignalCount = CountRequestedSignals(keys);
        if (totalSignalCount <= 0)
        {
            totalSignalCount = 1;
        }

        return allPaths.Select(path =>
        {
            directCounts.TryGetValue(path, out var directCount);
            transitiveCounts.TryGetValue(path, out var transitiveCount);
            var matchCount = Math.Max(directCount, transitiveCount > 0 ? 1 : 0);
            var matchRatePct = Math.Round((directCount * 100d) / totalSignalCount, 2, MidpointRounding.AwayFromZero);
            packageSizes.TryGetValue(path, out var sizeBytes);

            return new TrayDependencyAnalysisRow
            {
                PackagePath = path,
                PackageSizeBytes = sizeBytes,
                DirectMatchCount = directCount,
                TransitiveMatchCount = transitiveCount,
                MatchInstanceCount = matchCount,
                MatchRatePct = matchRatePct,
                Confidence = DetermineConfidence(directCount, transitiveCount, matchRatePct),
                IsUnused = false
            };
        }).ToArray();
    }

    private static TrayDependencyAnalysisRow[] ApplyMatchedFilters(
        IReadOnlyList<TrayDependencyAnalysisRow> rows,
        TrayDependencyAnalysisRequest request)
    {
        var minMatchCount = request.MinMatchCount.GetValueOrDefault(1);
        var minimumConfidenceRank = GetConfidenceRank(request.ExportMinConfidence);

        IEnumerable<TrayDependencyAnalysisRow> filtered = rows
            .Where(row => row.MatchInstanceCount >= minMatchCount)
            .Where(row => GetConfidenceRank(row.Confidence) >= minimumConfidenceRank)
            .OrderByDescending(row => GetConfidenceRank(row.Confidence))
            .ThenByDescending(row => row.MatchInstanceCount)
            .ThenByDescending(row => row.DirectMatchCount)
            .ThenByDescending(row => row.MatchRatePct)
            .ThenBy(row => row.PackagePath, StringComparer.OrdinalIgnoreCase);

        if (request.TopN is int topN && topN > 0)
        {
            filtered = filtered.Take(topN);
        }

        if (request.MaxPackageCount is int maxPackageCount && maxPackageCount > 0)
        {
            filtered = filtered.Take(maxPackageCount);
        }

        return filtered.ToArray();
    }

    private static TrayDependencyAnalysisRow[] BuildUnusedRows(
        PackageIndexSnapshot snapshot,
        IReadOnlyList<TrayDependencyAnalysisRow> matchedRows)
    {
        var matchedPaths = matchedRows
            .Select(row => row.PackagePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return snapshot.Packages
            .Where(package => !matchedPaths.Contains(package.FilePath))
            .OrderBy(package => package.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(package => new TrayDependencyAnalysisRow
            {
                PackagePath = package.FilePath,
                PackageSizeBytes = package.Length,
                DirectMatchCount = 0,
                TransitiveMatchCount = 0,
                MatchInstanceCount = 0,
                MatchRatePct = 0,
                Confidence = "Low",
                IsUnused = true
            })
            .ToArray();
    }

    private static int CountRequestedSignals(TraySearchKeys keys)
    {
        return keys.CasPartIds
            .Concat(keys.SkinToneIds)
            .Concat(keys.SimAspirationIds)
            .Concat(keys.SimTraitIds)
            .Concat(keys.CasPresetIds)
            .Concat(keys.FaceSliderIds)
            .Concat(keys.BodySliderIds)
            .Concat(keys.ObjectDefinitionIds)
            .Concat(keys.LotTraitIds)
            .Distinct()
            .Count() + keys.ResourceKeys.Distinct().Count();
    }

    private static string DetermineConfidence(int directCount, int transitiveCount, double matchRatePct)
    {
        if (directCount >= 3 || matchRatePct >= 30d)
        {
            return "High";
        }

        if (directCount >= 2 || transitiveCount > 0 || matchRatePct >= 10d)
        {
            return "Medium";
        }

        return "Low";
    }

    private static int GetConfidenceRank(string? confidence)
    {
        return (confidence ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "high" => 3,
            "medium" => 2,
            _ => 1
        };
    }

    private static string? WriteRowsCsv(
        string csvPath,
        IReadOnlyList<TrayDependencyAnalysisRow> rows,
        List<TrayDependencyIssue> issues,
        string label)
    {
        try
        {
            var fullPath = Path.GetFullPath(csvPath.Trim());
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var writer = new StreamWriter(fullPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine("PackagePath,Confidence,MatchInstanceCount,MatchRatePct,PackageSizeBytes");
            foreach (var row in rows)
            {
                writer.WriteLine(string.Join(
                    ",",
                    EscapeCsv(row.PackagePath),
                    EscapeCsv(row.Confidence),
                    row.MatchInstanceCount.ToString(CultureInfo.InvariantCulture),
                    row.MatchRatePct.ToString("0.##", CultureInfo.InvariantCulture),
                    row.PackageSizeBytes.ToString(CultureInfo.InvariantCulture)));
            }

            return fullPath;
        }
        catch (Exception ex)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Error,
                Kind = TrayDependencyIssueKind.CopyError,
                FilePath = csvPath,
                Message = $"Failed to write {label}: {ex.Message}"
            });
            return null;
        }
    }

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (text.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static bool TryResolveExportRoot(
        string? exportTargetPath,
        List<TrayDependencyIssue> issues,
        out string exportRoot)
    {
        exportRoot = string.Empty;
        var normalized = (exportTargetPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Error,
                Kind = TrayDependencyIssueKind.CopyError,
                Message = "Export target path is required when exporting dependency packages."
            });
            return false;
        }

        exportRoot = Path.GetFullPath(normalized);
        Directory.CreateDirectory(exportRoot);
        return true;
    }

    private static TrayDependencyAnalysisResult BuildResult(
        IReadOnlyList<TrayDependencyAnalysisRow> matchedRows,
        IReadOnlyList<TrayDependencyAnalysisRow> unusedRows,
        List<TrayDependencyIssue> issues,
        string? outputCsvPath = null,
        string? unusedOutputCsvPath = null,
        string? matchedExportPath = null,
        string? unusedExportPath = null,
        int exportedMatchedPackageCount = 0,
        int exportedUnusedPackageCount = 0)
    {
        var success = !issues.Any(issue => issue.Severity == TrayDependencyIssueSeverity.Error);
        return new TrayDependencyAnalysisResult
        {
            Success = success,
            MatchedPackageCount = matchedRows.Count,
            UnusedPackageCount = unusedRows.Count,
            ExportedMatchedPackageCount = exportedMatchedPackageCount,
            ExportedUnusedPackageCount = exportedUnusedPackageCount,
            OutputCsvPath = outputCsvPath,
            UnusedOutputCsvPath = unusedOutputCsvPath,
            MatchedExportPath = matchedExportPath,
            UnusedExportPath = unusedExportPath,
            MatchedPackages = matchedRows.ToArray(),
            UnusedPackages = unusedRows.ToArray(),
            Issues = issues.ToArray()
        };
    }

    private static void Report(
        IProgress<TrayDependencyAnalysisProgress>? progress,
        TrayDependencyAnalysisStage stage,
        int percent,
        string detail)
    {
        progress?.Report(new TrayDependencyAnalysisProgress
        {
            Stage = stage,
            Percent = Math.Clamp(percent, 0, 100),
            Detail = detail
        });
    }
}
