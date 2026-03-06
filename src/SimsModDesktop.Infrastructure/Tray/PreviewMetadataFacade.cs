namespace SimsModDesktop.Infrastructure.Tray;

internal sealed class PreviewMetadataFacade
{
    private readonly TrayMetadataIndexStore _metadataIndexStore;
    private readonly ITrayMetadataService _trayMetadataService;
    private readonly object _metadataIndexGate = new();
    private readonly Dictionary<string, TrayMetadataIndexState> _metadataIndexCache = new(StringComparer.OrdinalIgnoreCase);

    public PreviewMetadataFacade(
        TrayMetadataIndexStore metadataIndexStore,
        ITrayMetadataService trayMetadataService)
    {
        _metadataIndexStore = metadataIndexStore;
        _trayMetadataService = trayMetadataService;
    }

    public IReadOnlyDictionary<string, TrayMetadataResult> LoadMetadataByTrayItemPath(
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
            var pendingSet = pendingPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var loaded = _trayMetadataService
                .GetMetadataAsync(pendingPaths, cancellationToken)
                .GetAwaiter()
                .GetResult();

            UpdateMetadataIndex(
                normalizedTrayRoot,
                rows.Where(row => pendingSet.Contains(row.Group.TrayItemPath)).ToArray(),
                loaded);
            _metadataIndexStore.Store(loaded);
        }

        return GetCachedMetadataByTrayItemPath(normalizedTrayRoot, trayItemPaths);
    }

    public void EnsureMetadataIndex(
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

        var pendingSet = pendingPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        UpdateMetadataIndex(
            normalizedTrayRoot,
            rows.Where(row => pendingSet.Contains(row.Group.TrayItemPath)).ToArray(),
            loaded);
        _metadataIndexStore.Store(loaded);
    }

    public IReadOnlyDictionary<string, MetadataIndexEntry> GetMetadataIndexEntries(
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

    public void Invalidate(string? normalizedTrayRoot = null)
    {
        lock (_metadataIndexGate)
        {
            if (string.IsNullOrWhiteSpace(normalizedTrayRoot))
            {
                _metadataIndexCache.Clear();
                return;
            }

            _metadataIndexCache.Remove(normalizedTrayRoot);
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

    private static MetadataIndexEntry CreateMetadataIndexEntry(
        PreviewRowDescriptor row,
        TrayMetadataResult? metadata)
    {
        var creatorName = string.IsNullOrWhiteSpace(metadata?.CreatorName) ? string.Empty : metadata.CreatorName;
        var creatorId = string.IsNullOrWhiteSpace(metadata?.CreatorId) ? string.Empty : metadata.CreatorId;

        return new MetadataIndexEntry
        {
            AuthorSearchText = PreviewRowTextUtilities.BuildAuthorSearchText(creatorName, creatorId),
            NormalizedSearchText = PreviewRowTextUtilities.BuildNormalizedSearchText(row.Group, row.ChildGroups, metadata)
        };
    }
}
