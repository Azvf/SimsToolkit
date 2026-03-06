using System.Text.RegularExpressions;

namespace SimsModDesktop.Infrastructure.Tray;

internal sealed class PreviewProjectionEngine
{
    private static readonly Regex BuildSizeRegex = new(
        @"(?<!\d)(\d{1,2})\s*[xX]\s*(\d{1,2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HouseholdSizeRegex = new(
        @"(?:(\d{1,2})\s*(?:sim|sims|member|members|人|口)|(?:household|family)\s*(\d{1,2}))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly PreviewMetadataFacade _metadataFacade;

    public PreviewProjectionEngine(PreviewMetadataFacade metadataFacade)
    {
        _metadataFacade = metadataFacade;
    }

    public CachedSnapshot BuildProjectedSnapshot(
        SimsTrayPreviewRequest request,
        RootSnapshot rootSnapshot,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        IEnumerable<PreviewRowDescriptor> orderedRows = rootSnapshot.RowDescriptors;
        orderedRows = ApplyPresetTypeFilter(orderedRows, request.PresetTypeFilter);
        orderedRows = ApplyBuildSizeFilter(orderedRows, request.BuildSizeFilter);
        orderedRows = ApplyHouseholdSizeFilter(orderedRows, request.HouseholdSizeFilter);
        orderedRows = ApplyTimeFilter(orderedRows, request.TimeFilter);
        if (HasMetadataDependentFilters(request))
        {
            var preIndexedRows = orderedRows.ToList();
            _metadataFacade.EnsureMetadataIndex(rootSnapshot.NormalizedTrayRoot, preIndexedRows, cancellationToken);
            var metadataIndex = _metadataFacade.GetMetadataIndexEntries(rootSnapshot.NormalizedTrayRoot, preIndexedRows);

            orderedRows = ApplyAuthorFilter(preIndexedRows, request.AuthorFilter, metadataIndex);
            orderedRows = ApplySearchFilter(orderedRows, request.SearchQuery, metadataIndex);
        }

        var filteredRows = orderedRows.ToList();
        if (rootSnapshot.SourceKind == PreviewSourceKind.SaveDescriptor)
        {
            var materializedRows = filteredRows
                .Select(PreviewPageBuilder.CreateSaveDescriptorPreviewItem)
                .ToList();
            return new CachedSnapshot
            {
                CacheKey = cacheKey,
                RootFingerprint = rootSnapshot.RootFingerprint,
                Rows = materializedRows,
                RowDescriptors = filteredRows,
                Summary = CreateSummary(materializedRows),
                CachedAtUtc = DateTime.UtcNow
            };
        }

        return new CachedSnapshot
        {
            CacheKey = cacheKey,
            RootFingerprint = rootSnapshot.RootFingerprint,
            RowDescriptors = filteredRows,
            Summary = CreateSummary(filteredRows),
            CachedAtUtc = DateTime.UtcNow
        };
    }

    public static string BuildProjectedCacheKey(SimsTrayPreviewRequest request, RootSnapshot rootSnapshot)
    {
        var normalizedPresetTypeFilter = NormalizeFilterToken(request.PresetTypeFilter);
        var normalizedBuildSizeFilter = NormalizeFilterToken(request.BuildSizeFilter);
        var normalizedHouseholdSizeFilter = NormalizeFilterToken(request.HouseholdSizeFilter);
        var normalizedAuthorFilter = NormalizeFilterToken(request.AuthorFilter);
        var normalizedTimeFilter = NormalizeFilterToken(request.TimeFilter);
        var normalizedSearchQuery = NormalizeFilterToken(request.SearchQuery);

        return string.Join(
            "|",
            rootSnapshot.NormalizedTrayRoot.ToLowerInvariant(),
            rootSnapshot.RootFingerprint,
            normalizedPresetTypeFilter,
            normalizedBuildSizeFilter,
            normalizedHouseholdSizeFilter,
            normalizedAuthorFilter,
            normalizedTimeFilter,
            normalizedSearchQuery);
    }

    private static bool HasMetadataDependentFilters(SimsTrayPreviewRequest request)
    {
        return (!string.IsNullOrWhiteSpace(request.AuthorFilter) &&
                !string.Equals(request.AuthorFilter, "All", StringComparison.OrdinalIgnoreCase)) ||
               !string.IsNullOrWhiteSpace(request.SearchQuery);
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

    private static IEnumerable<PreviewRowDescriptor> ApplySearchFilter(
        IEnumerable<PreviewRowDescriptor> rows,
        string? searchQuery,
        IReadOnlyDictionary<string, MetadataIndexEntry> metadataIndex)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return rows;
        }

        var needle = PreviewRowTextUtilities.NormalizeSearch(searchQuery);
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

    private static bool TryParseBuildDimensions(
        string itemName,
        string fileListPreview,
        out int width,
        out int height)
    {
        width = 0;
        height = 0;

        var searchSources = new[] { itemName, fileListPreview };
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

    private static int? TryParseHouseholdSize(string itemName, string fileListPreview)
    {
        var searchSources = new[] { itemName, fileListPreview };
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

            var candidate = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
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
}
