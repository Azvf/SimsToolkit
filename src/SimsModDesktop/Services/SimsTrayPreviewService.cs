using System.Globalization;
using System.Text.RegularExpressions;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class SimsTrayPreviewService : ISimsTrayPreviewService
{
    private static readonly Regex TrayIdentityRegex = new(
        "^0x([0-9a-fA-F]{1,8})!0x([0-9a-fA-F]{1,16})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BuildSizeRegex = new(
        @"(?<!\d)(\d{1,2})\s*[xX]\s*(\d{1,2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HouseholdSizeRegex = new(
        @"(?:(\d{1,2})\s*(?:sim|sims|member|members|人|口)|(?:household|family)\s*(\d{1,2}))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
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

    private const int MaxPreviewFileNames = 12;
    private const int MaxSupportedHouseholdMembers = 8;
    private const int MaxCachedSnapshots = 8;
    private readonly TrayMetadataIndexStore _metadataIndexStore;
    private readonly ITrayThumbnailService _trayThumbnailService;
    private readonly ITrayMetadataService _trayMetadataService;
    private readonly object _cacheGate = new();
    private readonly object _metadataIndexGate = new();
    private readonly object _thumbnailCleanupGate = new();
    private readonly Dictionary<string, CachedSnapshot> _snapshotCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TrayMetadataIndexState> _metadataIndexCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task> _thumbnailCleanupTasks = new(StringComparer.OrdinalIgnoreCase);

    public SimsTrayPreviewService()
        : this(null, null, null)
    {
    }

    public SimsTrayPreviewService(ITrayThumbnailService? trayThumbnailService)
        : this(trayThumbnailService, null, null)
    {
    }

    public SimsTrayPreviewService(
        ITrayThumbnailService? trayThumbnailService,
        ITrayMetadataService? trayMetadataService)
        : this(trayThumbnailService, trayMetadataService, null)
    {
    }

    public SimsTrayPreviewService(
        ITrayThumbnailService? trayThumbnailService,
        ITrayMetadataService? trayMetadataService,
        TrayMetadataIndexStore? metadataIndexStore)
    {
        _metadataIndexStore = metadataIndexStore ?? new TrayMetadataIndexStore();
        _trayThumbnailService = trayThumbnailService ?? NullTrayThumbnailService.Instance;
        _trayMetadataService = trayMetadataService ?? NullTrayMetadataService.Instance;
    }

    public Task<SimsTrayPreviewSummary> BuildSummaryAsync(
        SimsTrayPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(() =>
        {
            var snapshot = GetOrBuildSnapshot(request, cancellationToken);
            return snapshot.Summary;
        }, cancellationToken);
    }

    public Task<SimsTrayPreviewPage> BuildPageAsync(
        SimsTrayPreviewRequest request,
        int pageIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(() =>
        {
            var snapshot = GetOrBuildSnapshot(request, cancellationToken);
            var totalItems = snapshot.TotalItemCount;
            var pageSize = Math.Clamp(request.PageSize, 1, 500);
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
            var normalizedPageIndex = Math.Clamp(pageIndex, 1, totalPages);
            var skip = (normalizedPageIndex - 1) * pageSize;
            IReadOnlyList<SimsTrayPreviewItem> items;
            if (snapshot.HasMaterializedRows)
            {
                items = snapshot.Rows.Skip(skip).Take(pageSize).ToList();
            }
            else
            {
                var pageDescriptors = snapshot.RowDescriptors.Skip(skip).Take(pageSize).ToList();
                items = BuildPageItems(request.TrayPath.Trim(), pageDescriptors, cancellationToken);
            }

            return new SimsTrayPreviewPage
            {
                PageIndex = normalizedPageIndex,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = totalPages,
                Items = items
            };
        }, cancellationToken);
    }

    private CachedSnapshot GetOrBuildSnapshot(
        SimsTrayPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(request);

        lock (_cacheGate)
        {
            if (_snapshotCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        var built = BuildSnapshotCore(request, cacheKey, cancellationToken);

        lock (_cacheGate)
        {
            _snapshotCache[cacheKey] = built;

            if (_snapshotCache.Count > MaxCachedSnapshots)
            {
                var staleKeys = _snapshotCache
                    .OrderBy(pair => pair.Value.CachedAtUtc)
                    .Take(_snapshotCache.Count - MaxCachedSnapshots)
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (var staleKey in staleKeys)
                {
                    _snapshotCache.Remove(staleKey);
                }
            }

            return built;
        }
    }

    private CachedSnapshot BuildSnapshotCore(
        SimsTrayPreviewRequest request,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var trayPath = request.TrayPath.Trim();
        var normalizedTrayRoot = NormalizeTrayRootPath(trayPath);
        var dir = new DirectoryInfo(trayPath);
        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException($"Tray path does not exist: {trayPath}");
        }

        var allFiles = dir
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(file => SupportedExtensions.Contains(file.Extension))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selectedFiles = allFiles;
        var fileEntries = selectedFiles
            .Select(file => new TrayFileEntry(file, ParseIdentity(file.Name)))
            .ToList();
        var groups = new Dictionary<string, GroupAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in fileEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = entry.File;
            var identity = entry.Identity;
            var key = identity.ParseSuccess
                ? identity.InstanceHex
                : Path.GetFileNameWithoutExtension(file.Name);
            if (!groups.TryGetValue(key, out var group))
            {
                group = new GroupAccumulator(key);
                groups[key] = group;
            }

            group.FileCount++;
            group.TotalBytes += file.Length;
            if (file.LastWriteTimeUtc > group.LatestWriteTimeUtc)
            {
                group.LatestWriteTimeUtc = file.LastWriteTimeUtc;
            }

            if (identity.ParseSuccess && !string.IsNullOrWhiteSpace(identity.TypeHex))
            {
                group.ResourceTypes.Add(identity.TypeHex);
                if (group.RepresentativeIdentity is null)
                {
                    group.RepresentativeIdentity = identity;
                }

                if (string.IsNullOrWhiteSpace(group.TrayInstanceId))
                {
                    group.TrayInstanceId = identity.InstanceHex;
                }
            }

            if (IsHouseholdAnchorExtension(file.Extension))
            {
                group.HasHouseholdAnchorFile = true;
            }

            group.Extensions.Add(file.Extension);
            group.SourceFiles.Add(file.FullName);

            if (group.FileNames.Count < MaxPreviewFileNames)
            {
                group.FileNames.Add(file.Name);
            }

            if (string.IsNullOrWhiteSpace(group.ItemName) &&
                string.Equals(file.Extension, ".trayitem", StringComparison.OrdinalIgnoreCase))
            {
                group.ItemName = Path.GetFileNameWithoutExtension(file.Name);
            }

            if (string.IsNullOrWhiteSpace(group.TrayItemPath) &&
                string.Equals(file.Extension, ".trayitem", StringComparison.OrdinalIgnoreCase))
            {
                group.TrayItemPath = file.FullName;
            }
        }

        var householdAnchorInstances = groups
            .Values
            .Where(group => group.HasHouseholdAnchorFile &&
                            group.Key.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var childParentKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resolvedRootKeys = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var childGroupsByParent = new Dictionary<string, List<GroupAccumulator>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups.Values)
        {
            if (!TryResolveAuxiliaryHouseholdRootKey(
                    group,
                    groups,
                    householdAnchorInstances,
                    resolvedRootKeys,
                    out var parentKey))
            {
                continue;
            }

            childParentKeys[group.Key] = parentKey;
            if (!childGroupsByParent.TryGetValue(parentKey, out var childGroups))
            {
                childGroups = new List<GroupAccumulator>();
                childGroupsByParent[parentKey] = childGroups;
            }

            childGroups.Add(group);
        }

        IEnumerable<PreviewRowDescriptor> orderedRows = groups
            .Values
            .Where(group => !childParentKeys.ContainsKey(group.Key))
            .Select(group => CreatePreviewRowDescriptor(
                group,
                childGroupsByParent.TryGetValue(group.Key, out var childGroups)
                    ? childGroups
                    : Array.Empty<GroupAccumulator>()))
            .OrderByDescending(item => item.LatestWriteTimeLocal)
            .ThenByDescending(item => item.FileCount)
            .ThenByDescending(item => item.TotalBytes)
            .ThenBy(item => item.Group.Key, StringComparer.OrdinalIgnoreCase);

        ScheduleThumbnailCleanup(trayPath, groups.Keys.ToArray());

        orderedRows = ApplyPresetTypeFilter(orderedRows, request.PresetTypeFilter);
        orderedRows = ApplyBuildSizeFilter(orderedRows, request.BuildSizeFilter);
        orderedRows = ApplyHouseholdSizeFilter(orderedRows, request.HouseholdSizeFilter);
        orderedRows = ApplyTimeFilter(orderedRows, request.TimeFilter);
        if (HasMetadataDependentFilters(request))
        {
            var preIndexedRows = orderedRows.ToList();
            EnsureMetadataIndex(normalizedTrayRoot, preIndexedRows, cancellationToken);
            var metadataIndex = GetMetadataIndexEntries(normalizedTrayRoot, preIndexedRows);

            orderedRows = ApplyAuthorFilter(preIndexedRows, request.AuthorFilter, metadataIndex);
            orderedRows = ApplySearchFilter(orderedRows, request.SearchQuery, metadataIndex);
        }

        var filteredRows = orderedRows.ToList();

        return new CachedSnapshot
        {
            CacheKey = cacheKey,
            RowDescriptors = filteredRows,
            Summary = CreateSummary(filteredRows),
            CachedAtUtc = DateTime.UtcNow
        };
    }

    private IReadOnlyList<SimsTrayPreviewItem> BuildPageItems(
        string trayPath,
        IReadOnlyList<PreviewRowDescriptor> rows,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<SimsTrayPreviewItem>();
        }

        var normalizedTrayRoot = NormalizeTrayRootPath(trayPath);
        var metadataByTrayItemPath = LoadMetadataByTrayItemPath(normalizedTrayRoot, rows, cancellationToken);
        return rows
            .Select(row => CreateAggregatePreviewItem(
                trayPath,
                row.Group,
                TryGetMetadata(row.Group, metadataByTrayItemPath),
                row.ChildGroups))
            .ToList();
    }

    private IReadOnlyDictionary<string, TrayMetadataResult> LoadMetadataByTrayItemPath(
        string normalizedTrayRoot,
        IReadOnlyCollection<PreviewRowDescriptor> rows,
        CancellationToken cancellationToken)
    {
        PrimeMetadataIndexFromStore(normalizedTrayRoot, rows);

        var trayItemPaths = rows
            .Select(row => row.Group.TrayItemPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (trayItemPaths.Length == 0)
        {
            return new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase);
        }

        var pendingPaths = trayItemPaths
            .Where(path => !HasMetadataIndexEntry(normalizedTrayRoot, path))
            .ToArray();
        if (pendingPaths.Length != 0)
        {
            var loaded = _trayMetadataService
                .GetMetadataAsync(pendingPaths, cancellationToken)
                .GetAwaiter()
                .GetResult();

            UpdateMetadataIndex(
                normalizedTrayRoot,
                rows.Where(row => pendingPaths.Contains(row.Group.TrayItemPath, StringComparer.OrdinalIgnoreCase)).ToArray(),
                loaded);
            _metadataIndexStore.Store(loaded);
        }

        return GetCachedMetadataByTrayItemPath(normalizedTrayRoot, trayItemPaths);
    }

    private void EnsureMetadataIndex(
        string normalizedTrayRoot,
        IReadOnlyCollection<PreviewRowDescriptor> rows,
        CancellationToken cancellationToken)
    {
        PrimeMetadataIndexFromStore(normalizedTrayRoot, rows);

        var pendingPaths = rows
            .Select(row => row.Group.TrayItemPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !HasMetadataIndexEntry(normalizedTrayRoot, path))
            .ToArray();
        if (pendingPaths.Length == 0)
        {
            return;
        }

        var loaded = _trayMetadataService
            .GetMetadataAsync(pendingPaths, cancellationToken)
            .GetAwaiter()
            .GetResult();

        UpdateMetadataIndex(
            normalizedTrayRoot,
            rows.Where(row => pendingPaths.Contains(row.Group.TrayItemPath, StringComparer.OrdinalIgnoreCase)).ToArray(),
            loaded);
        _metadataIndexStore.Store(loaded);
    }

    private IReadOnlyDictionary<string, MetadataIndexEntry> GetMetadataIndexEntries(
        string normalizedTrayRoot,
        IReadOnlyCollection<PreviewRowDescriptor> rows)
    {
        var trayItemPaths = rows
            .Select(row => row.Group.TrayItemPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (trayItemPaths.Count == 0)
        {
            return new Dictionary<string, MetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
        }

        lock (_metadataIndexGate)
        {
            if (!_metadataIndexCache.TryGetValue(normalizedTrayRoot, out var state))
            {
                return new Dictionary<string, MetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
            }

            return state.Entries
                .Where(pair => trayItemPaths.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }
    }

    private void UpdateMetadataIndex(
        string normalizedTrayRoot,
        IReadOnlyCollection<PreviewRowDescriptor> rows,
        IReadOnlyDictionary<string, TrayMetadataResult> metadataByTrayItemPath,
        bool markMissingAsIndexed = true)
    {
        if (rows.Count == 0)
        {
            return;
        }

        lock (_metadataIndexGate)
        {
            if (!_metadataIndexCache.TryGetValue(normalizedTrayRoot, out var state))
            {
                state = new TrayMetadataIndexState();
                _metadataIndexCache[normalizedTrayRoot] = state;
            }

            foreach (var row in rows)
            {
                var trayItemPath = row.Group.TrayItemPath;
                if (string.IsNullOrWhiteSpace(trayItemPath))
                {
                    continue;
                }

                if (!metadataByTrayItemPath.TryGetValue(trayItemPath, out var metadata) &&
                    !markMissingAsIndexed)
                {
                    continue;
                }

                state.Entries[trayItemPath] = CreateMetadataIndexEntry(row, metadata);
                state.MetadataByTrayItemPath[trayItemPath] = metadata;
            }
        }
    }

    private void PrimeMetadataIndexFromStore(
        string normalizedTrayRoot,
        IReadOnlyCollection<PreviewRowDescriptor> rows)
    {
        var pendingRows = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Group.TrayItemPath) &&
                          !HasMetadataIndexEntry(normalizedTrayRoot, row.Group.TrayItemPath))
            .ToArray();
        if (pendingRows.Length == 0)
        {
            return;
        }

        var persisted = _metadataIndexStore.GetMetadata(
            pendingRows
                .Select(row => row.Group.TrayItemPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray());
        if (persisted.Count == 0)
        {
            return;
        }

        UpdateMetadataIndex(
            normalizedTrayRoot,
            pendingRows,
            persisted,
            markMissingAsIndexed: false);
    }

    private IReadOnlyDictionary<string, TrayMetadataResult> GetCachedMetadataByTrayItemPath(
        string normalizedTrayRoot,
        IReadOnlyCollection<string> trayItemPaths)
    {
        if (trayItemPaths.Count == 0)
        {
            return new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase);
        }

        lock (_metadataIndexGate)
        {
            if (!_metadataIndexCache.TryGetValue(normalizedTrayRoot, out var state))
            {
                return new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase);
            }

            return trayItemPaths
                .Where(path => state.MetadataByTrayItemPath.TryGetValue(path, out var metadata) && metadata is not null)
                .ToDictionary(
                    path => path,
                    path => state.MetadataByTrayItemPath[path]!,
                    StringComparer.OrdinalIgnoreCase);
        }
    }

    private bool HasMetadataIndexEntry(string normalizedTrayRoot, string trayItemPath)
    {
        lock (_metadataIndexGate)
        {
            return _metadataIndexCache.TryGetValue(normalizedTrayRoot, out var state) &&
                   state.Entries.ContainsKey(trayItemPath);
        }
    }

    private static bool HasMetadataDependentFilters(SimsTrayPreviewRequest request)
    {
        return (!string.IsNullOrWhiteSpace(request.AuthorFilter) &&
                !string.Equals(request.AuthorFilter, "All", StringComparison.OrdinalIgnoreCase)) ||
               !string.IsNullOrWhiteSpace(request.SearchQuery);
    }

    private static string NormalizeTrayRootPath(string trayRootPath)
    {
        return Path.GetFullPath(trayRootPath.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static PreviewRowDescriptor CreatePreviewRowDescriptor(
        GroupAccumulator group,
        IReadOnlyList<GroupAccumulator> childGroups)
    {
        var relatedGroups = new[] { group }
            .Concat(childGroups)
            .ToList();
        var orderedExtensions = relatedGroups
            .SelectMany(entry => entry.Extensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ext => ext, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var previewFileNames = relatedGroups
            .SelectMany(entry => entry.FileNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxPreviewFileNames)
            .ToList();
        var latestWriteTimeUtc = relatedGroups.Max(entry => entry.LatestWriteTimeUtc);

        return new PreviewRowDescriptor
        {
            Group = group,
            ChildGroups = childGroups,
            PresetType = InferPresetType(orderedExtensions),
            ItemName = ResolveDefaultTitle(group, metadata: null),
            FileListPreview = string.Join("|", previewFileNames),
            NormalizedFallbackSearchText = BuildNormalizedSearchText(group, childGroups, metadata: null),
            FileCount = relatedGroups.Sum(entry => entry.FileCount),
            TotalBytes = relatedGroups.Sum(entry => entry.TotalBytes),
            LatestWriteTimeLocal = latestWriteTimeUtc == DateTime.MinValue
                ? DateTime.MinValue
                : latestWriteTimeUtc.ToLocalTime()
        };
    }

    private static MetadataIndexEntry CreateMetadataIndexEntry(
        PreviewRowDescriptor row,
        TrayMetadataResult? metadata)
    {
        var creatorName = string.IsNullOrWhiteSpace(metadata?.CreatorName) ? string.Empty : metadata.CreatorName;
        var creatorId = string.IsNullOrWhiteSpace(metadata?.CreatorId) ? string.Empty : metadata.CreatorId;

        return new MetadataIndexEntry
        {
            AuthorSearchText = BuildAuthorSearchText(creatorName, creatorId),
            NormalizedSearchText = BuildNormalizedSearchText(row.Group, row.ChildGroups, metadata)
        };
    }

    private static SimsTrayPreviewItem CreateAggregatePreviewItem(
        string trayPath,
        GroupAccumulator group,
        TrayMetadataResult? metadata,
        IReadOnlyList<GroupAccumulator> childGroups)
    {
        var relatedGroups = new[] { group }
            .Concat(childGroups)
            .ToList();

        var orderedExtensions = relatedGroups
            .SelectMany(entry => entry.Extensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ext => ext, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedSourceFiles = relatedGroups
            .SelectMany(entry => entry.SourceFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var previewFileNames = relatedGroups
            .SelectMany(entry => entry.FileNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxPreviewFileNames)
            .ToList();
        var totalBytes = relatedGroups.Sum(entry => entry.TotalBytes);
        var fileCount = relatedGroups.Sum(entry => entry.FileCount);
        var latestWriteTimeUtc = relatedGroups.Max(entry => entry.LatestWriteTimeUtc);
        var memberMetadataBySlot = CreateMemberMetadataLookup(metadata);
        var childItems = childGroups
            .OrderBy(entry => TryGetAuxiliaryHouseholdMemberSlot(entry, out var slot) ? slot : int.MaxValue)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry =>
            {
                TrayMemberDisplayMetadata? memberMetadata = null;
                if (TryGetAuxiliaryHouseholdMemberSlot(entry, out var slot) &&
                    memberMetadataBySlot.TryGetValue(slot, out var resolvedMemberMetadata))
                {
                    memberMetadata = resolvedMemberMetadata;
                }

                return CreateStandalonePreviewItem(
                    trayPath,
                    entry,
                    isChildItem: true,
                    memberMetadata);
            })
            .ToList();
        var resourceTypes = string.Join(",", relatedGroups
            .SelectMany(entry => entry.ResourceTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        var extensions = string.Join(",", orderedExtensions);
        var fileListPreview = string.Join("|", previewFileNames);
        var latestWriteTimeLocal = latestWriteTimeUtc == DateTime.MinValue
            ? DateTime.MinValue
            : latestWriteTimeUtc.ToLocalTime();
        var creatorName = string.IsNullOrWhiteSpace(metadata?.CreatorName) ? string.Empty : metadata.CreatorName;
        var creatorId = string.IsNullOrWhiteSpace(metadata?.CreatorId) ? string.Empty : metadata.CreatorId;

        return new SimsTrayPreviewItem
        {
            TrayItemKey = group.Key,
            PresetType = InferPresetType(orderedExtensions),
            ChildItems = childItems,
            DisplayTitle = ResolveDefaultTitle(group, metadata),
            DisplaySubtitle = ResolveDefaultSubtitle(metadata),
            DisplayDescription = ResolveDefaultDescription(metadata),
            DisplayPrimaryMeta = ResolvePrimaryMeta(group, metadata, childItems),
            DisplaySecondaryMeta = ResolveSecondaryMeta(metadata, childItems),
            DisplayTertiaryMeta = ResolveTertiaryMeta(metadata),
            DebugMetadata = new TrayDebugMetadata
            {
                TrayItemKey = group.Key,
                TrayInstanceId = group.TrayInstanceId,
                CreatorId = creatorId,
                CreatorName = creatorName,
                FileCount = fileCount,
                TotalMB = Math.Round(totalBytes / (1024d * 1024d), 4),
                LatestWriteTimeLocal = latestWriteTimeLocal,
                Extensions = extensions,
                ResourceTypes = resourceTypes,
                FileListPreview = fileListPreview,
                SourceFilePaths = orderedSourceFiles
            },
            TrayRootPath = trayPath,
            TrayInstanceId = group.TrayInstanceId,
            SourceFilePaths = orderedSourceFiles,
            ItemName = ResolveDefaultTitle(group, metadata),
            AuthorId = BuildAuthorSearchText(creatorName, creatorId),
            FileCount = fileCount,
            TotalBytes = totalBytes,
            TotalMB = Math.Round(totalBytes / (1024d * 1024d), 4),
            LatestWriteTimeLocal = latestWriteTimeLocal,
            ResourceTypes = resourceTypes,
            Extensions = extensions,
            FileListPreview = fileListPreview
        };
    }

    private static SimsTrayPreviewItem CreateStandalonePreviewItem(
        string trayPath,
        GroupAccumulator group,
        bool isChildItem,
        TrayMemberDisplayMetadata? memberMetadata = null)
    {
        var orderedExtensions = group.Extensions
            .OrderBy(ext => ext, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedSourceFiles = group.SourceFiles
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var resourceTypes = string.Join(",", group.ResourceTypes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        var extensions = string.Join(",", orderedExtensions);
        var fileListPreview = string.Join("|", group.FileNames);
        var latestWriteTimeLocal = group.LatestWriteTimeUtc == DateTime.MinValue
            ? DateTime.MinValue
            : group.LatestWriteTimeUtc.ToLocalTime();
        var fallbackTitle = ResolveFallbackStandaloneTitle(group, isChildItem);
        var displayTitle = string.IsNullOrWhiteSpace(memberMetadata?.FullName)
            ? fallbackTitle
            : memberMetadata!.FullName;
        var displaySubtitle = memberMetadata?.Subtitle ?? string.Empty;
        var displayDescription = memberMetadata?.Detail ?? string.Empty;

        return new SimsTrayPreviewItem
        {
            TrayItemKey = group.Key,
            PresetType = isChildItem ? "Member" : InferPresetType(orderedExtensions),
            ChildItems = Array.Empty<SimsTrayPreviewItem>(),
            DisplayTitle = displayTitle,
            DisplaySubtitle = displaySubtitle,
            DisplayDescription = displayDescription,
            DisplayPrimaryMeta = string.Empty,
            DisplaySecondaryMeta = string.Empty,
            DisplayTertiaryMeta = string.Empty,
            DebugMetadata = new TrayDebugMetadata
            {
                TrayItemKey = group.Key,
                TrayInstanceId = group.TrayInstanceId,
                FileCount = group.FileCount,
                TotalMB = Math.Round(group.TotalBytes / (1024d * 1024d), 4),
                LatestWriteTimeLocal = latestWriteTimeLocal,
                Extensions = extensions,
                ResourceTypes = resourceTypes,
                FileListPreview = fileListPreview,
                SourceFilePaths = orderedSourceFiles
            },
            TrayRootPath = trayPath,
            TrayInstanceId = group.TrayInstanceId,
            SourceFilePaths = orderedSourceFiles,
            ItemName = displayTitle,
            AuthorId = string.Empty,
            FileCount = group.FileCount,
            TotalBytes = group.TotalBytes,
            TotalMB = Math.Round(group.TotalBytes / (1024d * 1024d), 4),
            LatestWriteTimeLocal = latestWriteTimeLocal,
            ResourceTypes = resourceTypes,
            Extensions = extensions,
            FileListPreview = fileListPreview
        };
    }

    private static IReadOnlyDictionary<int, TrayMemberDisplayMetadata> CreateMemberMetadataLookup(TrayMetadataResult? metadata)
    {
        if (metadata is null || metadata.Members.Count == 0)
        {
            return new Dictionary<int, TrayMemberDisplayMetadata>();
        }

        return metadata.Members
            .Where(member => member.SlotIndex > 0)
            .GroupBy(member => member.SlotIndex)
            .ToDictionary(
                group => group.Key,
                group => group.First());
    }

    private static string ResolveDefaultTitle(GroupAccumulator group, TrayMetadataResult? metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata?.Name))
        {
            return metadata.Name;
        }

        return string.IsNullOrWhiteSpace(group.ItemName)
            ? group.Key
            : group.ItemName;
    }

    private static string ResolveDefaultSubtitle(TrayMetadataResult? metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata?.CreatorName))
        {
            return $"by {metadata.CreatorName}";
        }

        if (!string.IsNullOrWhiteSpace(metadata?.CreatorId))
        {
            return $"by {metadata.CreatorId}";
        }

        return string.Empty;
    }

    private static string ResolveDefaultDescription(TrayMetadataResult? metadata)
    {
        return metadata?.Description?.Trim() ?? string.Empty;
    }

    private static string ResolvePrimaryMeta(
        GroupAccumulator group,
        TrayMetadataResult? metadata,
        IReadOnlyList<SimsTrayPreviewItem> childItems)
    {
        if (metadata?.FamilySize is > 0)
        {
            var label = metadata.FamilySize == 1 ? "Sim" : "Sims";
            return $"{metadata.FamilySize} {label}";
        }

        if (metadata?.SizeX is > 0 && metadata?.SizeZ is > 0)
        {
            return $"{metadata.SizeX} x {metadata.SizeZ}";
        }

        return $"Files: {group.FileCount}";
    }

    private static string ResolveSecondaryMeta(
        TrayMetadataResult? metadata,
        IReadOnlyList<SimsTrayPreviewItem> childItems)
    {
        if (metadata?.FamilySize is > 0)
        {
            var names = metadata.Members
                .Select(member => member.FullName)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            if (names.Count == 0)
            {
                names = childItems
                    .Select(item => item.DisplayTitle)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
            }

            return string.Join(", ", names.Take(3));
        }

        if (metadata?.PriceValue is > 0)
        {
            return $"§{metadata.PriceValue.Value:N0}";
        }

        return string.Empty;
    }

    private static string ResolveTertiaryMeta(TrayMetadataResult? metadata)
    {
        if (metadata is null)
        {
            return string.Empty;
        }

        if (metadata.NumBedrooms is not null || metadata.NumBathrooms is not null)
        {
            return $"{metadata.NumBedrooms ?? 0} bd • {metadata.NumBathrooms ?? 0} ba";
        }

        if (metadata.Height is > 0)
        {
            return $"Height {metadata.Height}";
        }

        if (metadata.PendingBabies is > 0)
        {
            return $"Pending Babies: {metadata.PendingBabies}";
        }

        return string.Empty;
    }

    private static string ResolveFallbackStandaloneTitle(GroupAccumulator group, bool isChildItem)
    {
        if (isChildItem &&
            TryGetAuxiliaryHouseholdMemberSlot(group, out var slot))
        {
            return $"Member {slot}";
        }

        return string.IsNullOrWhiteSpace(group.ItemName)
            ? group.Key
            : group.ItemName;
    }

    private static string BuildAuthorSearchText(string creatorName, string creatorId)
    {
        if (string.IsNullOrWhiteSpace(creatorName))
        {
            return creatorId;
        }

        if (string.IsNullOrWhiteSpace(creatorId))
        {
            return creatorName;
        }

        return $"{creatorName} {creatorId}";
    }

    private static string BuildNormalizedSearchText(
        GroupAccumulator group,
        IReadOnlyList<GroupAccumulator> childGroups,
        TrayMetadataResult? metadata)
    {
        var normalizedParts = new List<string>
        {
            NormalizeSearch(ResolveDefaultTitle(group, metadata)),
            NormalizeSearch(BuildAuthorSearchText(
                metadata?.CreatorName?.Trim() ?? string.Empty,
                metadata?.CreatorId?.Trim() ?? string.Empty)),
            NormalizeSearch(group.Key)
        };

        var memberMetadataBySlot = CreateMemberMetadataLookup(metadata);
        foreach (var childGroup in childGroups
                     .OrderBy(entry => TryGetAuxiliaryHouseholdMemberSlot(entry, out var slot) ? slot : int.MaxValue)
                     .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            normalizedParts.Add(NormalizeSearch(childGroup.Key));

            TrayMemberDisplayMetadata? memberMetadata = null;
            if (TryGetAuxiliaryHouseholdMemberSlot(childGroup, out var slot) &&
                memberMetadataBySlot.TryGetValue(slot, out var resolvedMemberMetadata))
            {
                memberMetadata = resolvedMemberMetadata;
            }

            var childTitle = string.IsNullOrWhiteSpace(memberMetadata?.FullName)
                ? ResolveFallbackStandaloneTitle(childGroup, isChildItem: true)
                : memberMetadata!.FullName;
            normalizedParts.Add(NormalizeSearch(childTitle));
        }

        return string.Join("|", normalizedParts.Where(value => value.Length != 0));
    }

    private static TrayMetadataResult? TryGetMetadata(
        GroupAccumulator group,
        IReadOnlyDictionary<string, TrayMetadataResult> metadataByTrayItemPath)
    {
        if (string.IsNullOrWhiteSpace(group.TrayItemPath))
        {
            return null;
        }

        return metadataByTrayItemPath.TryGetValue(group.TrayItemPath, out var metadata)
            ? metadata
            : null;
    }

    private static bool TryResolveAuxiliaryHouseholdRootKey(
        GroupAccumulator group,
        IReadOnlyDictionary<string, GroupAccumulator> groups,
        IReadOnlySet<string> householdAnchorInstances,
        IDictionary<string, string?> resolvedRootKeys,
        out string parentKey)
    {
        parentKey = string.Empty;

        if (resolvedRootKeys.TryGetValue(group.Key, out var cachedRootKey))
        {
            if (string.IsNullOrWhiteSpace(cachedRootKey))
            {
                return false;
            }

            parentKey = cachedRootKey;
            return true;
        }

        var visitedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!TryResolveAuxiliaryHouseholdRootKeyCore(
                group,
                groups,
                householdAnchorInstances,
                resolvedRootKeys,
                visitedKeys,
                out parentKey))
        {
            resolvedRootKeys[group.Key] = null;
            return false;
        }

        resolvedRootKeys[group.Key] = parentKey;
        return true;
    }

    private static bool TryResolveAuxiliaryHouseholdRootKeyCore(
        GroupAccumulator group,
        IReadOnlyDictionary<string, GroupAccumulator> groups,
        IReadOnlySet<string> householdAnchorInstances,
        IDictionary<string, string?> resolvedRootKeys,
        ISet<string> visitedKeys,
        out string parentKey)
    {
        parentKey = string.Empty;

        if (group.Extensions.Count != 1 ||
            !group.Extensions.Contains(".sgi", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!visitedKeys.Add(group.Key) ||
            !TryGetAuxiliaryHouseholdMemberSlot(group, out var slot) ||
            group.RepresentativeIdentity is null)
        {
            return false;
        }

        if (!TryResolveAuxiliaryHouseholdDirectParentKey(
                group,
                groups,
                slot,
                out var directParentKey,
                out var directParentGroup))
        {
            return false;
        }

        if (householdAnchorInstances.Contains(directParentKey))
        {
            parentKey = directParentKey;
            resolvedRootKeys[group.Key] = parentKey;
            return true;
        }

        if (!TryResolveAuxiliaryHouseholdRootKeyCore(
                directParentGroup,
                groups,
                householdAnchorInstances,
                resolvedRootKeys,
                visitedKeys,
                out parentKey))
        {
            return false;
        }

        resolvedRootKeys[group.Key] = parentKey;
        return !string.IsNullOrWhiteSpace(parentKey);
    }

    private static bool TryResolveAuxiliaryHouseholdDirectParentKey(
        GroupAccumulator group,
        IReadOnlyDictionary<string, GroupAccumulator> groups,
        int slot,
        out string parentKey,
        out GroupAccumulator parentGroup)
    {
        parentKey = string.Empty;
        parentGroup = null!;

        if (group.RepresentativeIdentity is null || slot < 1)
        {
            return false;
        }

        var currentInstanceValue = ulong.Parse(
            group.RepresentativeIdentity.InstanceHex.AsSpan(2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
        if (currentInstanceValue <= (ulong)slot)
        {
            return false;
        }

        var reducedValue = currentInstanceValue - (ulong)slot;
        var memberSpaceParentKey = $"0x{reducedValue:x16}";
        if (!memberSpaceParentKey.Equals(group.Key, StringComparison.OrdinalIgnoreCase) &&
            groups.TryGetValue(memberSpaceParentKey, out var resolvedMemberSpaceParent) &&
            resolvedMemberSpaceParent is not null)
        {
            parentGroup = resolvedMemberSpaceParent;
            parentKey = memberSpaceParentKey;
            return true;
        }

        var highByteMask = currentInstanceValue & 0xFF00000000000000UL;
        if (highByteMask == 0 || reducedValue <= highByteMask)
        {
            return false;
        }

        var anchorSpaceValue = reducedValue - highByteMask;
        var anchorSpaceParentKey = $"0x{anchorSpaceValue:x16}";
        if (anchorSpaceParentKey.Equals(group.Key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (groups.TryGetValue(anchorSpaceParentKey, out var resolvedAnchorSpaceParent) &&
            resolvedAnchorSpaceParent is not null)
        {
            parentGroup = resolvedAnchorSpaceParent;
            parentKey = anchorSpaceParentKey;
            return true;
        }

        return TryResolveAuxiliaryHouseholdAnchorFallbackKey(
            anchorSpaceValue,
            group.Key,
            groups,
            out parentKey,
            out parentGroup);
    }

    private static bool TryResolveAuxiliaryHouseholdAnchorFallbackKey(
        ulong anchorSpaceValue,
        string currentKey,
        IReadOnlyDictionary<string, GroupAccumulator> groups,
        out string parentKey,
        out GroupAccumulator parentGroup)
    {
        parentKey = string.Empty;
        parentGroup = null!;

        for (var offset = 1; offset <= MaxSupportedHouseholdMembers; offset++)
        {
            if (anchorSpaceValue < (ulong)offset)
            {
                break;
            }

            var candidateKey = $"0x{anchorSpaceValue - (ulong)offset:x16}";
            if (candidateKey.Equals(currentKey, StringComparison.OrdinalIgnoreCase) ||
                !groups.TryGetValue(candidateKey, out var candidateGroup) ||
                candidateGroup is null ||
                !candidateGroup.HasHouseholdAnchorFile)
            {
                continue;
            }

            parentKey = candidateKey;
            parentGroup = candidateGroup;
            return true;
        }

        return false;
    }

    private static bool TryGetAuxiliaryHouseholdMemberSlot(
        GroupAccumulator group,
        out int slot)
    {
        slot = 0;

        if (group.RepresentativeIdentity is null)
        {
            return false;
        }

        return TryParseAuxiliaryHouseholdMemberSlot(group.RepresentativeIdentity, out slot);
    }

    private static bool TryParseAuxiliaryHouseholdMemberSlot(
        TrayIdentity identity,
        out int slot)
    {
        slot = 0;

        if (!identity.ParseSuccess ||
            string.IsNullOrWhiteSpace(identity.TypeHex))
        {
            return false;
        }

        var typeValue = uint.Parse(
            identity.TypeHex.AsSpan(2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
        if ((typeValue & 0xF) != 0x3)
        {
            return false;
        }

        var candidateSlot = (int)((typeValue >> 4) & 0xF);
        if (candidateSlot is < 1 or > MaxSupportedHouseholdMembers)
        {
            return false;
        }

        slot = candidateSlot;
        return true;
    }

    private static string BuildCacheKey(SimsTrayPreviewRequest request)
    {
        var normalizedTrayPath = Path.GetFullPath(request.TrayPath.Trim()).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        var normalizedPresetTypeFilter = NormalizeFilterToken(request.PresetTypeFilter);
        var normalizedBuildSizeFilter = NormalizeFilterToken(request.BuildSizeFilter);
        var normalizedHouseholdSizeFilter = NormalizeFilterToken(request.HouseholdSizeFilter);
        var normalizedAuthorFilter = NormalizeFilterToken(request.AuthorFilter);
        var normalizedTimeFilter = NormalizeFilterToken(request.TimeFilter);
        var normalizedSearchQuery = NormalizeFilterToken(request.SearchQuery);

        return string.Join(
            "|",
            normalizedTrayPath,
            request.PageSize,
            normalizedPresetTypeFilter,
            normalizedBuildSizeFilter,
            normalizedHouseholdSizeFilter,
            normalizedAuthorFilter,
            normalizedTimeFilter,
            normalizedSearchQuery);
    }

    private static TrayIdentity ParseIdentity(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var match = TrayIdentityRegex.Match(baseName);
        if (!match.Success)
        {
            return new TrayIdentity
            {
                ParseSuccess = false,
                TypeHex = string.Empty,
                InstanceHex = string.Empty
            };
        }

        var typeValue = uint.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var instanceValue = ulong.Parse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new TrayIdentity
        {
            ParseSuccess = true,
            TypeHex = $"0x{typeValue:x8}",
            InstanceHex = $"0x{instanceValue:x16}"
        };
    }

    private static string InferPresetType(IReadOnlyCollection<string> extensions)
    {
        var hasLot = extensions.Contains(".blueprint", StringComparer.OrdinalIgnoreCase) ||
                     extensions.Contains(".bpi", StringComparer.OrdinalIgnoreCase);
        var hasRoom = extensions.Contains(".room", StringComparer.OrdinalIgnoreCase) ||
                      extensions.Contains(".rmi", StringComparer.OrdinalIgnoreCase);
        var hasHousehold = extensions.Contains(".householdbinary", StringComparer.OrdinalIgnoreCase) ||
                           extensions.Contains(".hhi", StringComparer.OrdinalIgnoreCase) ||
                           extensions.Contains(".sgi", StringComparer.OrdinalIgnoreCase);

        var buckets = (hasLot ? 1 : 0) + (hasRoom ? 1 : 0) + (hasHousehold ? 1 : 0);
        if (buckets > 1)
        {
            return "Mixed";
        }

        if (hasLot)
        {
            return "Lot";
        }

        if (hasRoom)
        {
            return "Room";
        }

        if (hasHousehold)
        {
            return "Household";
        }

        if (extensions.Contains(".trayitem", StringComparer.OrdinalIgnoreCase))
        {
            return "GenericTray";
        }

        return "Unknown";
    }

    private static IEnumerable<SimsTrayPreviewItem> ApplyPresetTypeFilter(
        IEnumerable<SimsTrayPreviewItem> rows,
        string? presetTypeFilter)
    {
        if (string.IsNullOrWhiteSpace(presetTypeFilter) ||
            string.Equals(presetTypeFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return rows;
        }

        var normalized = presetTypeFilter.Trim();
        return rows.Where(item => item.PresetType.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<PreviewRowDescriptor> ApplyPresetTypeFilter(
        IEnumerable<PreviewRowDescriptor> rows,
        string? presetTypeFilter)
    {
        if (string.IsNullOrWhiteSpace(presetTypeFilter) ||
            string.Equals(presetTypeFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return rows;
        }

        var normalized = presetTypeFilter.Trim();
        return rows.Where(item => item.PresetType.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<SimsTrayPreviewItem> ApplyAuthorFilter(
        IEnumerable<SimsTrayPreviewItem> rows,
        string? authorFilter)
    {
        if (string.IsNullOrWhiteSpace(authorFilter) ||
            string.Equals(authorFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return rows;
        }

        var needle = authorFilter.Trim();
        if (string.IsNullOrWhiteSpace(needle))
        {
            return rows;
        }

        return rows.Where(item =>
            item.AuthorId.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            item.TrayItemKey.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<PreviewRowDescriptor> ApplyAuthorFilter(
        IEnumerable<PreviewRowDescriptor> rows,
        string? authorFilter,
        IReadOnlyDictionary<string, MetadataIndexEntry> metadataIndex)
    {
        if (string.IsNullOrWhiteSpace(authorFilter) ||
            string.Equals(authorFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return rows;
        }

        var needle = authorFilter.Trim();
        if (string.IsNullOrWhiteSpace(needle))
        {
            return rows;
        }

        return rows.Where(row =>
        {
            var trayItemPath = row.Group.TrayItemPath;
            if (!string.IsNullOrWhiteSpace(trayItemPath) &&
                metadataIndex.TryGetValue(trayItemPath, out var indexed))
            {
                return indexed.AuthorSearchText.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                       row.Group.Key.Contains(needle, StringComparison.OrdinalIgnoreCase);
            }

            return row.Group.Key.Contains(needle, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static IEnumerable<SimsTrayPreviewItem> ApplyBuildSizeFilter(
        IEnumerable<SimsTrayPreviewItem> rows,
        string? buildSizeFilter)
    {
        if (string.IsNullOrWhiteSpace(buildSizeFilter) ||
            string.Equals(buildSizeFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return rows;
        }

        if (!TryParseSizeToken(buildSizeFilter, out var expectedWidth, out var expectedHeight))
        {
            return rows;
        }

        return rows.Where(item =>
        {
            if (!item.PresetType.Equals("Lot", StringComparison.OrdinalIgnoreCase) &&
                !item.PresetType.Equals("Room", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!TryParseBuildDimensions(item, out var width, out var height))
            {
                return false;
            }

            return DimensionsMatchAllowSwap(expectedWidth, expectedHeight, width, height);
        });
    }

    private static IEnumerable<PreviewRowDescriptor> ApplyBuildSizeFilter(
        IEnumerable<PreviewRowDescriptor> rows,
        string? buildSizeFilter)
    {
        if (string.IsNullOrWhiteSpace(buildSizeFilter) ||
            string.Equals(buildSizeFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return rows;
        }

        if (!TryParseSizeToken(buildSizeFilter, out var expectedWidth, out var expectedHeight))
        {
            return rows;
        }

        return rows.Where(item =>
        {
            if (!item.PresetType.Equals("Lot", StringComparison.OrdinalIgnoreCase) &&
                !item.PresetType.Equals("Room", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!TryParseBuildDimensions(item.ItemName, item.FileListPreview, out var width, out var height))
            {
                return false;
            }

            return DimensionsMatchAllowSwap(expectedWidth, expectedHeight, width, height);
        });
    }

    private static IEnumerable<SimsTrayPreviewItem> ApplyHouseholdSizeFilter(
        IEnumerable<SimsTrayPreviewItem> rows,
        string? householdSizeFilter)
    {
        if (string.IsNullOrWhiteSpace(householdSizeFilter) ||
            string.Equals(householdSizeFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return rows;
        }

        var normalized = householdSizeFilter.Trim();
        return rows.Where(item =>
        {
            if (!item.PresetType.Equals("Household", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parsedSize = TryParseHouseholdSize(item);
            if (!int.TryParse(normalized, out var expectedSize))
            {
                return false;
            }

            return parsedSize.HasValue && parsedSize.Value == expectedSize;
        });
    }

    private static IEnumerable<PreviewRowDescriptor> ApplyHouseholdSizeFilter(
        IEnumerable<PreviewRowDescriptor> rows,
        string? householdSizeFilter)
    {
        if (string.IsNullOrWhiteSpace(householdSizeFilter) ||
            string.Equals(householdSizeFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return rows;
        }

        var normalized = householdSizeFilter.Trim();
        return rows.Where(item =>
        {
            if (!item.PresetType.Equals("Household", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parsedSize = TryParseHouseholdSize(item.ItemName, item.FileListPreview);
            if (!int.TryParse(normalized, out var expectedSize))
            {
                return false;
            }

            return parsedSize.HasValue && parsedSize.Value == expectedSize;
        });
    }

    private static IEnumerable<SimsTrayPreviewItem> ApplyTimeFilter(
        IEnumerable<SimsTrayPreviewItem> rows,
        string? timeFilter)
    {
        if (string.IsNullOrWhiteSpace(timeFilter) ||
            string.Equals(timeFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return rows;
        }

        var now = DateTime.Now;
        DateTime minDate;
        if (string.Equals(timeFilter, "Last24h", StringComparison.OrdinalIgnoreCase))
        {
            minDate = now.AddHours(-24);
        }
        else if (string.Equals(timeFilter, "Last7d", StringComparison.OrdinalIgnoreCase))
        {
            minDate = now.AddDays(-7);
        }
        else if (string.Equals(timeFilter, "Last30d", StringComparison.OrdinalIgnoreCase))
        {
            minDate = now.AddDays(-30);
        }
        else if (string.Equals(timeFilter, "Last90d", StringComparison.OrdinalIgnoreCase))
        {
            minDate = now.AddDays(-90);
        }
        else
        {
            return rows;
        }

        return rows.Where(item => item.LatestWriteTimeLocal != DateTime.MinValue && item.LatestWriteTimeLocal >= minDate);
    }

    private static IEnumerable<PreviewRowDescriptor> ApplyTimeFilter(
        IEnumerable<PreviewRowDescriptor> rows,
        string? timeFilter)
    {
        if (string.IsNullOrWhiteSpace(timeFilter) ||
            string.Equals(timeFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return rows;
        }

        var now = DateTime.Now;
        DateTime minDate;
        if (string.Equals(timeFilter, "Last24h", StringComparison.OrdinalIgnoreCase))
        {
            minDate = now.AddHours(-24);
        }
        else if (string.Equals(timeFilter, "Last7d", StringComparison.OrdinalIgnoreCase))
        {
            minDate = now.AddDays(-7);
        }
        else if (string.Equals(timeFilter, "Last30d", StringComparison.OrdinalIgnoreCase))
        {
            minDate = now.AddDays(-30);
        }
        else if (string.Equals(timeFilter, "Last90d", StringComparison.OrdinalIgnoreCase))
        {
            minDate = now.AddDays(-90);
        }
        else
        {
            return rows;
        }

        return rows.Where(item => item.LatestWriteTimeLocal != DateTime.MinValue && item.LatestWriteTimeLocal >= minDate);
    }

    private static IEnumerable<SimsTrayPreviewItem> ApplySearchFilter(
        IEnumerable<SimsTrayPreviewItem> rows,
        string? searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return rows;
        }

        var needle = NormalizeSearch(searchQuery);
        if (needle.Length == 0)
        {
            return rows;
        }

        return rows.Where(item =>
            NormalizeSearch(item.ItemName).Contains(needle, StringComparison.Ordinal) ||
            NormalizeSearch(item.AuthorId).Contains(needle, StringComparison.Ordinal) ||
            NormalizeSearch(item.TrayItemKey).Contains(needle, StringComparison.Ordinal) ||
            item.ChildItems.Any(child =>
                NormalizeSearch(child.ItemName).Contains(needle, StringComparison.Ordinal) ||
                NormalizeSearch(child.AuthorId).Contains(needle, StringComparison.Ordinal) ||
                NormalizeSearch(child.TrayItemKey).Contains(needle, StringComparison.Ordinal)));
    }

    private static IEnumerable<PreviewRowDescriptor> ApplySearchFilter(
        IEnumerable<PreviewRowDescriptor> rows,
        string? searchQuery,
        IReadOnlyDictionary<string, MetadataIndexEntry> metadataIndex)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return rows;
        }

        var needle = NormalizeSearch(searchQuery);
        if (needle.Length == 0)
        {
            return rows;
        }

        return rows.Where(row =>
        {
            var trayItemPath = row.Group.TrayItemPath;
            if (!string.IsNullOrWhiteSpace(trayItemPath) &&
                metadataIndex.TryGetValue(trayItemPath, out var indexed))
            {
                return indexed.NormalizedSearchText.Contains(needle, StringComparison.Ordinal);
            }

            return row.NormalizedFallbackSearchText.Contains(needle, StringComparison.Ordinal);
        });
    }

    private static string NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Trim()
            .ToLowerInvariant()
            .Where(c => !char.IsWhiteSpace(c))
            .ToArray());
    }

    private static string NormalizeFilterToken(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "all"
            : value.Trim().ToLowerInvariant();
    }

    private static SimsTrayPreviewSummary CreateSummary(IReadOnlyCollection<SimsTrayPreviewItem> rows)
    {
        var totalBytes = rows.Sum(item => item.TotalBytes);
        var latestWrite = rows.Count == 0
            ? DateTime.MinValue
            : rows.Max(item => item.LatestWriteTimeLocal);
        var typeBreakdown = rows
            .GroupBy(item => item.PresetType, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}:{group.Count()}")
            .ToList();

        return new SimsTrayPreviewSummary
        {
            TotalItems = rows.Count,
            TotalFiles = rows.Sum(item => item.FileCount),
            TotalBytes = totalBytes,
            TotalMB = Math.Round(totalBytes / (1024d * 1024d), 2),
            LatestWriteTimeLocal = latestWrite,
            PresetTypeBreakdown = string.Join(", ", typeBreakdown)
        };
    }

    private static SimsTrayPreviewSummary CreateSummary(IReadOnlyCollection<PreviewRowDescriptor> rows)
    {
        var totalBytes = rows.Sum(item => item.TotalBytes);
        var latestWrite = rows.Count == 0
            ? DateTime.MinValue
            : rows.Max(item => item.LatestWriteTimeLocal);
        var typeBreakdown = rows
            .GroupBy(item => item.PresetType, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}:{group.Count()}")
            .ToList();

        return new SimsTrayPreviewSummary
        {
            TotalItems = rows.Count,
            TotalFiles = rows.Sum(item => item.FileCount),
            TotalBytes = totalBytes,
            TotalMB = Math.Round(totalBytes / (1024d * 1024d), 2),
            LatestWriteTimeLocal = latestWrite,
            PresetTypeBreakdown = string.Join(", ", typeBreakdown)
        };
    }

    private void ScheduleThumbnailCleanup(string trayRootPath, IReadOnlyCollection<string> liveItemKeys)
    {
        if (string.IsNullOrWhiteSpace(trayRootPath))
        {
            return;
        }

        var normalizedTrayRoot = Path.GetFullPath(trayRootPath.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var snapshotKeys = liveItemKeys.ToArray();

        lock (_thumbnailCleanupGate)
        {
            if (_thumbnailCleanupTasks.TryGetValue(normalizedTrayRoot, out var activeTask) &&
                !activeTask.IsCompleted)
            {
                return;
            }

            Task cleanupTask = Task.CompletedTask;
            cleanupTask = Task.Run(async () =>
            {
                try
                {
                    await _trayThumbnailService
                        .CleanupStaleEntriesAsync(normalizedTrayRoot, snapshotKeys, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                }
                finally
                {
                    lock (_thumbnailCleanupGate)
                    {
                        if (_thumbnailCleanupTasks.TryGetValue(normalizedTrayRoot, out var currentTask) &&
                            ReferenceEquals(currentTask, cleanupTask))
                        {
                            _thumbnailCleanupTasks.Remove(normalizedTrayRoot);
                        }
                    }
                }
            });

            _thumbnailCleanupTasks[normalizedTrayRoot] = cleanupTask;
        }
    }

    private static bool TryParseBuildDimensions(SimsTrayPreviewItem item, out int width, out int height)
    {
        return TryParseBuildDimensions(item.ItemName, item.FileListPreview, out width, out height);
    }

    private static bool TryParseBuildDimensions(
        string itemName,
        string fileListPreview,
        out int width,
        out int height)
    {
        width = 0;
        height = 0;

        var searchSources = new[]
        {
            itemName,
            fileListPreview
        };

        foreach (var source in searchSources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var match = BuildSizeRegex.Match(source);
            if (!match.Success)
            {
                continue;
            }

            if (!int.TryParse(match.Groups[1].Value, out var parsedWidth) ||
                !int.TryParse(match.Groups[2].Value, out var parsedHeight))
            {
                continue;
            }

            if (parsedWidth < 1 || parsedHeight < 1)
            {
                continue;
            }

            width = parsedWidth;
            height = parsedHeight;
            return true;
        }

        return false;
    }

    private static bool TryParseSizeToken(string rawValue, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var match = BuildSizeRegex.Match(rawValue);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out width) ||
            !int.TryParse(match.Groups[2].Value, out height))
        {
            return false;
        }

        return width > 0 && height > 0;
    }

    private static bool DimensionsMatchAllowSwap(int expectedWidth, int expectedHeight, int actualWidth, int actualHeight)
    {
        return (expectedWidth == actualWidth && expectedHeight == actualHeight) ||
               (expectedWidth == actualHeight && expectedHeight == actualWidth);
    }

    private static int? TryParseHouseholdSize(SimsTrayPreviewItem item)
    {
        return TryParseHouseholdSize(item.ItemName, item.FileListPreview);
    }

    private static int? TryParseHouseholdSize(string itemName, string fileListPreview)
    {
        var searchSources = new[]
        {
            itemName,
            fileListPreview
        };

        foreach (var source in searchSources)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var match = HouseholdSizeRegex.Match(source);
            if (!match.Success)
            {
                continue;
            }

            var candidate = match.Groups[1].Success
                ? match.Groups[1].Value
                : match.Groups[2].Value;

            if (!int.TryParse(candidate, out var parsed))
            {
                continue;
            }

            if (parsed < 1)
            {
                continue;
            }

            return Math.Min(parsed, 8);
        }

        return null;
    }

    private sealed class CachedSnapshot
    {
        public required string CacheKey { get; init; }
        public IReadOnlyList<SimsTrayPreviewItem> Rows { get; init; } = Array.Empty<SimsTrayPreviewItem>();
        public IReadOnlyList<PreviewRowDescriptor> RowDescriptors { get; init; } = Array.Empty<PreviewRowDescriptor>();
        public required SimsTrayPreviewSummary Summary { get; init; }
        public required DateTime CachedAtUtc { get; init; }
        public bool HasMaterializedRows => Rows.Count != 0;
        public int TotalItemCount => HasMaterializedRows ? Rows.Count : RowDescriptors.Count;
    }

    private sealed class PreviewRowDescriptor
    {
        public required GroupAccumulator Group { get; init; }
        public required IReadOnlyList<GroupAccumulator> ChildGroups { get; init; }
        public required string PresetType { get; init; }
        public required string ItemName { get; init; }
        public required string FileListPreview { get; init; }
        public required string NormalizedFallbackSearchText { get; init; }
        public required int FileCount { get; init; }
        public required long TotalBytes { get; init; }
        public required DateTime LatestWriteTimeLocal { get; init; }
    }

    private sealed class TrayMetadataIndexState
    {
        public Dictionary<string, MetadataIndexEntry> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, TrayMetadataResult?> MetadataByTrayItemPath { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class MetadataIndexEntry
    {
        public required string AuthorSearchText { get; init; }
        public required string NormalizedSearchText { get; init; }
    }

    private static bool IsHouseholdAnchorExtension(string extension)
    {
        return string.Equals(extension, ".trayitem", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".householdbinary", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".hhi", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class GroupAccumulator
    {
        public GroupAccumulator(string key)
        {
            Key = key;
        }

        public string Key { get; }
        public string ItemName { get; set; } = string.Empty;
        public string TrayInstanceId { get; set; } = string.Empty;
        public string TrayItemPath { get; set; } = string.Empty;
        public TrayIdentity? RepresentativeIdentity { get; set; }
        public bool HasHouseholdAnchorFile { get; set; }
        public int FileCount { get; set; }
        public long TotalBytes { get; set; }
        public DateTime LatestWriteTimeUtc { get; set; } = DateTime.MinValue;
        public HashSet<string> ResourceTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Extensions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> FileNames { get; } = new();
        public List<string> SourceFiles { get; } = new();
    }

    private sealed class TrayFileEntry
    {
        public TrayFileEntry(FileInfo file, TrayIdentity identity)
        {
            File = file;
            Identity = identity;
        }

        public FileInfo File { get; }
        public TrayIdentity Identity { get; }
    }

    private sealed class TrayIdentity
    {
        public bool ParseSuccess { get; init; }
        public string TypeHex { get; init; } = string.Empty;
        public string InstanceHex { get; init; } = string.Empty;
    }
}

