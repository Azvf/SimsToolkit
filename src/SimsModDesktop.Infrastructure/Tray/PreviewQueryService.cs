using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Preview;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.PackageCore;
using SimsModDesktop.PackageCore.Performance;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.Infrastructure.Saves;

namespace SimsModDesktop.Infrastructure.Tray;

public sealed class PreviewQueryService : IPreviewQueryService
{
    private const int MaxCachedRootSnapshots = 8;
    private const int MaxCachedProjectedSnapshots = 32;
    private readonly TrayMetadataIndexStore _metadataIndexStore;
    private readonly ITrayPreviewRootSnapshotStore _rootSnapshotStore;
    private readonly ITrayThumbnailService _trayThumbnailService;
    private readonly ILogger<PreviewQueryService> _logger;
    private readonly IPathIdentityResolver _pathIdentityResolver;
    private readonly PreviewMetadataFacade _metadataFacade;
    private readonly PreviewPageBuilder _pageBuilder;
    private readonly PreviewProjectionEngine _projectionEngine;
    private readonly SaveDescriptorPreviewSourceReader _saveDescriptorSourceReader;
    private readonly TrayRootPreviewSourceReader _trayRootSourceReader;
    private readonly IActionInputValidator<TrayPreviewInput> _validator;
    private readonly object _cacheGate = new();
    private readonly object _thumbnailCleanupGate = new();
    private readonly Dictionary<string, RootSnapshot> _rootSnapshotCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedSnapshot> _projectedSnapshotCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task> _thumbnailCleanupTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PreviewQueryCacheEntry> _queryCache = new(StringComparer.Ordinal);

    public PreviewQueryService()
        : this(null, null, null, null, null, null, null)
    {
    }

    public PreviewQueryService(ITrayThumbnailService? trayThumbnailService)
        : this(trayThumbnailService, null, null, null, null, null, null)
    {
    }

    public PreviewQueryService(
        ITrayThumbnailService? trayThumbnailService,
        ITrayMetadataService? trayMetadataService)
        : this(trayThumbnailService, trayMetadataService, null, null, null, null, null)
    {
    }

    public PreviewQueryService(
        ITrayThumbnailService? trayThumbnailService,
        ITrayMetadataService? trayMetadataService,
        TrayMetadataIndexStore? metadataIndexStore,
        ILogger<PreviewQueryService>? logger = null,
        IPathIdentityResolver? pathIdentityResolver = null,
        ITrayPreviewRootSnapshotStore? rootSnapshotStore = null,
        ISavePreviewDescriptorStore? savePreviewDescriptorStore = null,
        IActionInputValidator<TrayPreviewInput>? validator = null)
    {
        _metadataIndexStore = metadataIndexStore ?? new TrayMetadataIndexStore();
        _rootSnapshotStore = rootSnapshotStore ?? new TrayPreviewRootSnapshotStore();
        _trayThumbnailService = trayThumbnailService ?? new TrayThumbnailService();
        var resolvedTrayMetadataService = trayMetadataService ?? new TrayMetadataService();
        var resolvedSavePreviewDescriptorStore = savePreviewDescriptorStore ?? new SavePreviewDescriptorStore();
        _logger = logger ?? NullLogger<PreviewQueryService>.Instance;
        _pathIdentityResolver = pathIdentityResolver ?? new SystemPathIdentityResolver();
        _metadataFacade = new PreviewMetadataFacade(_metadataIndexStore, resolvedTrayMetadataService);
        _pageBuilder = new PreviewPageBuilder(_metadataFacade, _logger);
        _projectionEngine = new PreviewProjectionEngine(_metadataFacade);
        _saveDescriptorSourceReader = new SaveDescriptorPreviewSourceReader(resolvedSavePreviewDescriptorStore);
        _trayRootSourceReader = new TrayRootPreviewSourceReader();
        _validator = validator ?? new TrayPreviewInputValidator();
    }

    public bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(input);

        result = null!;
        if (!_validator.TryValidate(input, out _))
        {
            return false;
        }

        var fingerprint = BuildFingerprint(input);
        lock (_cacheGate)
        {
            if (!_queryCache.TryGetValue(fingerprint, out var cacheEntry) ||
                cacheEntry.Summary is null ||
                !cacheEntry.PageCache.TryGetValue(cacheEntry.ActivePageIndex, out var cachedPage))
            {
                return false;
            }

            result = new TrayPreviewLoadResult
            {
                Summary = cacheEntry.Summary,
                Page = cachedPage,
                LoadedPageCount = cacheEntry.PageCache.Count
            };
            return true;
        }
    }

    public async Task<TrayPreviewLoadResult> LoadAsync(
        TrayPreviewInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!_validator.TryValidate(input, out var validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        var request = ToRequest(input);
        var summary = await BuildSummaryAsync(request, cancellationToken).ConfigureAwait(false);
        var firstPage = await BuildPageAsync(request, 1, cancellationToken).ConfigureAwait(false);

        lock (_cacheGate)
        {
            var fingerprint = BuildFingerprint(input);
            _queryCache[fingerprint] = new PreviewQueryCacheEntry
            {
                Input = input,
                Summary = summary,
                ActivePageIndex = firstPage.PageIndex
            };
            _queryCache[fingerprint].PageCache[firstPage.PageIndex] = firstPage;
        }

        return new TrayPreviewLoadResult
        {
            Summary = summary,
            Page = firstPage,
            LoadedPageCount = 1
        };
    }

    public async Task<TrayPreviewPageResult> LoadPageAsync(
        TrayPreviewInput input,
        int requestedPageIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!_validator.TryValidate(input, out var validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        TrayPreviewInput activeInput;
        SimsTrayPreviewSummary activeSummary;
        var fingerprint = BuildFingerprint(input);

        lock (_cacheGate)
        {
            if (!_queryCache.TryGetValue(fingerprint, out var cacheEntry))
            {
                throw new InvalidOperationException("Tray preview is not loaded yet.");
            }

            activeInput = cacheEntry.Input;
            activeSummary = cacheEntry.Summary;
            var totalPages = Math.Max(1, (int)Math.Ceiling(activeSummary.TotalItems / (double)activeInput.PageSize));
            var targetPageIndex = Math.Clamp(requestedPageIndex, 1, totalPages);
            if (cacheEntry.PageCache.TryGetValue(targetPageIndex, out var cachedPage))
            {
                cacheEntry.ActivePageIndex = cachedPage.PageIndex;
                return new TrayPreviewPageResult
                {
                    Page = cachedPage,
                    LoadedPageCount = cacheEntry.PageCache.Count,
                    FromCache = true
                };
            }
        }

        var request = ToRequest(activeInput);
        var page = await BuildPageAsync(request, requestedPageIndex, cancellationToken).ConfigureAwait(false);

        lock (_cacheGate)
        {
            if (!_queryCache.TryGetValue(fingerprint, out var cacheEntry))
            {
                throw new InvalidOperationException("Tray preview is not loaded yet.");
            }

            cacheEntry.ActivePageIndex = page.PageIndex;
            cacheEntry.PageCache[page.PageIndex] = page;
            return new TrayPreviewPageResult
            {
                Page = page,
                LoadedPageCount = cacheEntry.PageCache.Count,
                FromCache = false
            };
        }
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
            var sourceKey = NormalizePreviewSourceKey(request.PreviewSource);
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
                items = _pageBuilder.BuildPageItems(
                    sourceKey,
                    pageDescriptors,
                    request.PageBuildWorkerCount,
                    cancellationToken);
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

    public void Invalidate(PreviewSourceRef? source = null)
    {
        if (source is null)
        {
            lock (_cacheGate)
            {
                _rootSnapshotCache.Clear();
                _projectedSnapshotCache.Clear();
                _queryCache.Clear();
            }

            _metadataFacade.Invalidate();

            return;
        }

        var normalizedTrayRoot = NormalizePreviewSourceKey(source);

        lock (_cacheGate)
        {
            _rootSnapshotCache.Remove(normalizedTrayRoot);
            RemoveProjectedSnapshotsByRoot_NoLock(normalizedTrayRoot);
            foreach (var key in _queryCache
                         .Where(entry => PreviewSourcesEqual(entry.Value.Input.PreviewSource, source))
                         .Select(entry => entry.Key)
                         .ToArray())
            {
                _queryCache.Remove(key);
            }
        }
        _metadataFacade.Invalidate(normalizedTrayRoot);
    }

    public void Reset()
    {
        Invalidate();
    }

    private CachedSnapshot GetOrBuildSnapshot(
        SimsTrayPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var rootSnapshot = GetOrBuildRootSnapshot(request, cancellationToken);
        var cacheKey = PreviewProjectionEngine.BuildProjectedCacheKey(request, rootSnapshot);

        lock (_cacheGate)
        {
            if (_projectedSnapshotCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }
        }

        var built = _projectionEngine.BuildProjectedSnapshot(request, rootSnapshot, cacheKey, cancellationToken);

        lock (_cacheGate)
        {
            _projectedSnapshotCache[cacheKey] = built;

            if (_projectedSnapshotCache.Count > MaxCachedProjectedSnapshots)
            {
                var staleKeys = _projectedSnapshotCache
                    .OrderBy(pair => pair.Value.CachedAtUtc)
                    .Take(_projectedSnapshotCache.Count - MaxCachedProjectedSnapshots)
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (var staleKey in staleKeys)
                {
                    _projectedSnapshotCache.Remove(staleKey);
                }
            }

            return built;
        }
    }

    private RootSnapshot GetOrBuildRootSnapshot(
        SimsTrayPreviewRequest request,
        CancellationToken cancellationToken)
    {
        var previewSource = request.PreviewSource;
        var normalizedSourceKey = NormalizePreviewSourceKey(previewSource);
        if (previewSource.Kind == PreviewSourceKind.SaveDescriptor)
        {
            return GetOrBuildSaveDescriptorRootSnapshot(normalizedSourceKey);
        }

        var trayPath = normalizedSourceKey;
        var directoryWriteUtcTicks = Directory.GetLastWriteTimeUtc(normalizedSourceKey).Ticks;

        lock (_cacheGate)
        {
            if (_rootSnapshotCache.TryGetValue(normalizedSourceKey, out var cached) &&
                cached.DirectoryWriteUtcTicks == directoryWriteUtcTicks)
            {
                _logger.LogDebug(
                    "traypreview.rootsnapshot.hit trayRoot={TrayRoot} fingerprint={Fingerprint} rowCount={RowCount}",
                    normalizedSourceKey,
                    cached.RootFingerprint,
                    cached.RowDescriptors.Count);
                return cached;
            }
        }

        if (_rootSnapshotStore.TryLoad(normalizedSourceKey, directoryWriteUtcTicks, out var persisted))
        {
            var loaded = TrayPreviewSnapshotPersistence.CreateRootSnapshot(persisted);
            ScheduleThumbnailCleanup(trayPath, loaded.RowDescriptors.Select(row => row.Group.Key).ToArray());

            lock (_cacheGate)
            {
                _rootSnapshotCache[normalizedSourceKey] = loaded;
                RemoveProjectedSnapshotsByRoot_NoLock(normalizedSourceKey);
                EnforceRootSnapshotCacheLimit_NoLock();
            }

            _logger.LogDebug(
                "traypreview.rootsnapshot.persist.reuse trayRoot={TrayRoot} fingerprint={Fingerprint} rowCount={RowCount}",
                normalizedSourceKey,
                loaded.RootFingerprint,
                loaded.RowDescriptors.Count);
            return loaded;
        }

        var startedAt = Environment.TickCount64;
        var built = _trayRootSourceReader.Read(
            trayPath,
            normalizedSourceKey,
            directoryWriteUtcTicks,
            cancellationToken,
            ScheduleThumbnailCleanup,
            TrayPreviewSnapshotPersistence.BuildRootFingerprint);
        _rootSnapshotStore.Save(TrayPreviewSnapshotPersistence.ToRootSnapshotRecord(built));

        lock (_cacheGate)
        {
            _rootSnapshotCache[normalizedSourceKey] = built;
            RemoveProjectedSnapshotsByRoot_NoLock(normalizedSourceKey);
            EnforceRootSnapshotCacheLimit_NoLock();
        }

        _logger.LogDebug(
            "traypreview.rootsnapshot.miss trayRoot={TrayRoot} fingerprint={Fingerprint} rowCount={RowCount} elapsedMs={ElapsedMs}",
            normalizedSourceKey,
            built.RootFingerprint,
            built.RowDescriptors.Count,
            Environment.TickCount64 - startedAt);
        return built;
    }

    private void RemoveProjectedSnapshotsByRoot_NoLock(string normalizedTrayRoot)
    {
        var projectionKeyPrefix = normalizedTrayRoot + "|";
        var staleProjectionKeys = _projectedSnapshotCache.Keys
            .Where(key => key.StartsWith(projectionKeyPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var staleProjectionKey in staleProjectionKeys)
        {
            _projectedSnapshotCache.Remove(staleProjectionKey);
        }
    }

    private void EnforceRootSnapshotCacheLimit_NoLock()
    {
        if (_rootSnapshotCache.Count <= MaxCachedRootSnapshots)
        {
            return;
        }

        var staleRoots = _rootSnapshotCache
            .OrderBy(pair => pair.Value.CachedAtUtc)
            .Take(_rootSnapshotCache.Count - MaxCachedRootSnapshots)
            .Select(pair => pair.Key)
            .ToList();

        foreach (var staleRoot in staleRoots)
        {
            _rootSnapshotCache.Remove(staleRoot);
            RemoveProjectedSnapshotsByRoot_NoLock(staleRoot);
        }
    }

    private string NormalizeTrayRootPath(string trayRootPath)
    {
        var resolved = _pathIdentityResolver.ResolveDirectory(trayRootPath);
        if (!string.IsNullOrWhiteSpace(resolved.CanonicalPath))
        {
            return resolved.CanonicalPath;
        }

        if (!string.IsNullOrWhiteSpace(resolved.FullPath))
        {
            return resolved.FullPath;
        }

        return trayRootPath.Trim().Trim('"');
    }

    private string NormalizePreviewSourceKey(PreviewSourceRef previewSource)
    {
        return previewSource.Kind switch
        {
            PreviewSourceKind.TrayRoot => NormalizeTrayRootPath(previewSource.SourceKey),
            PreviewSourceKind.SaveDescriptor => NormalizeSavePath(previewSource.SourceKey),
            _ => previewSource.SourceKey.Trim()
        };
    }

    private string NormalizeSavePath(string saveFilePath)
    {
        var resolved = _pathIdentityResolver.ResolveFile(saveFilePath);
        if (!string.IsNullOrWhiteSpace(resolved.CanonicalPath))
        {
            return resolved.CanonicalPath;
        }

        if (!string.IsNullOrWhiteSpace(resolved.FullPath))
        {
            return resolved.FullPath;
        }

        return saveFilePath.Trim().Trim('"');
    }

    private RootSnapshot GetOrBuildSaveDescriptorRootSnapshot(string normalizedSavePath)
    {
        var sourceInfo = new FileInfo(normalizedSavePath);
        var sourceWriteTicks = sourceInfo.Exists ? sourceInfo.LastWriteTimeUtc.Ticks : 0;

        lock (_cacheGate)
        {
            if (_rootSnapshotCache.TryGetValue(normalizedSavePath, out var cached) &&
                cached.DirectoryWriteUtcTicks == sourceWriteTicks)
            {
                return cached;
            }
        }

        var built = _saveDescriptorSourceReader.Read(normalizedSavePath, TrayPreviewSnapshotPersistence.BuildRootFingerprint);
        lock (_cacheGate)
        {
            _rootSnapshotCache[normalizedSavePath] = built;
            RemoveProjectedSnapshotsByRoot_NoLock(normalizedSavePath);
            EnforceRootSnapshotCacheLimit_NoLock();
        }

        return built;
    }

    private void ScheduleThumbnailCleanup(string trayRootPath, IReadOnlyCollection<string> liveItemKeys)
    {
        if (string.IsNullOrWhiteSpace(trayRootPath))
        {
            return;
        }

        var normalizedTrayRoot = NormalizeTrayRootPath(trayRootPath);
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

    private static bool IsHouseholdAnchorExtension(string extension)
    {
        return string.Equals(extension, ".trayitem", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".householdbinary", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".hhi", StringComparison.OrdinalIgnoreCase);
    }

    private SimsTrayPreviewRequest ToRequest(TrayPreviewInput input)
    {
        return new SimsTrayPreviewRequest
        {
            PreviewSource = NormalizePreviewSource(input.PreviewSource),
            PageSize = input.PageSize,
            PresetTypeFilter = string.IsNullOrWhiteSpace(input.PresetTypeFilter) ? "All" : input.PresetTypeFilter.Trim(),
            BuildSizeFilter = string.IsNullOrWhiteSpace(input.BuildSizeFilter) ? "All" : input.BuildSizeFilter.Trim(),
            HouseholdSizeFilter = string.IsNullOrWhiteSpace(input.HouseholdSizeFilter) ? "All" : input.HouseholdSizeFilter.Trim(),
            AuthorFilter = input.AuthorFilter.Trim(),
            TimeFilter = string.IsNullOrWhiteSpace(input.TimeFilter) ? "All" : input.TimeFilter.Trim(),
            SearchQuery = input.SearchQuery.Trim(),
            PageBuildWorkerCount = null
        };
    }

    private string BuildFingerprint(TrayPreviewInput input)
    {
        var source = NormalizePreviewSource(input.PreviewSource);
        var sourceKey = source.SourceKey
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
        var presetTypeFilter = string.IsNullOrWhiteSpace(input.PresetTypeFilter) ? "all" : input.PresetTypeFilter.Trim().ToLowerInvariant();
        var buildSizeFilter = string.IsNullOrWhiteSpace(input.BuildSizeFilter) ? "all" : input.BuildSizeFilter.Trim().ToLowerInvariant();
        var householdSizeFilter = string.IsNullOrWhiteSpace(input.HouseholdSizeFilter) ? "all" : input.HouseholdSizeFilter.Trim().ToLowerInvariant();
        var authorFilter = string.IsNullOrWhiteSpace(input.AuthorFilter) ? "all" : input.AuthorFilter.Trim().ToLowerInvariant();
        var timeFilter = string.IsNullOrWhiteSpace(input.TimeFilter) ? "all" : input.TimeFilter.Trim().ToLowerInvariant();
        var searchQuery = string.IsNullOrWhiteSpace(input.SearchQuery) ? "all" : input.SearchQuery.Trim().ToLowerInvariant();

        return string.Join(
            "|",
            source.Kind,
            sourceKey,
            input.PageSize,
            presetTypeFilter,
            buildSizeFilter,
            householdSizeFilter,
            authorFilter,
            timeFilter,
            searchQuery);
    }

    private PreviewSourceRef NormalizePreviewSource(PreviewSourceRef source)
    {
        return source with { SourceKey = NormalizePreviewSourceKey(source) };
    }

    private bool PreviewSourcesEqual(PreviewSourceRef left, PreviewSourceRef right)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        var normalizedLeft = NormalizePreviewSource(left);
        var normalizedRight = NormalizePreviewSource(right);
        if (left.Kind == PreviewSourceKind.TrayRoot)
        {
            return _pathIdentityResolver.EqualsDirectory(normalizedLeft.SourceKey, normalizedRight.SourceKey);
        }

        return string.Equals(normalizedLeft.SourceKey, normalizedRight.SourceKey, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PreviewQueryCacheEntry
    {
        public required TrayPreviewInput Input { get; init; }
        public required SimsTrayPreviewSummary Summary { get; init; }
        public int ActivePageIndex { get; set; } = 1;
        public Dictionary<int, SimsTrayPreviewPage> PageCache { get; } = new();
    }

}

