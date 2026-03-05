using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.TrayDependencyEngine;

public sealed class TrayDependencyExportService : ITrayDependencyExportService
{
    private readonly IPackageIndexCache _packageIndexCache;
    private readonly ILogger<TrayDependencyExportService> _logger;
    private readonly IPathIdentityResolver _pathIdentityResolver;
    private readonly TrayBundleLoader _bundleLoader = new();
    private readonly TraySearchExtractor _searchExtractor = new();
    private readonly DirectMatchEngine _directMatchEngine = new();
    private readonly DependencyExpandEngine _dependencyExpandEngine;
    private readonly ModFileExporter _fileExporter = new();

    public TrayDependencyExportService(
        IPackageIndexCache packageIndexCache,
        IDbpfResourceReader? resourceReader = null,
        ILogger<TrayDependencyExportService>? logger = null,
        IPathIdentityResolver? pathIdentityResolver = null)
    {
        _packageIndexCache = packageIndexCache;
        _logger = logger ?? NullLogger<TrayDependencyExportService>.Instance;
        _pathIdentityResolver = pathIdentityResolver ?? new SystemPathIdentityResolver();
        _dependencyExpandEngine = new DependencyExpandEngine(resourceReader ?? new DbpfResourceReader(), _logger);
    }

    public Task<TrayDependencyExportResult> ExportAsync(
        TrayDependencyExportRequest request,
        IProgress<TrayDependencyExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(async () =>
        {
            var timing = Stopwatch.StartNew();
            var issues = new List<TrayDependencyIssue>();
            var inputSourceFileCount = request.TraySourceFiles
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var bundleTrayItemFileCount = 0;
            var bundleAuxiliaryFileCount = 0;
            var candidateResourceKeyCount = 0;
            var candidateIdCount = 0;
            var snapshotPackageCount = 0;
            var directMatchCount = 0;
            var expandedMatchCount = 0;
            var resolvedModsRoot = ResolveDirectory(request.ModsRootPath);
            _logger.LogInformation(
                "Tray export start trayKey={TrayItemKey} title={ItemTitle} sourceFiles={SourceFileCount} modsPath={ModsPath} preloadedSnapshot={HasPreloadedSnapshot}",
                request.TrayItemKey,
                request.ItemTitle,
                inputSourceFileCount,
                resolvedModsRoot.CanonicalPath,
                request.PreloadedSnapshot is not null);
            _logger.LogInformation(
                "path.resolve component={Component} rawPath={RawPath} canonicalPath={CanonicalPath} exists={Exists} isReparse={IsReparse} linkTarget={LinkTarget}",
                "trayexport",
                resolvedModsRoot.FullPath,
                resolvedModsRoot.CanonicalPath,
                resolvedModsRoot.Exists,
                resolvedModsRoot.IsReparsePoint,
                resolvedModsRoot.LinkTarget ?? string.Empty);

            Report(progress, TrayDependencyExportStage.Preparing, 0, "Copying tray files...");

            Directory.CreateDirectory(request.TrayExportRoot);
            Directory.CreateDirectory(request.ModsExportRoot);

            if (!TryCopyTrayFiles(request.TraySourceFiles, request.TrayExportRoot, issues, out var copiedTrayFileCount))
            {
                return BuildResult(
                    copiedTrayFileCount,
                    0,
                    issues,
                    BuildDiagnostics(
                        inputSourceFileCount,
                        bundleTrayItemFileCount,
                        bundleAuxiliaryFileCount,
                        candidateResourceKeyCount,
                        candidateIdCount,
                        snapshotPackageCount,
                        directMatchCount,
                        expandedMatchCount));
            }

            PackageIndexSnapshot snapshot;
            if (request.PreloadedSnapshot is not null && MatchesModsRoot(request.PreloadedSnapshot, request.ModsRootPath))
            {
                snapshot = request.PreloadedSnapshot;
                progress?.Report(new TrayDependencyExportProgress
                {
                    Stage = TrayDependencyExportStage.IndexingPackages,
                    Percent = 35,
                    Detail = $"Indexing packages... {snapshot.Packages.Count}/{snapshot.Packages.Count}"
                });
            }
            else
            {
                issues.Add(new TrayDependencyIssue
                {
                    Severity = TrayDependencyIssueSeverity.Error,
                    Kind = TrayDependencyIssueKind.CacheBuildError,
                    Message = "Tray dependency cache is not ready. Open the Tray page and wait for cache warmup to finish before exporting."
                });
                _logger.LogWarning(
                    "Tray export blocked because no preloaded snapshot was provided trayKey={TrayItemKey} modsPath={ModsPath}",
                    request.TrayItemKey,
                    request.ModsRootPath);
                return BuildResult(
                    copiedTrayFileCount,
                    0,
                    issues,
                    BuildDiagnostics(
                        inputSourceFileCount,
                        bundleTrayItemFileCount,
                        bundleAuxiliaryFileCount,
                        candidateResourceKeyCount,
                        candidateIdCount,
                        snapshotPackageCount,
                        directMatchCount,
                        expandedMatchCount));
            }

            snapshotPackageCount = snapshot.Packages.Count;

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyExportStage.ParsingTray, 35, "Parsing tray files...");

            if (!_bundleLoader.TryLoad(request.TraySourceFiles, issues, out var bundle))
            {
                return BuildResult(
                    copiedTrayFileCount,
                    0,
                    issues,
                    BuildDiagnostics(
                        inputSourceFileCount,
                        bundleTrayItemFileCount,
                        bundleAuxiliaryFileCount,
                        candidateResourceKeyCount,
                        candidateIdCount,
                        snapshotPackageCount,
                        directMatchCount,
                        expandedMatchCount));
            }

            bundleTrayItemFileCount = bundle.TrayItemPaths.Count;
            bundleAuxiliaryFileCount =
                bundle.HhiPaths.Count +
                bundle.SgiPaths.Count +
                bundle.HouseholdBinaryPaths.Count +
                bundle.BlueprintPaths.Count +
                bundle.RoomPaths.Count;
            var searchKeys = _searchExtractor.Extract(bundle, issues);
            candidateResourceKeyCount = searchKeys.ResourceKeys.Length;
            candidateIdCount = CountCandidateIds(searchKeys);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyExportStage.MatchingDirectReferences, 45, "Matching direct references...");

            using var lookupSession = snapshot.Lookup.OpenSession();

            var directMatch = _directMatchEngine.Match(searchKeys, lookupSession);
            issues.AddRange(directMatch.Issues);
            directMatchCount = directMatch.DirectMatches.Count;
            if (directMatchCount == 0)
            {
                _logger.LogWarning(
                    "trayexport.directmatch.none trayKey={TrayItemKey} title={ItemTitle} candidateIds={CandidateIdCount} resourceKeys={ResourceKeyCount} snapshotPackages={SnapshotPackageCount}",
                    request.TrayItemKey,
                    request.ItemTitle,
                    candidateIdCount,
                    candidateResourceKeyCount,
                    snapshotPackageCount);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyExportStage.ExpandingDependencies, 65, "Expanding second-level references...");

            var expansion = _dependencyExpandEngine.Expand(
                directMatch,
                lookupSession,
                cancellationToken);
            issues.AddRange(expansion.Issues);
            expandedMatchCount = expansion.ExpandedMatches.Count;
            if (directMatchCount == 0 && expandedMatchCount == 0)
            {
                _logger.LogWarning(
                    "trayexport.nomodmatches trayKey={TrayItemKey} title={ItemTitle} candidateIds={CandidateIdCount} resourceKeys={ResourceKeyCount} snapshotPackages={SnapshotPackageCount}",
                    request.TrayItemKey,
                    request.ItemTitle,
                    candidateIdCount,
                    candidateResourceKeyCount,
                    snapshotPackageCount);
            }

            var filePaths = directMatch.DirectMatches
                .Concat(expansion.ExpandedMatches)
                .Select(match => match.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyExportStage.CopyingMods, 85, $"Copying referenced mods ({filePaths.Length})...");

            _fileExporter.CopyFiles(filePaths, request.ModsExportRoot, issues, progress, out var copiedModFileCount);

            Report(progress, TrayDependencyExportStage.Completed, 100, "Completed.");
            var result = BuildResult(
                copiedTrayFileCount,
                copiedModFileCount,
                issues,
                BuildDiagnostics(
                    inputSourceFileCount,
                    bundleTrayItemFileCount,
                    bundleAuxiliaryFileCount,
                    candidateResourceKeyCount,
                    candidateIdCount,
                    snapshotPackageCount,
                    directMatchCount,
                    expandedMatchCount));
            _logger.LogInformation(
                "Tray export completed trayKey={TrayItemKey} success={Success} trayFiles={CopiedTrayFiles} modFiles={CopiedModFiles} issues={IssueCount} elapsedMs={ElapsedMs}",
                request.TrayItemKey,
                result.Success,
                result.CopiedTrayFileCount,
                result.CopiedModFileCount,
                result.Issues.Count,
                timing.ElapsedMilliseconds);
            return result;
        }, cancellationToken);
    }

    private static bool TryCopyTrayFiles(
        IReadOnlyList<string> traySourceFiles,
        string trayExportRoot,
        List<TrayDependencyIssue> issues,
        out int copiedFileCount)
    {
        copiedFileCount = 0;
        foreach (var sourcePath in traySourceFiles
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    issues.Add(new TrayDependencyIssue
                    {
                        Severity = TrayDependencyIssueSeverity.Error,
                        Kind = TrayDependencyIssueKind.CopyError,
                        FilePath = sourcePath,
                        Message = "Tray source file is missing."
                    });
                    return false;
                }

                var targetPath = Path.Combine(trayExportRoot, Path.GetFileName(sourcePath));
                targetPath = FileNameHelpers.GetUniquePath(targetPath);
                File.Copy(sourcePath, targetPath, overwrite: false);
                copiedFileCount++;
            }
            catch (Exception ex)
            {
                issues.Add(new TrayDependencyIssue
                {
                    Severity = TrayDependencyIssueSeverity.Error,
                    Kind = TrayDependencyIssueKind.CopyError,
                    FilePath = sourcePath,
                    Message = $"Failed to copy tray file: {ex.Message}"
                });
                return false;
            }
        }

        return true;
    }

    private static TrayDependencyExportResult BuildResult(
        int copiedTrayFileCount,
        int copiedModFileCount,
        List<TrayDependencyIssue> issues,
        TrayDependencyExportDiagnostics? diagnostics)
    {
        var hasErrors = issues.Any(issue => issue.Severity == TrayDependencyIssueSeverity.Error);
        var hasMissingWarnings = issues.Any(issue =>
            issue.Severity == TrayDependencyIssueSeverity.Warning &&
            (issue.Kind == TrayDependencyIssueKind.MissingReference || issue.Kind == TrayDependencyIssueKind.MissingSourceFile));

        return new TrayDependencyExportResult
        {
            Success = !hasErrors,
            CopiedTrayFileCount = copiedTrayFileCount,
            CopiedModFileCount = copiedModFileCount,
            HasMissingReferenceWarnings = hasMissingWarnings,
            Issues = issues.ToArray(),
            Diagnostics = diagnostics
        };
    }

    private static TrayDependencyExportDiagnostics BuildDiagnostics(
        int inputSourceFileCount,
        int bundleTrayItemFileCount,
        int bundleAuxiliaryFileCount,
        int candidateResourceKeyCount,
        int candidateIdCount,
        int snapshotPackageCount,
        int directMatchCount,
        int expandedMatchCount)
    {
        return new TrayDependencyExportDiagnostics
        {
            InputSourceFileCount = inputSourceFileCount,
            BundleTrayItemFileCount = bundleTrayItemFileCount,
            BundleAuxiliaryFileCount = bundleAuxiliaryFileCount,
            CandidateResourceKeyCount = candidateResourceKeyCount,
            CandidateIdCount = candidateIdCount,
            SnapshotPackageCount = snapshotPackageCount,
            DirectMatchCount = directMatchCount,
            ExpandedMatchCount = expandedMatchCount
        };
    }

    private static int CountCandidateIds(TraySearchKeys keys)
    {
        var ids = new HashSet<ulong>();
        Add(keys.CasPartIds);
        Add(keys.SkinToneIds);
        Add(keys.SimAspirationIds);
        Add(keys.SimTraitIds);
        Add(keys.CasPresetIds);
        Add(keys.FaceSliderIds);
        Add(keys.BodySliderIds);
        Add(keys.ObjectDefinitionIds);
        Add(keys.LotTraitIds);
        return ids.Count;

        void Add(IReadOnlyList<ulong> values)
        {
            for (var index = 0; index < values.Count; index++)
            {
                ids.Add(values[index]);
            }
        }
    }

    private bool MatchesModsRoot(PackageIndexSnapshot snapshot, string modsRootPath)
    {
        if (string.IsNullOrWhiteSpace(snapshot.ModsRootPath) || string.IsNullOrWhiteSpace(modsRootPath))
        {
            return false;
        }

        return _pathIdentityResolver.EqualsDirectory(snapshot.ModsRootPath, modsRootPath);
    }

    private ResolvedPathInfo ResolveDirectory(string path)
    {
        var resolved = _pathIdentityResolver.ResolveDirectory(path);
        var fullPath = !string.IsNullOrWhiteSpace(resolved.FullPath)
            ? resolved.FullPath
            : path.Trim().Trim('"');
        var canonicalPath = !string.IsNullOrWhiteSpace(resolved.CanonicalPath)
            ? resolved.CanonicalPath
            : fullPath;
        return resolved with
        {
            FullPath = fullPath,
            CanonicalPath = canonicalPath
        };
    }

    private static void Report(
        IProgress<TrayDependencyExportProgress>? progress,
        TrayDependencyExportStage stage,
        int percent,
        string detail)
    {
        progress?.Report(new TrayDependencyExportProgress
        {
            Stage = stage,
            Percent = Math.Clamp(percent, 0, 100),
            Detail = detail
        });
    }
}


internal static class ProgressScale
{
    public static int Scale(int start, int end, int current, int total)
    {
        if (total <= 0)
        {
            return end;
        }

        var clamped = Math.Clamp(current, 0, total);
        var value = start + ((end - start) * (double)clamped / total);
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}

internal sealed class TrayBundleLoader
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".trayitem",
        ".hhi",
        ".sgi",
        ".householdbinary",
        ".blueprint",
        ".bpi",
        ".room",
        ".rmi"
    };

    private static readonly System.Text.RegularExpressions.Regex TrayIdentityRegex = new(
        "^0x([0-9a-fA-F]{1,8})!0x([0-9a-fA-F]{1,16})$",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public bool TryLoad(
        IReadOnlyList<string> sourceFilePaths,
        List<TrayDependencyIssue> issues,
        out TrayFileBundle bundle)
    {
        var normalizedSourcePaths = ExpandCompanionPaths(sourceFilePaths);
        if (normalizedSourcePaths.Length == 0)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Error,
                Kind = TrayDependencyIssueKind.TrayParseError,
                Message = "No tray source files were provided for export parsing."
            });
            bundle = new TrayFileBundle();
            return false;
        }

        var trayItems = FilterByExtension(normalizedSourcePaths, ".trayitem");
        var hhiPaths = FilterByExtension(normalizedSourcePaths, ".hhi");
        var sgiPaths = FilterByExtension(normalizedSourcePaths, ".sgi");
        var householdBinaryPaths = FilterByExtension(normalizedSourcePaths, ".householdbinary");
        var blueprintPaths = FilterByExtension(normalizedSourcePaths, ".blueprint", ".bpi");
        var roomPaths = FilterByExtension(normalizedSourcePaths, ".room", ".rmi");

        if (trayItems.Length == 0)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Warning,
                Kind = TrayDependencyIssueKind.MissingReference,
                Message = ".trayitem file is missing; continuing with auxiliary tray files."
            });
        }

        bundle = new TrayFileBundle
        {
            TrayItemPaths = trayItems,
            HhiPaths = hhiPaths,
            SgiPaths = sgiPaths,
            HouseholdBinaryPaths = householdBinaryPaths,
            BlueprintPaths = blueprintPaths,
            RoomPaths = roomPaths
        };
        return true;
    }

    private static string[] ExpandCompanionPaths(IReadOnlyList<string> sourceFilePaths)
    {
        var normalizedPaths = sourceFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedPaths.Length == 0)
        {
            return Array.Empty<string>();
        }

        var result = new HashSet<string>(normalizedPaths, StringComparer.OrdinalIgnoreCase);
        var trayKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < normalizedPaths.Length; i++)
        {
            var path = normalizedPaths[i];
            trayKeys.Add(NormalizeTrayStem(Path.GetFileNameWithoutExtension(path)));
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                directories.Add(directory);
            }
        }

        foreach (var directory in directories)
        {
            foreach (var candidate in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                if (!IsSupportedTraySource(candidate))
                {
                    continue;
                }

                var stem = NormalizeTrayStem(Path.GetFileNameWithoutExtension(candidate));
                if (!trayKeys.Contains(stem))
                {
                    continue;
                }

                result.Add(candidate);
            }
        }

        return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] FilterByExtension(
        IReadOnlyList<string> sourceFilePaths,
        params string[] extensions)
    {
        return sourceFilePaths
            .Where(path =>
            {
                var extension = Path.GetExtension(path);
                for (var index = 0; index < extensions.Length; index++)
                {
                    if (string.Equals(extension, extensions[index], StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSupportedTraySource(string path)
    {
        var extension = Path.GetExtension(path);
        return SupportedExtensions.Contains(extension);
    }

    private static string NormalizeTrayStem(string? rawValue)
    {
        var value = (rawValue ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var match = TrayIdentityRegex.Match(value);
        if (match.Success)
        {
            return "0x" + match.Groups[2].Value.ToLowerInvariant();
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return "0x" + value[2..].ToLowerInvariant();
        }

        return "0x" + value.ToLowerInvariant();
    }
}

internal sealed class TraySearchExtractor
{
    public TraySearchKeys Extract(TrayFileBundle bundle, List<TrayDependencyIssue> issues)
    {
        var householdIds = new HashSet<ulong>();
        var buildIds = new HashSet<ulong>();
        var resourceKeys = new HashSet<TrayResourceKey>();

        ScanFiles(bundle.TrayItemPaths, issues, householdIds, resourceKeys);
        ScanFiles(bundle.HhiPaths, issues, householdIds, resourceKeys);
        ScanFiles(bundle.SgiPaths, issues, householdIds, resourceKeys);
        ScanFiles(bundle.HouseholdBinaryPaths, issues, householdIds, resourceKeys);
        ScanFiles(bundle.BlueprintPaths, issues, buildIds, resourceKeys);
        ScanFiles(bundle.RoomPaths, issues, buildIds, resourceKeys);

        var householdArray = householdIds.OrderBy(value => value).ToArray();
        var buildArray = buildIds.OrderBy(value => value).ToArray();
        var resourceKeyArray = resourceKeys
            .OrderBy(key => key.Type)
            .ThenBy(key => key.Group)
            .ThenBy(key => key.Instance)
            .ToArray();

        if (householdArray.Length == 0 && buildArray.Length == 0 && resourceKeyArray.Length == 0)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Warning,
                Kind = TrayDependencyIssueKind.MissingReference,
                Message = "No candidate references were detected in tray files."
            });
        }

        return new TraySearchKeys
        {
            CasPartIds = householdArray,
            SkinToneIds = householdArray,
            SimAspirationIds = householdArray,
            SimTraitIds = householdArray,
            CasPresetIds = householdArray,
            FaceSliderIds = householdArray,
            BodySliderIds = householdArray,
            ObjectDefinitionIds = buildArray.Length == 0 ? householdArray : buildArray,
            ResourceKeys = resourceKeyArray,
            LotTraitIds = buildArray
        };
    }

    private static void ScanFile(
        string? path,
        List<TrayDependencyIssue> issues,
        HashSet<ulong> ids,
        HashSet<TrayResourceKey> resourceKeys)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (!File.Exists(path))
            {
                issues.Add(new TrayDependencyIssue
                {
                    Severity = TrayDependencyIssueSeverity.Warning,
                    Kind = TrayDependencyIssueKind.MissingSourceFile,
                    FilePath = path,
                    Message = "Tray file disappeared before parsing."
                });
                return;
            }

            var data = File.ReadAllBytes(path);
            BinaryReferenceScanner.Scan(data, ids, resourceKeys);
        }
        catch (Exception ex)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Error,
                Kind = TrayDependencyIssueKind.TrayParseError,
                FilePath = path,
                Message = $"Failed to parse tray file: {ex.Message}"
            });
        }
    }

    private static void ScanFiles(
        IReadOnlyList<string> paths,
        List<TrayDependencyIssue> issues,
        HashSet<ulong> ids,
        HashSet<TrayResourceKey> resourceKeys)
    {
        foreach (var path in paths)
        {
            ScanFile(path, issues, ids, resourceKeys);
        }
    }
}

internal static class BinaryReferenceScanner
{
    public static void Scan(
        ReadOnlySpan<byte> data,
        HashSet<ulong> ids,
        HashSet<TrayResourceKey> resourceKeys)
    {
        if (data.IsEmpty)
        {
            return;
        }

        ScanResourceKeys(data, resourceKeys);
        ParseMessage(data, 0, ids, resourceKeys);

        if (ids.Count == 0)
        {
            for (var i = 0; i <= data.Length - 8; i += 4)
            {
                var value = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(i, 8));
                if (LooksLikeResourceId(value))
                {
                    ids.Add(value);
                }
            }
        }
    }

    private static void ParseMessage(
        ReadOnlySpan<byte> data,
        int depth,
        HashSet<ulong> ids,
        HashSet<TrayResourceKey> resourceKeys)
    {
        if (depth > 4 || data.Length == 0)
        {
            return;
        }

        var position = 0;
        while (position < data.Length)
        {
            var start = position;
            if (!TryReadVarint(data, ref position, out var key))
            {
                break;
            }

            var wireType = (int)(key & 0x7);
            switch (wireType)
            {
                case 0:
                    if (!TryReadVarint(data, ref position, out var varintValue))
                    {
                        position = start + 1;
                        continue;
                    }

                    if (LooksLikeResourceId(varintValue))
                    {
                        ids.Add(varintValue);
                    }
                    break;
                case 1:
                    if (position + 8 > data.Length)
                    {
                        position = data.Length;
                        break;
                    }

                    var fixed64 = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(position, 8));
                    position += 8;
                    if (LooksLikeResourceId(fixed64))
                    {
                        ids.Add(fixed64);
                    }
                    break;
                case 2:
                    if (!TryReadVarint(data, ref position, out var lengthValue))
                    {
                        position = start + 1;
                        continue;
                    }

                    if (lengthValue > int.MaxValue || position + (int)lengthValue > data.Length)
                    {
                        position = start + 1;
                        continue;
                    }

                    var slice = data.Slice(position, (int)lengthValue);
                    position += (int)lengthValue;
                    ScanResourceKeys(slice, resourceKeys);
                    ParseMessage(slice, depth + 1, ids, resourceKeys);
                    break;
                case 5:
                    if (position + 4 > data.Length)
                    {
                        position = data.Length;
                        break;
                    }

                    var fixed32 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position, 4));
                    position += 4;
                    if (LooksLikeResourceId(fixed32))
                    {
                        ids.Add(fixed32);
                    }
                    break;
                default:
                    position = start + 1;
                    break;
            }
        }
    }

    private static void ScanResourceKeys(ReadOnlySpan<byte> data, HashSet<TrayResourceKey> resourceKeys)
    {
        for (var offset = 0; offset <= data.Length - 16; offset += 4)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            if (!KnownResourceTypes.IsSupported(type))
            {
                continue;
            }

            var group = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            var instance = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 8, 8));
            if (!LooksLikeResourceId(instance))
            {
                continue;
            }

            resourceKeys.Add(new TrayResourceKey(type, group, instance));
        }
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> data, ref int position, out ulong value)
    {
        value = 0;
        var shift = 0;
        while (position < data.Length && shift < 64)
        {
            var b = data[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        value = 0;
        return false;
    }

    internal static bool LooksLikeResourceId(ulong value)
    {
        if (value == 0 || value == ulong.MaxValue)
        {
            return false;
        }

        return value > 1024;
    }
}

internal static class KnownResourceTypes
{
    public const uint CasPart = 55242443u;
    public const uint SkinTone = 55867754u;
    public const uint Data = 1415235194u;
    public const uint CasPreset = 2635774068u;
    public const uint SliderModifier = 3321263678u;
    public const uint ObjectCatalog = 832458525u;
    public const uint ObjectDefinition = 3235601127u;
    public const uint TextureCompositor = 3066607264u;
    public const uint ImageResource = 877907861u;
    public const uint MaterialDefinition = 734023391u;

    public const uint AspirationGroup = 2161773u;
    public const uint SimTraitGroup = 6282508u;
    public const uint LotTraitGroup = 1935269u;

    public static readonly uint[] Supported =
    [
        CasPart,
        SkinTone,
        Data,
        CasPreset,
        SliderModifier,
        ObjectCatalog,
        ObjectDefinition
    ];

    public static readonly uint[] SkinToneOverlayTypes =
    [
        TextureCompositor,
        ImageResource
    ];

    public static readonly uint[] SkinToneMaterialTypes =
    [
        ImageResource,
        MaterialDefinition
    ];

    public static readonly uint[] SkinToneBumpMapTypes =
    [
        ImageResource
    ];

    public static readonly uint[] StructuredReferenceTypes =
    [
        CasPart,
        SkinTone,
        Data,
        CasPreset,
        SliderModifier,
        ObjectCatalog,
        ObjectDefinition,
        TextureCompositor,
        ImageResource,
        MaterialDefinition
    ];

    public static bool IsSupported(uint type)
    {
        for (var i = 0; i < Supported.Length; i++)
        {
            if (Supported[i] == type)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsStructuredReferenceType(uint type)
    {
        for (var i = 0; i < StructuredReferenceTypes.Length; i++)
        {
            if (StructuredReferenceTypes[i] == type)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class ResourceDependencyExtraction
{
    public HashSet<TrayResourceKey> ExactKeys { get; } = [];

    public List<TypedInstanceDependency> TypedInstances { get; } = [];

    public HashSet<ulong> FallbackIds { get; } = [];

    public bool HasAny =>
        ExactKeys.Count > 0 ||
        TypedInstances.Count > 0 ||
        FallbackIds.Count > 0;
}

internal readonly record struct TypedInstanceDependency(ulong Instance, uint[] AllowedTypes);

internal static class StructuredDependencyReaders
{
    public static ResourceDependencyExtraction Read(TrayResourceKey key, ReadOnlySpan<byte> data)
    {
        var extraction = new ResourceDependencyExtraction();
        switch (key.Type)
        {
            case KnownResourceTypes.CasPart:
                CasPartDependencyReader.Read(data, extraction);
                break;
            case KnownResourceTypes.SkinTone:
                SkinToneDependencyReader.Read(data, extraction);
                break;
            case KnownResourceTypes.ObjectDefinition:
            case KnownResourceTypes.ObjectCatalog:
                ObjectDefinitionDependencyReader.Read(data, extraction);
                break;
            default:
                break;
        }

        return extraction;
    }
}

internal static class CasPartDependencyReader
{
    public static void Read(ReadOnlySpan<byte> data, ResourceDependencyExtraction extraction)
    {
        StructuredDependencyReaderUtilities.ExtractStructuredKeys(data, extraction.ExactKeys);
    }
}

internal static class SkinToneDependencyReader
{
    public static void Read(ReadOnlySpan<byte> data, ResourceDependencyExtraction extraction)
    {
        var seen = new HashSet<ulong>();
        StructuredDependencyReaderUtilities.CollectAlignedIds(data, seen);
        foreach (var id in seen)
        {
            extraction.TypedInstances.Add(new TypedInstanceDependency(id, KnownResourceTypes.SkinToneOverlayTypes));
            extraction.TypedInstances.Add(new TypedInstanceDependency(id, KnownResourceTypes.SkinToneMaterialTypes));
            extraction.TypedInstances.Add(new TypedInstanceDependency(id, KnownResourceTypes.SkinToneBumpMapTypes));
        }
    }
}

internal static class ObjectDefinitionDependencyReader
{
    public static void Read(ReadOnlySpan<byte> data, ResourceDependencyExtraction extraction)
    {
        StructuredDependencyReaderUtilities.ExtractStructuredKeys(data, extraction.ExactKeys);
    }
}

internal static class StructuredDependencyReaderUtilities
{
    public static void CollectAlignedIds(ReadOnlySpan<byte> data, HashSet<ulong> ids)
    {
        for (var offset = 0; offset <= data.Length - 8; offset += 8)
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
            if (!BinaryReferenceScanner.LooksLikeResourceId(value))
            {
                continue;
            }

            ids.Add(value);
        }
    }

    public static void ExtractStructuredKeys(ReadOnlySpan<byte> data, HashSet<TrayResourceKey> keys)
    {
        for (var offset = 0; offset <= data.Length - 16; offset += 4)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            if (!KnownResourceTypes.IsStructuredReferenceType(type))
            {
                continue;
            }

            var group = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            var instance = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 8, 8));
            if (!BinaryReferenceScanner.LooksLikeResourceId(instance))
            {
                continue;
            }

            keys.Add(new TrayResourceKey(type, group, instance));
        }
    }
}

internal sealed class DirectMatchEngine
{
    public DirectMatchResult Match(TraySearchKeys keys, ITrayDependencyLookupSession lookup)
    {
        var issues = new List<TrayDependencyIssue>();
        var results = new Dictionary<TrayResourceKey, ResolvedResourceRef>();
        var candidateInstances = GetCandidateInstances(keys);
        var matchedInstances = new HashSet<ulong>();
        var unmatchedExactKeys = new HashSet<TrayResourceKey>(keys.ResourceKeys);

        foreach (var resourceKey in keys.ResourceKeys)
        {
            if (!lookup.TryGetExact(resourceKey, out var matches) || matches.Length == 0)
            {
                continue;
            }

            Register(results, matches[0]);
            matchedInstances.Add(resourceKey.Instance);
            unmatchedExactKeys.Remove(resourceKey);
        }

        foreach (var instance in candidateInstances)
        {
            foreach (var supportedType in KnownResourceTypes.Supported)
            {
                if (!lookup.TryGetTypeInstance(new TypeInstanceKey(supportedType, instance), out var candidates))
                {
                    continue;
                }

                var chosen = ChooseEntry(candidates, supportedType);
                if (chosen is null)
                {
                    continue;
                }

                Register(results, chosen);
                matchedInstances.Add(instance);
                break;
            }
        }

        if (candidateInstances.Count > 0 && matchedInstances.Count == 0 && keys.ResourceKeys.Length == 0)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Warning,
                Kind = TrayDependencyIssueKind.MissingReference,
                Message = "No matching mod files were found for extracted tray references."
            });
        }
        else if (candidateInstances.Count > matchedInstances.Count)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Warning,
                Kind = TrayDependencyIssueKind.MissingReference,
                Message = $"Some extracted references were not matched ({candidateInstances.Count - matchedInstances.Count})."
            });
        }

        if (unmatchedExactKeys.Count > 0)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Warning,
                Kind = TrayDependencyIssueKind.MissingReference,
                Message = $"Some exact resource keys were not matched ({unmatchedExactKeys.Count})."
            });
        }

        return new DirectMatchResult(results.Values.OrderBy(match => match.FilePath, StringComparer.OrdinalIgnoreCase).ToArray(), issues.ToArray());
    }

    private static HashSet<ulong> GetCandidateInstances(TraySearchKeys keys)
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
            .ToHashSet();
    }

    private static ResolvedResourceRef? ChooseEntry(ResolvedResourceRef[] candidates, uint supportedType)
    {
        ResolvedResourceRef? chosen = null;
        string? chosenPath = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Entry is null || candidate.Entry.IsDeleted)
            {
                continue;
            }

            var entry = candidate.Entry;
            if (supportedType == KnownResourceTypes.Data &&
                entry.Group != KnownResourceTypes.AspirationGroup &&
                entry.Group != KnownResourceTypes.SimTraitGroup &&
                entry.Group != KnownResourceTypes.LotTraitGroup)
            {
                continue;
            }

            if (chosenPath is null)
            {
                chosenPath = candidate.FilePath;
                chosen = candidate;
                continue;
            }

            if (!string.Equals(chosenPath, candidate.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (chosen is null || entry.Group < chosen.Entry!.Group)
            {
                chosen = candidate;
            }
        }

        return chosen;
    }

    private static void Register(Dictionary<TrayResourceKey, ResolvedResourceRef> results, ResolvedResourceRef item)
    {
        if (results.TryGetValue(item.Key, out var existing) &&
            string.Compare(item.FilePath, existing.FilePath, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return;
        }

        results[item.Key] = item;
    }
}

internal sealed record DirectMatchResult(
    IReadOnlyList<ResolvedResourceRef> DirectMatches,
    IReadOnlyList<TrayDependencyIssue> Issues);

internal sealed class DependencyExpandEngine
{
    private readonly IDbpfResourceReader _resourceReader;
    private readonly ILogger _logger;

    public DependencyExpandEngine(IDbpfResourceReader resourceReader, ILogger? logger = null)
    {
        _resourceReader = resourceReader;
        _logger = logger ?? NullLogger.Instance;
    }

    public DependencyExpandResult Expand(
        DirectMatchResult directMatch,
        ITrayDependencyLookupSession lookup,
        CancellationToken cancellationToken)
    {
        var issues = new List<TrayDependencyIssue>();
        var results = new Dictionary<TrayResourceKey, ResolvedResourceRef>();
        var sessions = new Dictionary<string, DbpfPackageReadSession>(StringComparer.OrdinalIgnoreCase);
        var payload = new ArrayBufferWriter<byte>();
        var siblingPayload = new ArrayBufferWriter<byte>();
        var pooledReadAttempts = 0;
        var pooledReadSuccess = 0;
        var pooledReadBytes = 0L;
        var sessionsOpened = 0;
        var sessionReuseHits = 0;

        try
        {
            foreach (var match in directMatch.DirectMatches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (match.Entry is null)
                {
                    continue;
                }

                pooledReadAttempts++;
                if (!TryReadPayload(match, sessions, payload, out var bytesRead, out var openedSession, out var readError))
                {
                    if (!string.IsNullOrWhiteSpace(readError))
                    {
                        issues.Add(new TrayDependencyIssue
                        {
                            Severity = TrayDependencyIssueSeverity.Warning,
                            Kind = TrayDependencyIssueKind.PackageParseError,
                            FilePath = match.FilePath,
                            ResourceKey = FormatKey(match.Key),
                            Message = readError
                        });
                    }

                    continue;
                }

                pooledReadSuccess++;
                pooledReadBytes += bytesRead;
                if (openedSession)
                {
                    sessionsOpened++;
                }
                else
                {
                    sessionReuseHits++;
                }

                var extraction = StructuredDependencyReaders.Read(match.Key, payload.WrittenSpan);

                if (match.Key.Type == KnownResourceTypes.ObjectCatalog &&
                    TryResolveExact(
                        lookup,
                        new TrayResourceKey(KnownResourceTypes.ObjectDefinition, match.Key.Group, match.Key.Instance),
                        out var siblingObjectDefinition))
                {
                    Register(results, siblingObjectDefinition with { Parent = match });

                    pooledReadAttempts++;
                    if (TryReadPayload(siblingObjectDefinition, sessions, siblingPayload, out var siblingBytes, out var siblingOpenedSession, out _))
                    {
                        pooledReadSuccess++;
                        pooledReadBytes += siblingBytes;
                        if (siblingOpenedSession)
                        {
                            sessionsOpened++;
                        }
                        else
                        {
                            sessionReuseHits++;
                        }
                        ObjectDefinitionDependencyReader.Read(siblingPayload.WrittenSpan, extraction);
                    }
                }

                if (!extraction.HasAny)
                {
                    BinaryReferenceScanner.Scan(payload.WrittenSpan, extraction.FallbackIds, extraction.ExactKeys);
                }

                foreach (var exactKey in extraction.ExactKeys)
                {
                    if (TryResolveExact(lookup, exactKey, out var resolved))
                    {
                        Register(results, resolved with { Parent = match });
                    }
                }

                foreach (var typedInstance in extraction.TypedInstances)
                {
                    if (TryResolveByTypes(lookup, typedInstance.Instance, typedInstance.AllowedTypes, out var resolved))
                    {
                        Register(results, resolved with { Parent = match });
                    }
                }

                foreach (var id in extraction.FallbackIds)
                {
                    if (!TryResolveAny(lookup, id, out var resolved))
                    {
                        continue;
                    }

                    Register(results, resolved with { Parent = match });
                }
            }
        }
        finally
        {
            foreach (var session in sessions.Values)
            {
                session.Dispose();
            }
        }

        _logger.LogDebug(
            "resource.read.pooled domain={Domain} readAttempts={ReadAttempts} readSuccess={ReadSuccess} readBytes={ReadBytes} sessionsOpened={SessionsOpened} sessionReuseHits={SessionReuseHits} directMatches={DirectMatches} expandedMatches={ExpandedMatches}",
            "traydependency",
            pooledReadAttempts,
            pooledReadSuccess,
            pooledReadBytes,
            sessionsOpened,
            sessionReuseHits,
            directMatch.DirectMatches.Count,
            results.Count);

        return new DependencyExpandResult(results.Values.OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase).ToArray(), issues.ToArray());
    }

    private static bool TryResolveExact(ITrayDependencyLookupSession lookup, TrayResourceKey key, out ResolvedResourceRef resolved)
    {
        if (lookup.TryGetExact(key, out var matches) && matches.Length > 0)
        {
            resolved = matches[0];
            return true;
        }

        resolved = null!;
        return false;
    }

    private static bool TryResolveByTypes(
        ITrayDependencyLookupSession lookup,
        ulong instance,
        IReadOnlyList<uint> allowedTypes,
        out ResolvedResourceRef resolved)
    {
        for (var typeIndex = 0; typeIndex < allowedTypes.Count; typeIndex++)
        {
            if (!lookup.TryGetTypeInstance(new TypeInstanceKey(allowedTypes[typeIndex], instance), out var matches))
            {
                continue;
            }

            if (TryChooseFirst(matches, out resolved))
            {
                return true;
            }
        }

        resolved = null!;
        return false;
    }

    private static bool TryResolveAny(ITrayDependencyLookupSession lookup, ulong instance, out ResolvedResourceRef resolved)
    {
        if (!lookup.HasSupportedInstance(instance))
        {
            resolved = null!;
            return false;
        }

        foreach (var supportedType in KnownResourceTypes.Supported)
        {
            if (!lookup.TryGetTypeInstance(new TypeInstanceKey(supportedType, instance), out var matches))
            {
                continue;
            }

            if (TryChooseFirst(matches, out resolved))
            {
                return true;
            }
        }

        resolved = null!;
        return false;
    }

    private bool TryReadPayload(
        ResolvedResourceRef resource,
        IDictionary<string, DbpfPackageReadSession> sessions,
        ArrayBufferWriter<byte> payload,
        out int bytesRead,
        out bool openedSession,
        out string? error)
    {
        payload.Clear();
        bytesRead = 0;
        openedSession = false;
        error = null;

        if (resource.Entry is null)
        {
            error = "Missing package entry.";
            return false;
        }

        if (!sessions.TryGetValue(resource.FilePath, out var session))
        {
            session = _resourceReader.OpenSession(resource.FilePath);
            sessions[resource.FilePath] = session;
            openedSession = true;
        }

        if (!session.TryReadInto(
            new DbpfIndexEntry(
                resource.Entry.Type,
                resource.Entry.Group,
                resource.Entry.Instance,
                resource.Entry.DataOffset,
                resource.Entry.CompressedSize,
                resource.Entry.UncompressedSize,
                resource.Entry.CompressionType,
                resource.Entry.IsDeleted),
            payload,
            out error))
        {
            return false;
        }

        bytesRead = payload.WrittenCount;
        return true;
    }

    private static bool TryChooseFirst(ResolvedResourceRef[] matches, out ResolvedResourceRef resolved)
    {
        for (var i = 0; i < matches.Length; i++)
        {
            var candidate = matches[i];
            if (candidate.Entry is null || candidate.Entry.IsDeleted)
            {
                continue;
            }

            resolved = candidate;
            return true;
        }

        resolved = null!;
        return false;
    }

    private static void Register(Dictionary<TrayResourceKey, ResolvedResourceRef> results, ResolvedResourceRef item)
    {
        if (results.TryGetValue(item.Key, out var existing) &&
            string.Compare(existing.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase) <= 0)
        {
            return;
        }

        results[item.Key] = item;
    }

    private static string FormatKey(TrayResourceKey key)
    {
        return $"0x{key.Type:x8}:0x{key.Group:x8}:0x{key.Instance:x16}";
    }
}

internal sealed record DependencyExpandResult(
    IReadOnlyList<ResolvedResourceRef> ExpandedMatches,
    IReadOnlyList<TrayDependencyIssue> Issues);

internal sealed class ModFileExporter
{
    public void CopyFiles(
        IReadOnlyList<string> sourceFiles,
        string targetRoot,
        List<TrayDependencyIssue> issues,
        IProgress<TrayDependencyExportProgress>? progress,
        out int copiedFileCount)
    {
        copiedFileCount = 0;
        Directory.CreateDirectory(targetRoot);

        for (var index = 0; index < sourceFiles.Count; index++)
        {
            var sourcePath = sourceFiles[index];
            try
            {
                if (!File.Exists(sourcePath))
                {
                    issues.Add(new TrayDependencyIssue
                    {
                        Severity = TrayDependencyIssueSeverity.Warning,
                        Kind = TrayDependencyIssueKind.MissingSourceFile,
                        FilePath = sourcePath,
                        Message = "Source mod file no longer exists."
                    });
                    continue;
                }

                var targetPath = Path.Combine(targetRoot, Path.GetFileName(sourcePath));
                targetPath = FileNameHelpers.GetUniquePath(targetPath);
                File.Copy(sourcePath, targetPath, overwrite: false);
                copiedFileCount++;
                progress?.Report(new TrayDependencyExportProgress
                {
                    Stage = TrayDependencyExportStage.CopyingMods,
                    Percent = ProgressScale.Scale(85, 99, copiedFileCount, sourceFiles.Count),
                    Detail = $"Copying referenced mods... {copiedFileCount}/{sourceFiles.Count}"
                });
            }
            catch (Exception ex)
            {
                issues.Add(new TrayDependencyIssue
                {
                    Severity = TrayDependencyIssueSeverity.Error,
                    Kind = TrayDependencyIssueKind.CopyError,
                    FilePath = sourcePath,
                    Message = $"Failed to copy mod file: {ex.Message}"
                });
                return;
            }
        }
    }
}

internal static class FileNameHelpers
{
    public static string GetUniquePath(string targetPath)
    {
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            return targetPath;
        }

        var directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(targetPath);
        var extension = Path.GetExtension(targetPath);

        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            var candidate = Path.Combine(directory, $"{fileNameWithoutExtension} ({suffix}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Unable to generate a unique export filename.");
    }
}
