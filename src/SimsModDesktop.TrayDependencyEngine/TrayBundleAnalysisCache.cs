using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.TrayDependencyEngine;

public sealed class TrayBundleAnalysisCache : ITrayBundleAnalysisCache
{
    private const int DefaultMaxEntries = 64;

    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly TrayBundleLoader _bundleLoader;
    private readonly TraySearchExtractor _searchExtractor;
    private readonly IPathIdentityResolver _pathIdentityResolver;
    private readonly ILogger<TrayBundleAnalysisCache> _logger;
    private readonly int _maxEntries;

    public TrayBundleAnalysisCache(
        int maxEntries = DefaultMaxEntries,
        IPathIdentityResolver? pathIdentityResolver = null,
        ILogger<TrayBundleAnalysisCache>? logger = null)
    {
        _bundleLoader = new TrayBundleLoader();
        _searchExtractor = new TraySearchExtractor();
        _pathIdentityResolver = pathIdentityResolver ?? new SystemPathIdentityResolver();
        _logger = logger ?? NullLogger<TrayBundleAnalysisCache>.Instance;
        _maxEntries = Math.Max(1, maxEntries);
    }

    public Task<TrayBundleAnalysisResult> GetOrLoadAsync(
        string trayRootPath,
        string trayItemKey,
        IReadOnlyList<string> traySourceFiles,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedTrayRoot = NormalizeTrayRoot(trayRootPath);
        var normalizedSourceFiles = NormalizeSourceFiles(traySourceFiles);
        var cacheKey = new TrayBundleAnalysisCacheKey
        {
            CanonicalTrayRoot = normalizedTrayRoot,
            TrayItemKey = trayItemKey?.Trim() ?? string.Empty,
            ContentFingerprint = BuildContentFingerprint(normalizedTrayRoot, trayItemKey ?? string.Empty, normalizedSourceFiles)
        };
        var entryKey = BuildEntryKey(cacheKey);

        lock (_gate)
        {
            if (_entries.TryGetValue(entryKey, out var cached))
            {
                cached.LastAccessUtc = DateTime.UtcNow;
                _logger.LogDebug(
                    "traybundle.analysiscache.hit trayRoot={TrayRoot} trayItemKey={TrayItemKey} fingerprint={Fingerprint}",
                    cacheKey.CanonicalTrayRoot,
                    cacheKey.TrayItemKey,
                    cacheKey.ContentFingerprint);
                return Task.FromResult(cached.Result with { FromCache = true });
            }
        }

        var issues = new List<TrayDependencyIssue>();
        if (!_bundleLoader.TryLoad(normalizedSourceFiles, issues, out var bundle))
        {
            return Task.FromResult(new TrayBundleAnalysisResult
            {
                Success = false,
                CacheKey = cacheKey,
                Issues = issues.ToArray(),
                InputSourceFileCount = normalizedSourceFiles.Length,
                SourceFileCount = normalizedSourceFiles.Length,
                BundleFileCount = normalizedSourceFiles.Length,
                FromCache = false
            });
        }

        var searchKeys = _searchExtractor.Extract(bundle, issues);
        var result = new TrayBundleAnalysisResult
        {
            Success = true,
            CacheKey = cacheKey,
            Issues = issues.ToArray(),
            Bundle = bundle,
            SearchKeys = searchKeys,
            InputSourceFileCount = normalizedSourceFiles.Length,
            BundleTrayItemFileCount = bundle.TrayItemPaths.Count,
            BundleAuxiliaryFileCount = bundle.HhiPaths.Count +
                                      bundle.SgiPaths.Count +
                                      bundle.HouseholdBinaryPaths.Count +
                                      bundle.BlueprintPaths.Count +
                                      bundle.RoomPaths.Count,
            CandidateResourceKeyCount = searchKeys.ResourceKeys.Length,
            CandidateIdCount = CountCandidateIds(searchKeys),
            SourceFileCount = normalizedSourceFiles.Length,
            BundleFileCount = bundle.TrayItemPaths.Count +
                              bundle.HhiPaths.Count +
                              bundle.SgiPaths.Count +
                              bundle.HouseholdBinaryPaths.Count +
                              bundle.BlueprintPaths.Count +
                              bundle.RoomPaths.Count,
            FromCache = false
        };

        lock (_gate)
        {
            var staleKeys = _entries
                .Where(pair =>
                    string.Equals(pair.Value.Result.CacheKey.CanonicalTrayRoot, cacheKey.CanonicalTrayRoot, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(pair.Value.Result.CacheKey.TrayItemKey, cacheKey.TrayItemKey, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(pair.Value.Result.CacheKey.ContentFingerprint, cacheKey.ContentFingerprint, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var staleKey in staleKeys)
            {
                _entries.Remove(staleKey);
            }

            _entries[entryKey] = new CacheEntry
            {
                Result = result,
                LastAccessUtc = DateTime.UtcNow
            };

            if (_entries.Count > _maxEntries)
            {
                var removeKeys = _entries
                    .OrderBy(pair => pair.Value.LastAccessUtc)
                    .Take(_entries.Count - _maxEntries)
                    .Select(pair => pair.Key)
                    .ToArray();
                foreach (var removeKey in removeKeys)
                {
                    _entries.Remove(removeKey);
                }
            }
        }

        _logger.LogDebug(
            "traybundle.analysiscache.miss trayRoot={TrayRoot} trayItemKey={TrayItemKey} fingerprint={Fingerprint}",
            cacheKey.CanonicalTrayRoot,
            cacheKey.TrayItemKey,
            cacheKey.ContentFingerprint);
        return Task.FromResult(result);
    }

    public void Reset(string? trayRootPath = null)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(trayRootPath))
            {
                _entries.Clear();
                return;
            }

            var normalizedTrayRoot = NormalizeTrayRoot(trayRootPath);
            var staleKeys = _entries
                .Where(pair => string.Equals(
                    pair.Value.Result.CacheKey.CanonicalTrayRoot,
                    normalizedTrayRoot,
                    StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var staleKey in staleKeys)
            {
                _entries.Remove(staleKey);
            }
        }
    }

    private string NormalizeTrayRoot(string trayRootPath)
    {
        var resolved = _pathIdentityResolver.ResolveDirectory(trayRootPath ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(resolved.CanonicalPath))
        {
            return resolved.CanonicalPath;
        }

        if (!string.IsNullOrWhiteSpace(resolved.FullPath))
        {
            return resolved.FullPath;
        }

        return trayRootPath?.Trim().Trim('"') ?? string.Empty;
    }

    private string[] NormalizeSourceFiles(IReadOnlyList<string> traySourceFiles)
    {
        return traySourceFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path =>
            {
                var resolved = _pathIdentityResolver.ResolveFile(path);
                if (!string.IsNullOrWhiteSpace(resolved.CanonicalPath))
                {
                    return resolved.CanonicalPath;
                }

                if (!string.IsNullOrWhiteSpace(resolved.FullPath))
                {
                    return resolved.FullPath;
                }

                return path.Trim().Trim('"');
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildContentFingerprint(
        string normalizedTrayRoot,
        string trayItemKey,
        IReadOnlyList<string> traySourceFiles)
    {
        using var sha = SHA256.Create();
        var payload = new StringBuilder();
        payload.Append(normalizedTrayRoot).Append('\n');
        payload.Append(trayItemKey?.Trim() ?? string.Empty).Append('\n');
        foreach (var path in traySourceFiles)
        {
            payload.Append(path).Append('|');
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                payload.Append(info.Length).Append('|');
                payload.Append(info.LastWriteTimeUtc.Ticks);
            }
            else
            {
                payload.Append("missing|0");
            }

            payload.Append('\n');
        }

        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload.ToString())));
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

    private static string BuildEntryKey(TrayBundleAnalysisCacheKey key)
    {
        return key.CanonicalTrayRoot + "|" + key.TrayItemKey + "|" + key.ContentFingerprint;
    }

    private sealed class CacheEntry
    {
        public required TrayBundleAnalysisResult Result { get; init; }
        public required DateTime LastAccessUtc { get; set; }
    }
}
