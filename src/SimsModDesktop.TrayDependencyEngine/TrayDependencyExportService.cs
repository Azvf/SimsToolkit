using System.Buffers.Binary;
using System.IO.Compression;

namespace SimsModDesktop.TrayDependencyEngine;

public sealed class TrayDependencyExportService : ITrayDependencyExportService
{
    private readonly IPackageIndexCache _packageIndexCache;
    private readonly TrayBundleLoader _bundleLoader = new();
    private readonly TraySearchExtractor _searchExtractor = new();
    private readonly DirectMatchEngine _directMatchEngine = new();
    private readonly DependencyExpandEngine _dependencyExpandEngine = new();
    private readonly ModFileExporter _fileExporter = new();

    public TrayDependencyExportService(IPackageIndexCache packageIndexCache)
    {
        _packageIndexCache = packageIndexCache;
    }

    public Task<TrayDependencyExportResult> ExportAsync(
        TrayDependencyExportRequest request,
        IProgress<TrayDependencyExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(async () =>
        {
            var issues = new List<TrayDependencyIssue>();
            Report(progress, TrayDependencyExportStage.Preparing, 0, "Copying tray files...");

            Directory.CreateDirectory(request.TrayExportRoot);
            Directory.CreateDirectory(request.ModsExportRoot);

            if (!TryCopyTrayFiles(request.TraySourceFiles, request.TrayExportRoot, issues, out var copiedTrayFileCount))
            {
                return BuildResult(copiedTrayFileCount, 0, issues);
            }

            var snapshot = await _packageIndexCache.GetSnapshotAsync(request.ModsRootPath, progress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyExportStage.ParsingTray, 35, "Parsing tray files...");

            if (!_bundleLoader.TryLoad(request.TraySourceFiles, issues, out var bundle))
            {
                return BuildResult(copiedTrayFileCount, 0, issues);
            }

            var searchKeys = _searchExtractor.Extract(bundle, issues);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyExportStage.MatchingDirectReferences, 45, "Matching direct references...");

            var directMatch = _directMatchEngine.Match(searchKeys, snapshot);
            issues.AddRange(directMatch.Issues);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyExportStage.ExpandingDependencies, 65, "Expanding second-level references...");

            var expansion = _dependencyExpandEngine.Expand(
                directMatch,
                snapshot,
                cancellationToken);
            issues.AddRange(expansion.Issues);

            var filePaths = directMatch.DirectMatches
                .Concat(expansion.ExpandedMatches)
                .Select(match => match.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyExportStage.CopyingMods, 85, $"Copying referenced mods ({filePaths.Length})...");

            _fileExporter.CopyFiles(filePaths, request.ModsExportRoot, issues, out var copiedModFileCount);

            Report(progress, TrayDependencyExportStage.Completed, 100, "Completed.");
            return BuildResult(copiedTrayFileCount, copiedModFileCount, issues);
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
        List<TrayDependencyIssue> issues)
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
            Issues = issues.ToArray()
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

public sealed class PackageIndexCache : IPackageIndexCache
{
    private readonly object _sync = new();
    private readonly Dictionary<string, CacheState> _states = new(StringComparer.OrdinalIgnoreCase);

    public Task<PackageIndexSnapshot> GetSnapshotAsync(
        string modsRootPath,
        IProgress<TrayDependencyExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modsRootPath))
        {
            throw new ArgumentException("Mods root path is required.", nameof(modsRootPath));
        }

        var normalizedRoot = Path.GetFullPath(modsRootPath.Trim());
        var files = Directory.Exists(normalizedRoot)
            ? Directory.EnumerateFiles(normalizedRoot, "*.package", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

        CacheState? existingState;
        lock (_sync)
        {
            _states.TryGetValue(normalizedRoot, out existingState);
        }

        var nextEntries = new Dictionary<string, CachedPackage>(files.Length, StringComparer.OrdinalIgnoreCase);
        var hasChanges = existingState is null || existingState.CachedPackages.Count != files.Length;

        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = files[index];
            var fileInfo = new FileInfo(path);
            CachedPackage? existingPackage = null;
            if (existingState is not null &&
                existingState.CachedPackages.TryGetValue(path, out var cached) &&
                cached.Length == fileInfo.Length &&
                cached.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc)
            {
                existingPackage = cached;
            }
            else
            {
                hasChanges = true;
            }

            nextEntries[path] = existingPackage ?? new CachedPackage(
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc,
                DbpfPackageReader.ReadPackage(path));

            progress?.Report(new TrayDependencyExportProgress
            {
                Stage = TrayDependencyExportStage.IndexingPackages,
                Percent = ProgressScale.Scale(5, 35, index + 1, Math.Max(files.Length, 1)),
                Detail = $"Indexing packages... {index + 1}/{files.Length}"
            });
        }

        if (existingState is not null)
        {
            foreach (var cachedPath in existingState.CachedPackages.Keys)
            {
                if (!nextEntries.ContainsKey(cachedPath))
                {
                    hasChanges = true;
                    break;
                }
            }
        }

        CacheState nextState;
        if (!hasChanges && existingState is not null)
        {
            nextState = existingState;
        }
        else
        {
            var snapshot = new PackageIndexSnapshot
            {
                ModsRootPath = normalizedRoot,
                Packages = nextEntries
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => pair.Value.Package)
                    .ToArray()
            };
            nextState = new CacheState(snapshot, nextEntries);
            lock (_sync)
            {
                _states[normalizedRoot] = nextState;
            }
        }

        if (files.Length == 0)
        {
            progress?.Report(new TrayDependencyExportProgress
            {
                Stage = TrayDependencyExportStage.IndexingPackages,
                Percent = 35,
                Detail = "Indexing packages... 0/0"
            });
        }

        return Task.FromResult(nextState.Snapshot);
    }

    private sealed record CacheState(
        PackageIndexSnapshot Snapshot,
        Dictionary<string, CachedPackage> CachedPackages);

    private sealed record CachedPackage(
        long Length,
        DateTime LastWriteTimeUtc,
        IndexedPackageFile Package);
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
    public bool TryLoad(
        IReadOnlyList<string> sourceFilePaths,
        List<TrayDependencyIssue> issues,
        out TrayFileBundle bundle)
    {
        var trayItems = sourceFilePaths
            .Where(path => string.Equals(Path.GetExtension(path), ".trayitem", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (trayItems.Length != 1)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Error,
                Kind = TrayDependencyIssueKind.TrayParseError,
                Message = ".trayitem file is missing or duplicated."
            });
            bundle = new TrayFileBundle();
            return false;
        }

        bundle = new TrayFileBundle
        {
            TrayItemPath = trayItems[0],
            HhiPath = sourceFilePaths.FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".hhi", StringComparison.OrdinalIgnoreCase)),
            SgiPaths = sourceFilePaths
                .Where(path => string.Equals(Path.GetExtension(path), ".sgi", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            HouseholdBinaryPath = sourceFilePaths.FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".householdbinary", StringComparison.OrdinalIgnoreCase)),
            BlueprintPath = sourceFilePaths.FirstOrDefault(path =>
                string.Equals(Path.GetExtension(path), ".blueprint", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(path), ".bpi", StringComparison.OrdinalIgnoreCase)),
            RoomPath = sourceFilePaths.FirstOrDefault(path =>
                string.Equals(Path.GetExtension(path), ".room", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(path), ".rmi", StringComparison.OrdinalIgnoreCase))
        };
        return true;
    }
}

internal sealed class TraySearchExtractor
{
    public TraySearchKeys Extract(TrayFileBundle bundle, List<TrayDependencyIssue> issues)
    {
        var householdIds = new HashSet<ulong>();
        var buildIds = new HashSet<ulong>();
        var resourceKeys = new HashSet<TrayResourceKey>();

        ScanFile(bundle.TrayItemPath, issues, householdIds, resourceKeys);
        ScanFile(bundle.HhiPath, issues, householdIds, resourceKeys);

        foreach (var sgiPath in bundle.SgiPaths)
        {
            ScanFile(sgiPath, issues, householdIds, resourceKeys);
        }

        ScanFile(bundle.HouseholdBinaryPath, issues, householdIds, resourceKeys);
        ScanFile(bundle.BlueprintPath, issues, buildIds, resourceKeys);
        ScanFile(bundle.RoomPath, issues, buildIds, resourceKeys);

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
    public DirectMatchResult Match(TraySearchKeys keys, PackageIndexSnapshot snapshot)
    {
        var issues = new List<TrayDependencyIssue>();
        var results = new Dictionary<TrayResourceKey, ResolvedResourceRef>();
        var candidateInstances = GetCandidateInstances(keys);
        var matchedInstances = new HashSet<ulong>();
        var unmatchedExactKeys = new HashSet<TrayResourceKey>(keys.ResourceKeys);

        foreach (var package in snapshot.Packages)
        {
            foreach (var resourceKey in keys.ResourceKeys)
            {
                if (!package.TypeIndexes.TryGetValue(resourceKey.Type, out var typeIndex) ||
                    !typeIndex.InstanceToEntryIndexes.TryGetValue(resourceKey.Instance, out var entryIndexes))
                {
                    continue;
                }

                foreach (var entryIndex in entryIndexes)
                {
                    var entry = package.Entries[entryIndex];
                    if (entry.Group != resourceKey.Group || entry.IsDeleted)
                    {
                        continue;
                    }

                    Register(results, package.FilePath, entry, null);
                    matchedInstances.Add(resourceKey.Instance);
                    unmatchedExactKeys.Remove(resourceKey);
                    break;
                }
            }

            foreach (var supportedType in KnownResourceTypes.Supported)
            {
                if (!package.TypeIndexes.TryGetValue(supportedType, out var typeIndex))
                {
                    continue;
                }

                foreach (var pair in typeIndex.InstanceToEntryIndexes)
                {
                    if (!candidateInstances.Contains(pair.Key))
                    {
                        continue;
                    }

                    var chosen = ChooseEntry(package, pair.Value, supportedType);
                    if (chosen is null)
                    {
                        continue;
                    }

                    Register(results, package.FilePath, chosen, null);
                    matchedInstances.Add(pair.Key);
                }
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

    private static PackageIndexEntry? ChooseEntry(IndexedPackageFile package, int[] entryIndexes, uint supportedType)
    {
        PackageIndexEntry? chosen = null;
        foreach (var entryIndex in entryIndexes)
        {
            var entry = package.Entries[entryIndex];
            if (entry.IsDeleted)
            {
                continue;
            }

            if (supportedType == KnownResourceTypes.Data &&
                entry.Group != KnownResourceTypes.AspirationGroup &&
                entry.Group != KnownResourceTypes.SimTraitGroup &&
                entry.Group != KnownResourceTypes.LotTraitGroup)
            {
                continue;
            }

            if (chosen is null || entry.Group < chosen.Group)
            {
                chosen = entry;
            }
        }

        return chosen;
    }

    private static void Register(
        Dictionary<TrayResourceKey, ResolvedResourceRef> results,
        string filePath,
        PackageIndexEntry entry,
        ResolvedResourceRef? parent)
    {
        var key = new TrayResourceKey(entry.Type, entry.Group, entry.Instance);
        if (results.TryGetValue(key, out var existing) &&
            string.Compare(filePath, existing.FilePath, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return;
        }

        results[key] = new ResolvedResourceRef
        {
            Key = key,
            FilePath = filePath,
            Entry = entry,
            Parent = parent
        };
    }
}

internal sealed record DirectMatchResult(
    IReadOnlyList<ResolvedResourceRef> DirectMatches,
    IReadOnlyList<TrayDependencyIssue> Issues);

internal sealed class DependencyExpandEngine
{
    public DependencyExpandResult Expand(
        DirectMatchResult directMatch,
        PackageIndexSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var issues = new List<TrayDependencyIssue>();
        var results = new Dictionary<TrayResourceKey, ResolvedResourceRef>();

        foreach (var match in directMatch.DirectMatches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (match.Entry is null)
            {
                continue;
            }

            if (!DbpfPackageReader.TryReadResourceBytes(match.FilePath, match.Entry, out var bytes, out var readError))
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

            var extraction = StructuredDependencyReaders.Read(match.Key, bytes);

            if (match.Key.Type == KnownResourceTypes.ObjectCatalog &&
                TryResolveExact(
                    snapshot,
                    new TrayResourceKey(KnownResourceTypes.ObjectDefinition, match.Key.Group, match.Key.Instance),
                    out var siblingObjectDefinition))
            {
                Register(results, siblingObjectDefinition with { Parent = match });

                if (siblingObjectDefinition.Entry is not null &&
                    DbpfPackageReader.TryReadResourceBytes(
                        siblingObjectDefinition.FilePath,
                        siblingObjectDefinition.Entry,
                        out var objectDefinitionBytes,
                        out _))
                {
                    ObjectDefinitionDependencyReader.Read(objectDefinitionBytes, extraction);
                }
            }

            if (!extraction.HasAny)
            {
                BinaryReferenceScanner.Scan(bytes, extraction.FallbackIds, extraction.ExactKeys);
            }

            foreach (var exactKey in extraction.ExactKeys)
            {
                if (TryResolveExact(snapshot, exactKey, out var resolved))
                {
                    Register(results, resolved with { Parent = match });
                }
            }

            foreach (var typedInstance in extraction.TypedInstances)
            {
                if (TryResolveByTypes(snapshot, typedInstance.Instance, typedInstance.AllowedTypes, out var resolved))
                {
                    Register(results, resolved with { Parent = match });
                }
            }

            foreach (var id in extraction.FallbackIds)
            {
                if (!TryResolveAny(snapshot, id, out var resolved))
                {
                    continue;
                }

                Register(results, resolved with { Parent = match });
            }
        }

        return new DependencyExpandResult(results.Values.OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase).ToArray(), issues.ToArray());
    }

    private static bool TryResolveExact(PackageIndexSnapshot snapshot, TrayResourceKey key, out ResolvedResourceRef resolved)
    {
        foreach (var package in snapshot.Packages.OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            if (!package.TypeIndexes.TryGetValue(key.Type, out var typeIndex) ||
                !typeIndex.InstanceToEntryIndexes.TryGetValue(key.Instance, out var indexes))
            {
                continue;
            }

            foreach (var index in indexes)
            {
                var entry = package.Entries[index];
                if (entry.Group != key.Group || entry.IsDeleted)
                {
                    continue;
                }

                resolved = new ResolvedResourceRef
                {
                    Key = key,
                    FilePath = package.FilePath,
                    Entry = entry
                };
                return true;
            }
        }

        resolved = null!;
        return false;
    }

    private static bool TryResolveByTypes(
        PackageIndexSnapshot snapshot,
        ulong instance,
        IReadOnlyList<uint> allowedTypes,
        out ResolvedResourceRef resolved)
    {
        foreach (var package in snapshot.Packages.OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            for (var typeIndex = 0; typeIndex < allowedTypes.Count; typeIndex++)
            {
                var type = allowedTypes[typeIndex];
                if (!package.TypeIndexes.TryGetValue(type, out var typedIndex) ||
                    !typedIndex.InstanceToEntryIndexes.TryGetValue(instance, out var indexes))
                {
                    continue;
                }

                foreach (var index in indexes)
                {
                    var entry = package.Entries[index];
                    if (entry.IsDeleted)
                    {
                        continue;
                    }

                    resolved = new ResolvedResourceRef
                    {
                        Key = new TrayResourceKey(entry.Type, entry.Group, entry.Instance),
                        FilePath = package.FilePath,
                        Entry = entry
                    };
                    return true;
                }
            }
        }

        resolved = null!;
        return false;
    }

    private static bool TryResolveAny(PackageIndexSnapshot snapshot, ulong instance, out ResolvedResourceRef resolved)
    {
        foreach (var package in snapshot.Packages.OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var supportedType in KnownResourceTypes.Supported)
            {
                if (!package.TypeIndexes.TryGetValue(supportedType, out var typeIndex) ||
                    !typeIndex.InstanceToEntryIndexes.TryGetValue(instance, out var indexes))
                {
                    continue;
                }

                foreach (var index in indexes)
                {
                    var entry = package.Entries[index];
                    if (entry.IsDeleted)
                    {
                        continue;
                    }

                    resolved = new ResolvedResourceRef
                    {
                        Key = new TrayResourceKey(entry.Type, entry.Group, entry.Instance),
                        FilePath = package.FilePath,
                        Entry = entry
                    };
                    return true;
                }
            }
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
        out int copiedFileCount)
    {
        copiedFileCount = 0;
        Directory.CreateDirectory(targetRoot);

        foreach (var sourcePath in sourceFiles)
        {
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

internal static class DbpfPackageReader
{
    private const uint DbpfSignature = 1179664964u;
    private const ushort CompressionDeleted = 65504;
    private const ushort CompressionZlib = 23106;

    public static IndexedPackageFile ReadPackage(string filePath)
    {
        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> header = stackalloc byte[96];
        FillExactly(stream, header);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != DbpfSignature)
        {
            throw new InvalidDataException("Not a DBPF package file.");
        }

        var entryCount = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(36, 4));
        var indexPositionLow = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(40, 4));
        var indexRecordSize = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(44, 4));
        var indexPosition = BinaryPrimitives.ReadUInt64LittleEndian(header.Slice(64, 8));
        if (entryCount == 0)
        {
            var emptyFileInfo = new FileInfo(filePath);
            return new IndexedPackageFile
            {
                FilePath = filePath,
                Length = emptyFileInfo.Length,
                LastWriteTimeUtc = emptyFileInfo.LastWriteTimeUtc,
                Entries = Array.Empty<PackageIndexEntry>(),
                TypeIndexes = new Dictionary<uint, PackageTypeIndex>()
            };
        }

        var finalIndexPosition = indexPosition != 0 ? (long)indexPosition : indexPositionLow;

        stream.Seek(finalIndexPosition, SeekOrigin.Begin);
        Span<byte> flagsBytes = stackalloc byte[4];
        FillExactly(stream, flagsBytes);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(flagsBytes);
        var constantType = (flags & 0x1) != 0;
        var constantGroup = (flags & 0x2) != 0;
        var constantInstanceEx = (flags & 0x4) != 0;

        var template = new byte[32];
        var constantWords = 0;
        if (constantType)
        {
            FillExactly(stream, template.AsSpan(0, 4));
            constantWords++;
        }

        if (constantGroup)
        {
            FillExactly(stream, template.AsSpan(4, 4));
            constantWords++;
        }

        if (constantInstanceEx)
        {
            FillExactly(stream, template.AsSpan(8, 4));
            constantWords++;
        }

        var overheadBytes = 4 + constantWords * 4;
        var entryCountInt = checked((int)entryCount);
        var variableBytesPerEntry = 32 - (constantWords * 4);
        var totalVariableBytes = ResolveIndexVariableByteCount(
            filePath,
            stream,
            finalIndexPosition,
            indexRecordSize,
            entryCountInt,
            overheadBytes,
            variableBytesPerEntry);

        if (variableBytesPerEntry < 20 || variableBytesPerEntry > 32 || totalVariableBytes <= 0)
        {
            throw new InvalidDataException("Unsupported DBPF index record size.");
        }

        var entries = new PackageIndexEntry[entryCount];
        var indexBlock = new byte[totalVariableBytes];
        FillExactly(stream, indexBlock);
        for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
        {
            var variableBuffer = new byte[variableBytesPerEntry];
            Buffer.BlockCopy(indexBlock, entryIndex * variableBytesPerEntry, variableBuffer, 0, variableBytesPerEntry);
            var fullEntry = new byte[32];
            Buffer.BlockCopy(template, 0, fullEntry, 0, template.Length);

            var cursor = 0;
            if (!constantType)
            {
                Buffer.BlockCopy(variableBuffer, cursor, fullEntry, 0, 4);
                cursor += 4;
            }

            if (!constantGroup)
            {
                Buffer.BlockCopy(variableBuffer, cursor, fullEntry, 4, 4);
                cursor += 4;
            }

            if (!constantInstanceEx)
            {
                Buffer.BlockCopy(variableBuffer, cursor, fullEntry, 8, 4);
                cursor += 4;
            }

            Buffer.BlockCopy(variableBuffer, cursor, fullEntry, 12, variableBytesPerEntry - cursor);

            var type = BinaryPrimitives.ReadUInt32LittleEndian(fullEntry.AsSpan(0, 4));
            var group = BinaryPrimitives.ReadUInt32LittleEndian(fullEntry.AsSpan(4, 4));
            var instanceEx = BinaryPrimitives.ReadUInt32LittleEndian(fullEntry.AsSpan(8, 4));
            var instanceLow = BinaryPrimitives.ReadUInt32LittleEndian(fullEntry.AsSpan(12, 4));
            var position = BinaryPrimitives.ReadUInt32LittleEndian(fullEntry.AsSpan(16, 4));
            var sizeAndCompression = BinaryPrimitives.ReadUInt32LittleEndian(fullEntry.AsSpan(20, 4));
            var sizeDecompressed = BinaryPrimitives.ReadUInt32LittleEndian(fullEntry.AsSpan(24, 4));
            var compressionType = BinaryPrimitives.ReadUInt16LittleEndian(fullEntry.AsSpan(28, 2));

            entries[entryIndex] = new PackageIndexEntry
            {
                Type = type,
                Group = group,
                Instance = ((ulong)instanceEx << 32) | instanceLow,
                IsDeleted = compressionType == CompressionDeleted,
                DataOffset = position,
                CompressedSize = unchecked((int)(sizeAndCompression & 0x7FFFFFFF)),
                UncompressedSize = unchecked((int)sizeDecompressed),
                CompressionType = compressionType
            };
        }

        var typeIndexes = entries
            .Select((entry, index) => (entry, index))
            .Where(item => !item.entry.IsDeleted)
            .GroupBy(item => item.entry.Type)
            .ToDictionary(
                group => group.Key,
                group => new PackageTypeIndex
                {
                    InstanceToEntryIndexes = group
                        .GroupBy(item => item.entry.Instance)
                        .ToDictionary(
                            instanceGroup => instanceGroup.Key,
                            instanceGroup => instanceGroup.Select(item => item.index).ToArray())
                });

        var fileInfo = new FileInfo(filePath);
        return new IndexedPackageFile
        {
            FilePath = filePath,
            Length = fileInfo.Length,
            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc,
            Entries = entries,
            TypeIndexes = typeIndexes
        };
    }

    private static int ResolveIndexVariableByteCount(
        string filePath,
        FileStream stream,
        long finalIndexPosition,
        uint indexRecordSize,
        int entryCount,
        int overheadBytes,
        int variableBytesPerEntry)
    {
        if (entryCount <= 0)
        {
            return 0;
        }

        var availableBytes = checked((int)Math.Min(int.MaxValue, stream.Length - finalIndexPosition - overheadBytes));
        var recordSizeInt = checked((int)indexRecordSize);

        if (recordSizeInt >= overheadBytes)
        {
            var candidateTotal = recordSizeInt - overheadBytes;
            if (candidateTotal > 0 &&
                candidateTotal % entryCount == 0 &&
                candidateTotal / entryCount == variableBytesPerEntry &&
                candidateTotal <= availableBytes)
            {
                return candidateTotal;
            }
        }

        // Some non-standard writers store the per-entry payload size in the header
        // instead of the total index block size. Accept it when it matches the
        // expected entry payload width derived from the flags.
        if (recordSizeInt == variableBytesPerEntry)
        {
            var candidateTotal = checked(variableBytesPerEntry * entryCount);
            if (candidateTotal <= availableBytes)
            {
                return candidateTotal;
            }
        }

        // Other writers keep the full 32-byte entry width in the header even when
        // the flags hoist fields into the shared template. Reduce it using the flag
        // mask and treat the result as a per-entry size.
        if (recordSizeInt == 32)
        {
            var candidatePerEntry = recordSizeInt - (overheadBytes - 4);
            if (candidatePerEntry == variableBytesPerEntry)
            {
                var candidateTotal = checked(candidatePerEntry * entryCount);
                if (candidateTotal <= availableBytes)
                {
                    return candidateTotal;
                }
            }
        }

        throw new InvalidDataException($"Unsupported DBPF index record size for '{filePath}'.");
    }

    public static bool TryReadResourceBytes(
        string packagePath,
        PackageIndexEntry entry,
        out byte[] bytes,
        out string? error)
    {
        bytes = Array.Empty<byte>();
        error = null;

        if (entry.IsDeleted)
        {
            error = "Deleted package entry.";
            return false;
        }

        try
        {
            using var stream = File.Open(packagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(entry.DataOffset, SeekOrigin.Begin);
            if (entry.CompressionType == 0)
            {
                bytes = new byte[entry.CompressedSize];
                FillExactly(stream, bytes);
                return true;
            }

            if (entry.CompressionType == CompressionZlib)
            {
                Span<byte> zlibHeader = stackalloc byte[2];
                FillExactly(stream, zlibHeader);
                if (zlibHeader[0] != 120)
                {
                    error = "Invalid ZLIB signature.";
                    return false;
                }

                using var deflate = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true);
                using var output = new MemoryStream(entry.UncompressedSize > 0 ? entry.UncompressedSize : 0);
                deflate.CopyTo(output);
                bytes = output.ToArray();
                return true;
            }

            error = $"Unsupported compression type: {entry.CompressionType}.";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Failed to read package resource: {ex.Message}";
            return false;
        }
    }

    private static void FillExactly(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer.Slice(total));
            if (read <= 0)
            {
                throw new EndOfStreamException("Unexpected end of stream.");
            }

            total += read;
        }
    }

    private static void FillExactly(Stream stream, byte[] buffer)
    {
        FillExactly(stream, buffer.AsSpan());
    }
}
