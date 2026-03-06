using System.Buffers;
using System.Buffers.Binary;
using System.Data.Common;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.TrayDependencyEngine;

internal sealed class PackageDependencyGraphBuilder
{
    private readonly IDbpfResourceReader _resourceReader;
    private readonly ILogger _logger;

    public PackageDependencyGraphBuilder(
        IDbpfResourceReader? resourceReader = null,
        ILogger? logger = null)
    {
        _resourceReader = resourceReader ?? new DbpfResourceReader();
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<PackageDependencyGraphBuildResult> BuildAsync(
        SqliteConnection connection,
        string modsRootPath,
        long inventoryVersion,
        IReadOnlyList<PackageGraphManifestPackage> manifestPackages,
        CancellationToken cancellationToken)
    {
        if (manifestPackages.Count == 0)
        {
            return new PackageDependencyGraphBuildResult(
                new PackageDependencyGraph
                {
                    PackageCount = 0,
                    EdgeOffsets = [0],
                    EdgeTargets = Array.Empty<int>(),
                    PackagePathsById = Array.Empty<string>()
                },
                0);
        }

        var packagePathsById = manifestPackages
            .Select(package => package.PackagePath)
            .ToArray();
        var packageIdByPath = new Dictionary<string, int>(manifestPackages.Count, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < packagePathsById.Length; index++)
        {
            packageIdByPath[packagePathsById[index]] = index;
        }

        var edges = new HashSet<long>();
        var sessions = new Dictionary<string, DbpfPackageReadSession>(StringComparer.OrdinalIgnoreCase);
        var payload = new ArrayBufferWriter<byte>();
        var siblingPayload = new ArrayBufferWriter<byte>();

        using var lookupSession = new SqliteTrayDependencyLookupSession(
            connection,
            modsRootPath,
            inventoryVersion,
            _logger,
            ownsConnection: false);

        string? currentPackagePath = null;
        var currentEntries = new List<GraphEntryRow>(64);
        int processedPackages = 0;
        try
        {
            var rows = connection.Query<GraphEntryRow>(
                """
                SELECT
                    manifest.PackagePath AS PackagePath,
                    entry.Type AS Type,
                    entry.[Group] AS [Group],
                    entry.Instance AS Instance,
                    entry.IsDeleted AS IsDeleted,
                    entry.DataOffset AS DataOffset,
                    entry.CompressedSize AS CompressedSize,
                    entry.UncompressedSize AS UncompressedSize,
                    entry.CompressionType AS CompressionType
                FROM TrayRootManifestPackage manifest
                INNER JOIN TrayRootManifest root ON root.ModsRootPath = manifest.ModsRootPath
                INNER JOIN TrayCachePackage package ON package.FingerprintKey = manifest.FingerprintKey
                INNER JOIN TrayCacheEntry entry ON entry.FingerprintKey = manifest.FingerprintKey
                WHERE manifest.ModsRootPath = @ModsRootPath
                  AND root.InventoryVersion = @InventoryVersion
                  AND package.ParseStatus = 1
                  AND entry.IsDeleted = 0
                ORDER BY manifest.PackagePath COLLATE NOCASE, entry.EntryIndex;
                """,
                new
                {
                    ModsRootPath = modsRootPath,
                    InventoryVersion = inventoryVersion
                },
                buffered: false);

            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(currentPackagePath, row.PackagePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (currentEntries.Count > 0 && currentPackagePath is not null)
                    {
                        ProcessPackageEntries(
                            currentPackagePath,
                            currentEntries,
                            packageIdByPath,
                            lookupSession,
                            sessions,
                            payload,
                            siblingPayload,
                            edges,
                            cancellationToken);
                        processedPackages++;
                        currentEntries.Clear();
                    }

                    currentPackagePath = row.PackagePath;
                }

                currentEntries.Add(row);
            }

            if (currentEntries.Count > 0 && currentPackagePath is not null)
            {
                ProcessPackageEntries(
                    currentPackagePath,
                    currentEntries,
                    packageIdByPath,
                    lookupSession,
                    sessions,
                    payload,
                    siblingPayload,
                    edges,
                    cancellationToken);
                processedPackages++;
            }
        }
        finally
        {
            foreach (var session in sessions.Values)
            {
                session.Dispose();
            }
        }

        _logger.LogInformation(
            "traygraph.build.materialized modsRoot={ModsRoot} inventoryVersion={InventoryVersion} packageCount={PackageCount} processedPackages={ProcessedPackages} edgeCount={EdgeCount}",
            modsRootPath,
            inventoryVersion,
            manifestPackages.Count,
            processedPackages,
            edges.Count);

        var edgePairs = edges
            .Select(ToPair)
            .OrderBy(edge => edge.Source)
            .ThenBy(edge => edge.Target)
            .ToArray();

        var edgeOffsets = new long[manifestPackages.Count + 1];
        var edgeTargets = new int[edgePairs.Length];
        var pairIndex = 0;
        var targetIndex = 0;
        for (var source = 0; source < manifestPackages.Count; source++)
        {
            edgeOffsets[source] = targetIndex;
            while (pairIndex < edgePairs.Length && edgePairs[pairIndex].Source == source)
            {
                edgeTargets[targetIndex++] = edgePairs[pairIndex].Target;
                pairIndex++;
            }
        }

        edgeOffsets[manifestPackages.Count] = targetIndex;
        return new PackageDependencyGraphBuildResult(
            new PackageDependencyGraph
            {
                PackageCount = manifestPackages.Count,
                EdgeOffsets = edgeOffsets,
                EdgeTargets = edgeTargets,
                PackagePathsById = packagePathsById
            },
            edgePairs.Length);
    }

    private void ProcessPackageEntries(
        string sourcePackagePath,
        IReadOnlyList<GraphEntryRow> entries,
        IReadOnlyDictionary<string, int> packageIdByPath,
        ITrayDependencyLookupSession lookupSession,
        IDictionary<string, DbpfPackageReadSession> sessions,
        ArrayBufferWriter<byte> payload,
        ArrayBufferWriter<byte> siblingPayload,
        ISet<long> edges,
        CancellationToken cancellationToken)
    {
        if (!packageIdByPath.TryGetValue(sourcePackagePath, out var sourcePackageId))
        {
            return;
        }

        var packageExactKeys = new HashSet<TrayResourceKey>();
        var packageTypedInstances = new List<TypedInstanceDependency>();
        var packageFallbackIds = new HashSet<ulong>();
        for (var index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[index];
            var resource = new ResolvedResourceRef
            {
                Key = new TrayResourceKey(entry.Type, entry.Group, entry.Instance),
                FilePath = sourcePackagePath,
                Entry = new PackageIndexEntry
                {
                    Type = entry.Type,
                    Group = entry.Group,
                    Instance = entry.Instance,
                    IsDeleted = entry.IsDeleted,
                    DataOffset = entry.DataOffset,
                    CompressedSize = entry.CompressedSize,
                    UncompressedSize = entry.UncompressedSize,
                    CompressionType = entry.CompressionType
                }
            };

            if (!TryReadPayload(resource, sessions, payload, out _))
            {
                continue;
            }

            var extraction = StructuredDependencyReaders.Read(resource.Key, payload.WrittenSpan);
            if (resource.Key.Type == KnownResourceTypes.ObjectCatalog)
            {
                var siblingKey = new TrayResourceKey(KnownResourceTypes.ObjectDefinition, resource.Key.Group, resource.Key.Instance);
                extraction.ExactKeys.Add(siblingKey);
                if (lookupSession.TryGetExact(siblingKey, out var siblingMatches) &&
                    siblingMatches.Length > 0 &&
                    TryChooseFirst(siblingMatches, out var siblingMatch) &&
                    TryReadPayload(siblingMatch, sessions, siblingPayload, out _))
                {
                    ObjectDefinitionDependencyReader.Read(siblingPayload.WrittenSpan, extraction);
                }
            }

            if (!extraction.HasAny)
            {
                BinaryReferenceScanner.Scan(payload.WrittenSpan, extraction.FallbackIds, extraction.ExactKeys);
            }

            packageExactKeys.UnionWith(extraction.ExactKeys);
            packageTypedInstances.AddRange(extraction.TypedInstances);
            packageFallbackIds.UnionWith(extraction.FallbackIds);
        }

        if (packageExactKeys.Count == 0 && packageTypedInstances.Count == 0 && packageFallbackIds.Count == 0)
        {
            return;
        }

        IReadOnlyDictionary<TrayResourceKey, ResolvedResourceRef[]>? exactBatch = null;
        IReadOnlyDictionary<TypeInstanceKey, ResolvedResourceRef[]>? typeBatch = null;
        IReadOnlyDictionary<ulong, bool>? supportedBatch = null;
        if (lookupSession is IBatchTrayDependencyLookupSession batchLookup)
        {
            var exactKeys = packageExactKeys.ToArray();
            if (exactKeys.Length > 0)
            {
                exactBatch = batchLookup.QueryExactBatch(exactKeys);
            }

            var fallbackInstances = packageFallbackIds.ToArray();
            if (fallbackInstances.Length > 0)
            {
                supportedBatch = batchLookup.QuerySupportedInstanceBatch(fallbackInstances);
            }

            var typedKeys = packageTypedInstances
                .SelectMany(typed => typed.AllowedTypes.Select(type => new TypeInstanceKey(type, typed.Instance)))
                .Concat(
                    packageFallbackIds
                        .SelectMany(instance => KnownResourceTypes.Supported.Select(type => new TypeInstanceKey(type, instance))))
                .Distinct()
                .ToArray();
            if (typedKeys.Length > 0)
            {
                typeBatch = batchLookup.QueryTypeInstanceBatch(typedKeys);
            }
        }

        foreach (var exactKey in packageExactKeys)
        {
            if (!TryResolveExact(lookupSession, exactBatch, exactKey, out var resolved))
            {
                continue;
            }

            AddEdge(sourcePackageId, resolved.FilePath, packageIdByPath, edges);
        }

        foreach (var typed in packageTypedInstances)
        {
            if (!TryResolveByTypes(lookupSession, typeBatch, typed.Instance, typed.AllowedTypes, out var resolved))
            {
                continue;
            }

            AddEdge(sourcePackageId, resolved.FilePath, packageIdByPath, edges);
        }

        foreach (var fallbackId in packageFallbackIds)
        {
            if (!TryResolveAny(lookupSession, typeBatch, supportedBatch, fallbackId, out var resolved))
            {
                continue;
            }

            AddEdge(sourcePackageId, resolved.FilePath, packageIdByPath, edges);
        }
    }

    private static void AddEdge(
        int sourcePackageId,
        string targetPackagePath,
        IReadOnlyDictionary<string, int> packageIdByPath,
        ISet<long> edges)
    {
        if (!packageIdByPath.TryGetValue(targetPackagePath, out var targetPackageId))
        {
            return;
        }

        if (sourcePackageId == targetPackageId)
        {
            return;
        }

        edges.Add(ToLong(sourcePackageId, targetPackageId));
    }

    private static bool TryResolveExact(
        ITrayDependencyLookupSession lookup,
        IReadOnlyDictionary<TrayResourceKey, ResolvedResourceRef[]>? exactBatch,
        TrayResourceKey key,
        out ResolvedResourceRef resolved)
    {
        if (exactBatch is not null)
        {
            if (exactBatch.TryGetValue(key, out var batchMatches) && batchMatches.Length > 0)
            {
                resolved = batchMatches[0];
                return true;
            }

            resolved = null!;
            return false;
        }

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
        IReadOnlyDictionary<TypeInstanceKey, ResolvedResourceRef[]>? typeBatch,
        ulong instance,
        IReadOnlyList<uint> allowedTypes,
        out ResolvedResourceRef resolved)
    {
        for (var typeIndex = 0; typeIndex < allowedTypes.Count; typeIndex++)
        {
            var key = new TypeInstanceKey(allowedTypes[typeIndex], instance);
            ResolvedResourceRef[] matches;
            if (typeBatch is not null)
            {
                if (!typeBatch.TryGetValue(key, out matches!) || matches.Length == 0)
                {
                    continue;
                }
            }
            else if (!lookup.TryGetTypeInstance(key, out matches))
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

    private static bool TryResolveAny(
        ITrayDependencyLookupSession lookup,
        IReadOnlyDictionary<TypeInstanceKey, ResolvedResourceRef[]>? typeBatch,
        IReadOnlyDictionary<ulong, bool>? supportedBatch,
        ulong instance,
        out ResolvedResourceRef resolved)
    {
        if (supportedBatch is not null)
        {
            if (!supportedBatch.TryGetValue(instance, out var exists) || !exists)
            {
                resolved = null!;
                return false;
            }
        }
        else if (!lookup.HasSupportedInstance(instance))
        {
            resolved = null!;
            return false;
        }

        foreach (var supportedType in KnownResourceTypes.Supported)
        {
            var key = new TypeInstanceKey(supportedType, instance);
            ResolvedResourceRef[] matches;
            if (typeBatch is not null)
            {
                if (!typeBatch.TryGetValue(key, out matches!) || matches.Length == 0)
                {
                    continue;
                }
            }
            else if (!lookup.TryGetTypeInstance(key, out matches))
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
        out string? error)
    {
        payload.Clear();
        error = null;

        if (resource.Entry is null)
        {
            error = "Missing package entry.";
            return false;
        }

        try
        {
            if (!sessions.TryGetValue(resource.FilePath, out var session))
            {
                session = _resourceReader.OpenSession(resource.FilePath);
                sessions[resource.FilePath] = session;
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
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }

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

    private static long ToLong(int source, int target)
    {
        return ((long)source << 32) | (uint)target;
    }

    private static (int Source, int Target) ToPair(long value)
    {
        return ((int)(value >> 32), (int)(value & 0xFFFFFFFF));
    }

    private sealed class GraphEntryRow
    {
        public string PackagePath { get; set; } = string.Empty;
        public uint Type { get; set; }
        public uint Group { get; set; }
        public ulong Instance { get; set; }
        public bool IsDeleted { get; set; }
        public long DataOffset { get; set; }
        public int CompressedSize { get; set; }
        public int UncompressedSize { get; set; }
        public ushort CompressionType { get; set; }
    }
}

internal sealed record PackageGraphManifestPackage(
    string PackagePath,
    string FingerprintKey);

internal sealed record PackageDependencyGraphBuildResult(
    PackageDependencyGraph Graph,
    int EdgeCount);
