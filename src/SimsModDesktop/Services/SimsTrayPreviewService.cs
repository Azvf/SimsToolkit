using System.Globalization;
using System.Text.RegularExpressions;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class SimsTrayPreviewService : ISimsTrayPreviewService
{
    private static readonly Regex TrayIdentityRegex = new(
        "^0x([0-9a-fA-F]{1,8})!0x([0-9a-fA-F]{1,16})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
    private const int MaxCachedSnapshots = 8;
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, CachedSnapshot> _snapshotCache = new(StringComparer.Ordinal);

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
            var totalItems = snapshot.Rows.Count;
            var pageSize = Math.Clamp(request.PageSize, 1, 500);
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
            var normalizedPageIndex = Math.Clamp(pageIndex, 1, totalPages);
            var skip = (normalizedPageIndex - 1) * pageSize;
            var items = snapshot.Rows.Skip(skip).Take(pageSize).ToList();

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

    private static CachedSnapshot BuildSnapshotCore(
        SimsTrayPreviewRequest request,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var trayPath = request.TrayPath.Trim();
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
        var groups = new Dictionary<string, GroupAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in selectedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var identity = ParseIdentity(file.Name);
            var key = identity.ParseSuccess ? identity.InstanceHex : Path.GetFileNameWithoutExtension(file.Name);
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
            }

            group.Extensions.Add(file.Extension);

            if (group.FileNames.Count < MaxPreviewFileNames)
            {
                group.FileNames.Add(file.Name);
            }

            if (string.IsNullOrWhiteSpace(group.ItemName) &&
                string.Equals(file.Extension, ".trayitem", StringComparison.OrdinalIgnoreCase))
            {
                group.ItemName = Path.GetFileNameWithoutExtension(file.Name);
            }
        }

        IEnumerable<SimsTrayPreviewItem> orderedRows = groups
            .Values
            .Select(group =>
            {
                var orderedExtensions = group.Extensions
                    .OrderBy(ext => ext, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return new SimsTrayPreviewItem
                {
                    TrayItemKey = group.Key,
                    PresetType = InferPresetType(orderedExtensions),
                    ItemName = string.IsNullOrWhiteSpace(group.ItemName) ? group.Key : group.ItemName,
                    AuthorId = string.Empty,
                    FileCount = group.FileCount,
                    TotalBytes = group.TotalBytes,
                    TotalMB = Math.Round(group.TotalBytes / (1024d * 1024d), 4),
                    LatestWriteTimeLocal = group.LatestWriteTimeUtc == DateTime.MinValue
                        ? DateTime.MinValue
                        : group.LatestWriteTimeUtc.ToLocalTime(),
                    ResourceTypes = string.Join(",", group.ResourceTypes.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)),
                    Extensions = string.Join(",", orderedExtensions),
                    FileListPreview = string.Join("|", group.FileNames)
                };
            })
            .OrderByDescending(item => item.LatestWriteTimeLocal)
            .ThenByDescending(item => item.FileCount)
            .ThenByDescending(item => item.TotalBytes)
            .ThenBy(item => item.TrayItemKey, StringComparer.OrdinalIgnoreCase);

        orderedRows = ApplyPresetTypeFilter(orderedRows, request.PresetTypeFilter);
        orderedRows = ApplyAuthorFilter(orderedRows, request.AuthorFilter);
        orderedRows = ApplyTimeFilter(orderedRows, request.TimeFilter);
        orderedRows = ApplySearchFilter(orderedRows, request.SearchQuery);

        var rows = orderedRows.ToList();
        var totalBytes = rows.Sum(item => item.TotalBytes);
        var latestWrite = rows.Count == 0
            ? DateTime.MinValue
            : rows.Max(item => item.LatestWriteTimeLocal);
        var typeBreakdown = rows
            .GroupBy(item => item.PresetType, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}:{group.Count()}")
            .ToList();

        var summary = new SimsTrayPreviewSummary
        {
            TotalItems = rows.Count,
            TotalFiles = rows.Sum(item => item.FileCount),
            TotalBytes = totalBytes,
            TotalMB = Math.Round(totalBytes / (1024d * 1024d), 2),
            LatestWriteTimeLocal = latestWrite,
            PresetTypeBreakdown = string.Join(", ", typeBreakdown)
        };

        return new CachedSnapshot
        {
            CacheKey = cacheKey,
            Rows = rows,
            Summary = summary,
            CachedAtUtc = DateTime.UtcNow
        };
    }

    private static string BuildCacheKey(SimsTrayPreviewRequest request)
    {
        var normalizedTrayPath = Path.GetFullPath(request.TrayPath.Trim()).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        var normalizedPresetTypeFilter = NormalizeFilterToken(request.PresetTypeFilter);
        var normalizedAuthorFilter = NormalizeFilterToken(request.AuthorFilter);
        var normalizedTimeFilter = NormalizeFilterToken(request.TimeFilter);
        var normalizedSearchQuery = NormalizeFilterToken(request.SearchQuery);

        return string.Join("|", normalizedTrayPath, request.PageSize, normalizedPresetTypeFilter, normalizedAuthorFilter, normalizedTimeFilter, normalizedSearchQuery);
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
            NormalizeSearch(item.TrayItemKey).Contains(needle, StringComparison.Ordinal));
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

    private sealed class CachedSnapshot
    {
        public required string CacheKey { get; init; }
        public required IReadOnlyList<SimsTrayPreviewItem> Rows { get; init; }
        public required SimsTrayPreviewSummary Summary { get; init; }
        public required DateTime CachedAtUtc { get; init; }
    }

    private sealed class GroupAccumulator
    {
        public GroupAccumulator(string key)
        {
            Key = key;
        }

        public string Key { get; }
        public string ItemName { get; set; } = string.Empty;
        public int FileCount { get; set; }
        public long TotalBytes { get; set; }
        public DateTime LatestWriteTimeUtc { get; set; } = DateTime.MinValue;
        public HashSet<string> ResourceTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Extensions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> FileNames { get; } = new();
    }

    private sealed class TrayIdentity
    {
        public bool ParseSuccess { get; init; }
        public string TypeHex { get; init; } = string.Empty;
        public string InstanceHex { get; init; } = string.Empty;
    }
}

