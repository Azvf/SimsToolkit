using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.PackageCore.Performance;

namespace SimsModDesktop.Infrastructure.Tray;

internal sealed class PreviewPageBuilder
{
    private const int MaxPreviewFileNames = 12;
    private readonly PreviewMetadataFacade _metadataFacade;
    private readonly ILogger _logger;

    public PreviewPageBuilder(
        PreviewMetadataFacade metadataFacade,
        ILogger? logger = null)
    {
        _metadataFacade = metadataFacade;
        _logger = logger ?? NullLogger.Instance;
    }

    public IReadOnlyList<SimsTrayPreviewItem> BuildPageItems(
        string trayPath,
        IReadOnlyList<PreviewRowDescriptor> rows,
        int? requestedWorkerCount,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return Array.Empty<SimsTrayPreviewItem>();
        }

        var startedAt = Environment.TickCount64;
        var metadataByTrayItemPath = _metadataFacade.LoadMetadataByTrayItemPath(trayPath, rows, cancellationToken);
        var workerCount = PerformanceWorkerSizer.ResolveTrayPreviewPageWorkers(requestedWorkerCount);
        var useParallel = rows.Count >= 24 && workerCount > 1;
        _logger.LogDebug(
            "traypreview.pagebuild.start rowCount={RowCount} workerCount={WorkerCount} parallelEnabled={ParallelEnabled}",
            rows.Count,
            workerCount,
            useParallel);

        IReadOnlyList<SimsTrayPreviewItem> items;
        if (!useParallel)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items = rows
                .Select(row => CreateAggregatePreviewItem(
                    trayPath,
                    row.Group,
                    TryGetMetadata(row.Group, metadataByTrayItemPath),
                    row.ChildGroups))
                .ToList();
        }
        else
        {
            var orderedItems = new SimsTrayPreviewItem[rows.Count];
            Parallel.For(
                fromInclusive: 0,
                toExclusive: rows.Count,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = workerCount
                },
                index =>
                {
                    var row = rows[index];
                    orderedItems[index] = CreateAggregatePreviewItem(
                        trayPath,
                        row.Group,
                        TryGetMetadata(row.Group, metadataByTrayItemPath),
                        row.ChildGroups);
                });
            items = orderedItems;
        }

        _logger.LogDebug(
            "traypreview.pagebuild.done rowCount={RowCount} workerCount={WorkerCount} parallelEnabled={ParallelEnabled} elapsedMs={ElapsedMs}",
            rows.Count,
            workerCount,
            useParallel,
            Environment.TickCount64 - startedAt);
        return items;
    }

    public static SimsTrayPreviewPage BuildPage(
        IReadOnlyList<SimsTrayPreviewItem> items,
        int pageSize,
        int pageIndex)
    {
        var totalItems = items.Count;
        var normalizedPageSize = Math.Clamp(pageSize, 1, 500);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)normalizedPageSize));
        var normalizedPageIndex = Math.Clamp(pageIndex, 1, totalPages);
        var skip = (normalizedPageIndex - 1) * normalizedPageSize;

        return new SimsTrayPreviewPage
        {
            PageIndex = normalizedPageIndex,
            PageSize = normalizedPageSize,
            TotalItems = totalItems,
            TotalPages = totalPages,
            Items = items.Skip(skip).Take(normalizedPageSize).ToList()
        };
    }

    public static SimsTrayPreviewItem CreateSaveDescriptorPreviewItem(PreviewRowDescriptor row)
    {
        var entry = row.SaveDescriptorEntry ?? throw new InvalidOperationException("Missing save descriptor entry.");
        var contentFingerprint = string.Join(
            "|",
            "save-descriptor",
            row.SaveDescriptorSourcePath,
            entry.HouseholdId.ToString(CultureInfo.InvariantCulture),
            row.SaveDescriptorSourceLastWriteUtcTicks.ToString(CultureInfo.InvariantCulture),
            row.SaveDescriptorSchemaVersion);

        return new SimsTrayPreviewItem
        {
            TrayItemKey = entry.TrayItemKey,
            PresetType = "Household",
            ChildItems = Array.Empty<SimsTrayPreviewItem>(),
            DisplayTitle = entry.DisplayTitle,
            DisplaySubtitle = entry.DisplaySubtitle,
            DisplayDescription = entry.DisplayDescription,
            DisplayPrimaryMeta = entry.DisplayPrimaryMeta,
            DisplaySecondaryMeta = entry.DisplaySecondaryMeta,
            DisplayTertiaryMeta = entry.DisplayTertiaryMeta,
            DebugMetadata = new TrayDebugMetadata
            {
                TrayItemKey = entry.TrayItemKey,
                TrayInstanceId = entry.StableInstanceIdHex,
                ContentFingerprint = contentFingerprint,
                FileCount = 1,
                TotalMB = 0,
                LatestWriteTimeLocal = row.LatestWriteTimeLocal,
                Extensions = ".trayitem",
                ResourceTypes = string.Empty,
                FileListPreview = row.FileListPreview,
                SourceFilePaths = Array.Empty<string>()
            },
            TrayRootPath = string.Empty,
            TrayInstanceId = entry.StableInstanceIdHex,
            ContentFingerprint = contentFingerprint,
            SourceFilePaths = Array.Empty<string>(),
            ItemName = entry.DisplayTitle,
            AuthorId = string.Empty,
            FileCount = 1,
            TotalBytes = 0,
            TotalMB = 0,
            LatestWriteTimeLocal = row.LatestWriteTimeLocal,
            ResourceTypes = string.Empty,
            Extensions = ".trayitem",
            FileListPreview = row.FileListPreview
        };
    }

    public static PreviewRowDescriptor CreatePreviewRowDescriptor(
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
            PresetType = TrayPreviewItemUtilities.InferPresetType(orderedExtensions),
            ItemName = PreviewRowTextUtilities.ResolveDefaultTitle(group, metadata: null),
            FileListPreview = string.Join("|", previewFileNames),
            NormalizedFallbackSearchText = PreviewRowTextUtilities.BuildNormalizedSearchText(group, childGroups, metadata: null),
            FileCount = relatedGroups.Sum(entry => entry.FileCount),
            TotalBytes = relatedGroups.Sum(entry => entry.TotalBytes),
            LatestWriteTimeLocal = latestWriteTimeUtc == DateTime.MinValue
                ? DateTime.MinValue
                : latestWriteTimeUtc.ToLocalTime()
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
        var memberMetadataBySlot = PreviewRowTextUtilities.CreateMemberMetadataLookup(metadata);
        var childItems = childGroups
            .OrderBy(entry => TrayPreviewItemUtilities.TryGetAuxiliaryHouseholdMemberSlot(entry, out var slot) ? slot : int.MaxValue)
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry =>
            {
                TrayMemberDisplayMetadata? memberMetadata = null;
                if (TrayPreviewItemUtilities.TryGetAuxiliaryHouseholdMemberSlot(entry, out var slot) &&
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
            PresetType = TrayPreviewItemUtilities.InferPresetType(orderedExtensions),
            ChildItems = childItems,
            DisplayTitle = PreviewRowTextUtilities.ResolveDefaultTitle(group, metadata),
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
            ItemName = PreviewRowTextUtilities.ResolveDefaultTitle(group, metadata),
            AuthorId = PreviewRowTextUtilities.BuildAuthorSearchText(creatorName, creatorId),
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
        var fallbackTitle = PreviewRowTextUtilities.ResolveFallbackStandaloneTitle(group, isChildItem);
        var displayTitle = string.IsNullOrWhiteSpace(memberMetadata?.FullName)
            ? fallbackTitle
            : memberMetadata!.FullName;
        var displaySubtitle = memberMetadata?.Subtitle ?? string.Empty;
        var displayDescription = memberMetadata?.Detail ?? string.Empty;

        return new SimsTrayPreviewItem
        {
            TrayItemKey = group.Key,
            PresetType = isChildItem ? "Member" : TrayPreviewItemUtilities.InferPresetType(orderedExtensions),
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
}
