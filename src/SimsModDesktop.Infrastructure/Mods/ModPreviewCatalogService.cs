using SimsModDesktop.Models;
using System.Threading.Channels;

namespace SimsModDesktop.Infrastructure.Mods;

public sealed class ModPreviewCatalogService : IModPreviewCatalogService, IDisposable
{
    private readonly object _cacheGate = new();
    private CachedCatalogSnapshot? _cachedSnapshot;
    private FileSystemWatcher? _cacheWatcher;
    private const int StreamBatchSize = 96;

    public Task<IReadOnlyList<ModPreviewCatalogItem>> LoadCatalogAsync(
        ModPreviewCatalogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Task.Run(() => LoadCatalogCore(query, cancellationToken), cancellationToken);
    }

    public IAsyncEnumerable<ModPreviewCatalogPage> StreamCatalogAsync(
        ModPreviewCatalogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var channel = Channel.CreateUnbounded<ModPreviewCatalogPage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

        _ = Task.Run(() => ProduceCatalogPages(query, channel.Writer, cancellationToken), cancellationToken);
        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    private IReadOnlyList<ModPreviewCatalogItem> LoadCatalogCore(
        ModPreviewCatalogQuery query,
        CancellationToken cancellationToken)
    {
        var snapshot = GetOrCreateSnapshot(query.ModsRoot, query.BypassCache, cancellationToken);
        if (snapshot.Items.Length == 0)
        {
            return Array.Empty<ModPreviewCatalogItem>();
        }

        var filtered = snapshot.Items.Where(item => MatchesFilters(item, query));
        return ApplySort(filtered, query.SortBy);
    }

    private void ProduceCatalogPages(
        ModPreviewCatalogQuery query,
        ChannelWriter<ModPreviewCatalogPage> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query.ModsRoot) || !Directory.Exists(query.ModsRoot))
            {
                writer.TryWrite(new ModPreviewCatalogPage
                {
                    Items = Array.Empty<ModPreviewCatalogItem>(),
                    ReplaceExisting = true,
                    IsFinal = true,
                    FromCache = false,
                    ScannedCount = 0,
                    MatchedCount = 0,
                    PackageCount = 0,
                    ScriptCount = 0
                });
                writer.TryComplete();
                return;
            }

            if (TryGetCachedSnapshot(query.ModsRoot, query.BypassCache, out var cachedSnapshot))
            {
                var cachedItems = ApplySort(cachedSnapshot.Items.Where(item => MatchesFilters(item, query)), query.SortBy);
                writer.TryWrite(new ModPreviewCatalogPage
                {
                    Items = cachedItems,
                    ReplaceExisting = true,
                    IsFinal = true,
                    FromCache = true,
                    ScannedCount = cachedSnapshot.Items.Length,
                    MatchedCount = cachedItems.Count,
                    PackageCount = cachedItems.Count(item =>
                        string.Equals(item.FileExtension, ".package", StringComparison.OrdinalIgnoreCase)),
                    ScriptCount = cachedItems.Count(item =>
                        string.Equals(item.FileExtension, ".ts4script", StringComparison.OrdinalIgnoreCase))
                });
                writer.TryComplete();
                return;
            }

            var scannedFiles = new List<ScannedModFile>();
            var provisionalBatch = new List<ModPreviewCatalogItem>(StreamBatchSize);
            var matchedCount = 0;
            var packageCount = 0;
            var scriptCount = 0;

            foreach (var path in EnumerateModFiles(query.ModsRoot, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileInfo = new FileInfo(path);
                if (!fileInfo.Exists)
                {
                    continue;
                }

                var scan = new ScannedModFile(
                    FullPath: path,
                    FileName: Path.GetFileNameWithoutExtension(path),
                    RelativePath: Path.GetRelativePath(query.ModsRoot, path),
                    FileNameWithExtension: fileInfo.Name,
                    FileExtension: fileInfo.Extension,
                    FileSizeBytes: fileInfo.Length,
                    LastUpdatedLocal: fileInfo.LastWriteTime);
                scannedFiles.Add(scan);

                var provisionalItem = CreateCatalogItem(scan, duplicateCount: 1);
                if (!MatchesFilters(provisionalItem, query))
                {
                    continue;
                }

                matchedCount++;
                if (string.Equals(provisionalItem.FileExtension, ".package", StringComparison.OrdinalIgnoreCase))
                {
                    packageCount++;
                }
                else if (string.Equals(provisionalItem.FileExtension, ".ts4script", StringComparison.OrdinalIgnoreCase))
                {
                    scriptCount++;
                }

                provisionalBatch.Add(provisionalItem);
                if (provisionalBatch.Count < StreamBatchSize)
                {
                    continue;
                }

                writer.TryWrite(new ModPreviewCatalogPage
                {
                    Items = provisionalBatch.ToArray(),
                    ReplaceExisting = false,
                    IsFinal = false,
                    FromCache = false,
                    ScannedCount = scannedFiles.Count,
                    MatchedCount = matchedCount,
                    PackageCount = packageCount,
                    ScriptCount = scriptCount
                });
                provisionalBatch.Clear();
            }

            if (provisionalBatch.Count > 0)
            {
                writer.TryWrite(new ModPreviewCatalogPage
                {
                    Items = provisionalBatch.ToArray(),
                    ReplaceExisting = false,
                    IsFinal = false,
                    FromCache = false,
                    ScannedCount = scannedFiles.Count,
                    MatchedCount = matchedCount,
                    PackageCount = packageCount,
                    ScriptCount = scriptCount
                });
            }

            var snapshot = new CachedCatalogSnapshot(query.ModsRoot, BuildCatalogSnapshot(scannedFiles));
            lock (_cacheGate)
            {
                _cachedSnapshot = snapshot;
                EnsureWatcher(query.ModsRoot);
            }

            var finalItems = ApplySort(snapshot.Items.Where(item => MatchesFilters(item, query)), query.SortBy);
            writer.TryWrite(new ModPreviewCatalogPage
            {
                Items = finalItems,
                ReplaceExisting = true,
                IsFinal = true,
                FromCache = false,
                ScannedCount = snapshot.Items.Length,
                MatchedCount = finalItems.Count,
                PackageCount = finalItems.Count(item =>
                    string.Equals(item.FileExtension, ".package", StringComparison.OrdinalIgnoreCase)),
                ScriptCount = finalItems.Count(item =>
                    string.Equals(item.FileExtension, ".ts4script", StringComparison.OrdinalIgnoreCase))
            });
            writer.TryComplete();
        }
        catch (Exception ex)
        {
            writer.TryComplete(ex);
        }
    }

    private bool TryGetCachedSnapshot(string modsRoot, bool bypassCache, out CachedCatalogSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            if (!bypassCache &&
                _cachedSnapshot is not null &&
                string.Equals(_cachedSnapshot.ModsRoot, modsRoot, StringComparison.OrdinalIgnoreCase))
            {
                snapshot = _cachedSnapshot;
                return true;
            }
        }

        snapshot = CachedCatalogSnapshot.Empty;
        return false;
    }

    private CachedCatalogSnapshot GetOrCreateSnapshot(
        string modsRoot,
        bool bypassCache,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
        {
            return CachedCatalogSnapshot.Empty;
        }

        lock (_cacheGate)
        {
            if (!bypassCache &&
                _cachedSnapshot is not null &&
                string.Equals(_cachedSnapshot.ModsRoot, modsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedSnapshot;
            }
        }

        var snapshot = new CachedCatalogSnapshot(
            modsRoot,
            BuildCatalogSnapshot(EnumerateScannedModFiles(modsRoot, cancellationToken)));

        lock (_cacheGate)
        {
            if (!bypassCache &&
                _cachedSnapshot is not null &&
                string.Equals(_cachedSnapshot.ModsRoot, modsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return _cachedSnapshot;
            }

            _cachedSnapshot = snapshot;
            EnsureWatcher(modsRoot);
            return snapshot;
        }
    }

    private static IReadOnlyList<ScannedModFile> EnumerateScannedModFiles(
        string modsRoot,
        CancellationToken cancellationToken)
    {
        var scannedFiles = new List<ScannedModFile>();
        foreach (var path in EnumerateModFiles(modsRoot, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                continue;
            }

            scannedFiles.Add(new ScannedModFile(
                FullPath: path,
                FileName: Path.GetFileNameWithoutExtension(path),
                RelativePath: Path.GetRelativePath(modsRoot, path),
                FileNameWithExtension: fileInfo.Name,
                FileExtension: fileInfo.Extension,
                FileSizeBytes: fileInfo.Length,
                LastUpdatedLocal: fileInfo.LastWriteTime));
        }

        return scannedFiles;
    }

    private static ModPreviewCatalogItem[] BuildCatalogSnapshot(IReadOnlyList<ScannedModFile> scannedFiles)
    {
        var duplicateNames = scannedFiles
            .GroupBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var items = new List<ModPreviewCatalogItem>(scannedFiles.Count);
        foreach (var scan in scannedFiles)
        {
            items.Add(CreateCatalogItem(scan, duplicateNames.GetValueOrDefault(scan.FileName, 1)));
        }

        return items.ToArray();
    }

    private static ModPreviewCatalogItem CreateCatalogItem(ScannedModFile scan, int duplicateCount)
    {
        var packageType = ResolvePackageType(scan.FileNameWithExtension, scan.FileExtension);
        var scope = ResolveScope(scan.RelativePath);
        var hasConflictHint = duplicateCount > 1 ||
                              string.Equals(packageType, "Override", StringComparison.OrdinalIgnoreCase);

        return new ModPreviewCatalogItem
        {
            Key = scan.FullPath,
            DisplayTitle = scan.FileName,
            DisplaySubtitle = ResolveDisplaySubtitle(scan.RelativePath),
            PackageType = packageType,
            Scope = scope,
            RelativePath = scan.RelativePath,
            FullPath = scan.FullPath,
            FileSizeBytes = scan.FileSizeBytes,
            FileSizeText = FormatSize(scan.FileSizeBytes),
            LastUpdatedLocal = scan.LastUpdatedLocal,
            LastUpdatedText = scan.LastUpdatedLocal.ToString("yyyy-MM-dd HH:mm"),
            IsOverride = string.Equals(packageType, "Override", StringComparison.OrdinalIgnoreCase),
            HasConflictHint = hasConflictHint,
            ConflictHintText = ResolveConflictHint(packageType, duplicateCount),
            Classification = ResolveClassification(packageType, scope),
            FileExtension = scan.FileExtension
        };
    }

    private static IEnumerable<string> EnumerateModFiles(string modsRoot, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(modsRoot);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var current = pending.Pop();
            string[] childDirectories;
            string[] files;

            try
            {
                childDirectories = Directory.GetDirectories(current);
                files = Directory.GetFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var child in childDirectories)
            {
                pending.Push(child);
            }

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                if (string.Equals(extension, ".package", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".ts4script", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    private static bool MatchesFilters(ModPreviewCatalogItem item, ModPreviewCatalogQuery query)
    {
        if (query.ShowOverridesOnly && !item.IsOverride)
        {
            return false;
        }

        if (!string.Equals(query.PackageTypeFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            var matchesType =
                string.Equals(query.PackageTypeFilter, item.PackageType, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(query.PackageTypeFilter, item.FileExtension, StringComparison.OrdinalIgnoreCase);

            if (!matchesType)
            {
                return false;
            }
        }

        if (!string.Equals(query.ScopeFilter, "All", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(query.ScopeFilter, item.Scope, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var search = query.SearchQuery?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return item.DisplayTitle.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               item.DisplaySubtitle.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               item.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ModPreviewCatalogItem> ApplySort(
        IEnumerable<ModPreviewCatalogItem> items,
        string? sortBy)
    {
        return (sortBy ?? string.Empty).Trim() switch
        {
            "Name" => items
                .OrderBy(item => item.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            "Size" => items
                .OrderByDescending(item => item.FileSizeBytes)
                .ThenBy(item => item.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            _ => items
                .OrderByDescending(item => item.LastUpdatedLocal)
                .ThenBy(item => item.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static string ResolvePackageType(string fileName, string extension)
    {
        if (string.Equals(extension, ".ts4script", StringComparison.OrdinalIgnoreCase))
        {
            return "Script Mod";
        }

        return fileName.Contains("override", StringComparison.OrdinalIgnoreCase)
            ? "Override"
            : ".package";
    }

    private static string ResolveScope(string relativePath)
    {
        if (relativePath.Contains("cas", StringComparison.OrdinalIgnoreCase))
        {
            return "CAS";
        }

        if (relativePath.Contains("buildbuy", StringComparison.OrdinalIgnoreCase) ||
            relativePath.Contains("build_buy", StringComparison.OrdinalIgnoreCase) ||
            relativePath.Contains("build-buy", StringComparison.OrdinalIgnoreCase) ||
            relativePath.Contains("bb", StringComparison.OrdinalIgnoreCase))
        {
            return "Build/Buy";
        }

        return "Gameplay";
    }

    private static string ResolveDisplaySubtitle(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return segments.Length <= 1 ? "Mods root" : segments[0];
    }

    private static string ResolveConflictHint(string packageType, int duplicateCount)
    {
        if (duplicateCount > 1)
        {
            return $"Potential duplicate family ({duplicateCount})";
        }

        return string.Equals(packageType, "Override", StringComparison.OrdinalIgnoreCase)
            ? "Override file detected"
            : "No obvious conflicts";
    }

    private static string ResolveClassification(string packageType, string scope)
    {
        if (string.Equals(packageType, "Script Mod", StringComparison.OrdinalIgnoreCase))
        {
            return "Runtime script package";
        }

        if (string.Equals(packageType, "Override", StringComparison.OrdinalIgnoreCase))
        {
            return $"{scope} override package";
        }

        return $"{scope} content package";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / 1024d / 1024d:0.##} MB";
        }

        if (bytes >= 1024L)
        {
            return $"{bytes / 1024d:0.##} KB";
        }

        return $"{bytes} B";
    }

    private void EnsureWatcher(string modsRoot)
    {
        if (_cacheWatcher is not null &&
            string.Equals(_cacheWatcher.Path, modsRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DisposeWatcher();

        try
        {
            var watcher = new FileSystemWatcher(modsRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName |
                               NotifyFilters.DirectoryName |
                               NotifyFilters.LastWrite |
                               NotifyFilters.Size
            };

            watcher.Created += OnCacheFilesChanged;
            watcher.Changed += OnCacheFilesChanged;
            watcher.Deleted += OnCacheFilesChanged;
            watcher.Renamed += OnCacheFilesChanged;
            watcher.Error += OnCacheWatcherError;
            watcher.EnableRaisingEvents = true;
            _cacheWatcher = watcher;
        }
        catch
        {
            DisposeWatcher();
        }
    }

    private void OnCacheFilesChanged(object sender, FileSystemEventArgs e)
    {
        InvalidateCacheForPath(e.FullPath);
    }

    private void OnCacheWatcherError(object sender, ErrorEventArgs e)
    {
        lock (_cacheGate)
        {
            _cachedSnapshot = null;
            DisposeWatcher();
        }
    }

    private void InvalidateCacheForPath(string path)
    {
        lock (_cacheGate)
        {
            if (_cachedSnapshot is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(path) &&
                !path.StartsWith(_cachedSnapshot.ModsRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _cachedSnapshot = null;
        }
    }

    public void Dispose()
    {
        lock (_cacheGate)
        {
            _cachedSnapshot = null;
            DisposeWatcher();
        }
    }

    private void DisposeWatcher()
    {
        if (_cacheWatcher is null)
        {
            return;
        }

        _cacheWatcher.Created -= OnCacheFilesChanged;
        _cacheWatcher.Changed -= OnCacheFilesChanged;
        _cacheWatcher.Deleted -= OnCacheFilesChanged;
        _cacheWatcher.Renamed -= OnCacheFilesChanged;
        _cacheWatcher.Error -= OnCacheWatcherError;
        _cacheWatcher.Dispose();
        _cacheWatcher = null;
    }

    private sealed record CachedCatalogSnapshot(string ModsRoot, ModPreviewCatalogItem[] Items)
    {
        public static readonly CachedCatalogSnapshot Empty = new(string.Empty, Array.Empty<ModPreviewCatalogItem>());
    }

    private sealed record ScannedModFile(
        string FullPath,
        string FileName,
        string RelativePath,
        string FileNameWithExtension,
        string FileExtension,
        long FileSizeBytes,
        DateTime LastUpdatedLocal);
}
