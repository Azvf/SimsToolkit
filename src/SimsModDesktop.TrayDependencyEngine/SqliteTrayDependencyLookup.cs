using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.TrayDependencyEngine;

internal sealed class SqliteTrayDependencyLookup : ITrayDependencyLookup
{
    private readonly SqliteCacheDatabase _database;
    private readonly string _modsRootPath;
    private readonly long _inventoryVersion;
    private readonly ILogger _logger;

    public SqliteTrayDependencyLookup(SqliteCacheDatabase database, string modsRootPath, long inventoryVersion, ILogger logger)
    {
        _database = database;
        _modsRootPath = modsRootPath;
        _inventoryVersion = inventoryVersion;
        _logger = logger;
    }

    public ITrayDependencyLookupSession OpenSession()
    {
        return new SqliteTrayDependencyLookupSession(_database.OpenConnection(), _modsRootPath, _inventoryVersion, _logger);
    }
}

internal sealed class SqliteTrayDependencyLookupSession : IBatchTrayDependencyLookupSession
{
    private static readonly uint[] SupportedTypes = KnownResourceTypes.Supported;
    private const int BatchInsertChunkSize = 200;

    private readonly SqliteConnection _connection;
    private readonly string _modsRootPath;
    private readonly long _inventoryVersion;
    private readonly ILogger _logger;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly LruCache<TrayResourceKey, ResolvedResourceRef[]> _exactCache = new(4096);
    private readonly LruCache<TypeInstanceKey, ResolvedResourceRef[]> _typeInstanceCache = new(8192);
    private readonly LruCache<ulong, bool> _supportedCache = new(8192);
    private bool _batchModeDisabled;
    private bool _batchModeDisabledLogged;

    public SqliteTrayDependencyLookupSession(SqliteConnection connection, string modsRootPath, long inventoryVersion, ILogger logger)
    {
        _connection = connection;
        _modsRootPath = modsRootPath;
        _inventoryVersion = inventoryVersion;
        _logger = logger;
    }

    public bool TryGetExact(TrayResourceKey key, out ResolvedResourceRef[] matches)
    {
        if (_exactCache.TryGetValue(key, out matches))
        {
            return matches.Length > 0;
        }

        matches = QueryExact(key);
        _exactCache.Set(key, matches);
        return matches.Length > 0;
    }

    public bool TryGetTypeInstance(TypeInstanceKey key, out ResolvedResourceRef[] matches)
    {
        if (_typeInstanceCache.TryGetValue(key, out matches))
        {
            return matches.Length > 0;
        }

        matches = QueryTypeInstance(key);
        _typeInstanceCache.Set(key, matches);
        return matches.Length > 0;
    }

    public bool HasSupportedInstance(ulong instance)
    {
        if (_supportedCache.TryGetValue(instance, out var value))
        {
            return value;
        }

        var found = _connection.ExecuteScalar<long>(
            """
            SELECT COUNT(1)
            FROM TrayRootManifestPackage manifest
            INNER JOIN TrayRootManifest root ON root.ModsRootPath = manifest.ModsRootPath
            INNER JOIN TrayCacheEntry entry ON entry.FingerprintKey = manifest.FingerprintKey
            INNER JOIN TrayCachePackage package ON package.FingerprintKey = manifest.FingerprintKey
            WHERE manifest.ModsRootPath = @ModsRootPath
              AND root.InventoryVersion = @InventoryVersion
              AND package.ParseStatus = 1
              AND entry.IsDeleted = 0
              AND entry.Instance = @Instance
              AND entry.Type IN @SupportedTypes;
            """,
            new
            {
                ModsRootPath = _modsRootPath,
                InventoryVersion = _inventoryVersion,
                Instance = instance,
                SupportedTypes
            }) > 0;

        _supportedCache.Set(instance, found);
        return found;
    }

    public IReadOnlyDictionary<TrayResourceKey, ResolvedResourceRef[]> QueryExactBatch(IReadOnlyCollection<TrayResourceKey> keys)
    {
        var distinctKeys = keys
            .Distinct()
            .ToArray();
        if (distinctKeys.Length == 0)
        {
            return new Dictionary<TrayResourceKey, ResolvedResourceRef[]>();
        }

        var hitMap = new Dictionary<TrayResourceKey, ResolvedResourceRef[]>(distinctKeys.Length);
        var missedKeys = new List<TrayResourceKey>(distinctKeys.Length);
        foreach (var key in distinctKeys)
        {
            if (_exactCache.TryGetValue(key, out var cached))
            {
                hitMap[key] = cached;
                continue;
            }

            missedKeys.Add(key);
        }

        if (missedKeys.Count > 0)
        {
            var startedAt = DateTime.UtcNow;
            var usedFallback = false;
            int statementCount;
            int insertChunkCount;
            IReadOnlyDictionary<TrayResourceKey, ResolvedResourceRef[]> dbMap;
            if (_batchModeDisabled)
            {
                LogBatchDisabled();
                dbMap = QueryExactSingleFallback(missedKeys);
                statementCount = missedKeys.Count;
                insertChunkCount = 0;
                usedFallback = true;
            }
            else
            {
                try
                {
                    var coreResult = QueryExactBatchCore(missedKeys);
                    dbMap = coreResult.Map;
                    statementCount = coreResult.StatementCount;
                    insertChunkCount = coreResult.InsertChunkCount;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    DisableBatchMode("exact", missedKeys.Count, ex);
                    dbMap = QueryExactSingleFallback(missedKeys);
                    statementCount = missedKeys.Count;
                    insertChunkCount = 0;
                    usedFallback = true;
                }
            }

            foreach (var key in missedKeys)
            {
                if (!dbMap.TryGetValue(key, out var rows))
                {
                    rows = Array.Empty<ResolvedResourceRef>();
                }

                _exactCache.Set(key, rows);
                hitMap[key] = rows;
            }

            _logger.LogInformation(
                "traylookup.batch.exact keyCount={KeyCount} dbFetchCount={DbFetchCount} matchedKeyCount={MatchedKeyCount} elapsedMs={ElapsedMs} statementCount={StatementCount} insertChunkCount={InsertChunkCount} roundTripsEstimate={RoundTripsEstimate} fallback={Fallback}",
                distinctKeys.Length,
                missedKeys.Count,
                dbMap.Count(pair => pair.Value.Length > 0),
                (DateTime.UtcNow - startedAt).TotalMilliseconds,
                statementCount,
                insertChunkCount,
                statementCount,
                usedFallback);
        }

        return hitMap;
    }

    public IReadOnlyDictionary<TypeInstanceKey, ResolvedResourceRef[]> QueryTypeInstanceBatch(IReadOnlyCollection<TypeInstanceKey> keys)
    {
        var distinctKeys = keys
            .Distinct()
            .ToArray();
        if (distinctKeys.Length == 0)
        {
            return new Dictionary<TypeInstanceKey, ResolvedResourceRef[]>();
        }

        var hitMap = new Dictionary<TypeInstanceKey, ResolvedResourceRef[]>(distinctKeys.Length);
        var missedKeys = new List<TypeInstanceKey>(distinctKeys.Length);
        foreach (var key in distinctKeys)
        {
            if (_typeInstanceCache.TryGetValue(key, out var cached))
            {
                hitMap[key] = cached;
                continue;
            }

            missedKeys.Add(key);
        }

        if (missedKeys.Count > 0)
        {
            var startedAt = DateTime.UtcNow;
            var usedFallback = false;
            int statementCount;
            int insertChunkCount;
            IReadOnlyDictionary<TypeInstanceKey, ResolvedResourceRef[]> dbMap;
            if (_batchModeDisabled)
            {
                LogBatchDisabled();
                dbMap = QueryTypeInstanceSingleFallback(missedKeys);
                statementCount = missedKeys.Count;
                insertChunkCount = 0;
                usedFallback = true;
            }
            else
            {
                try
                {
                    var coreResult = QueryTypeInstanceBatchCore(missedKeys);
                    dbMap = coreResult.Map;
                    statementCount = coreResult.StatementCount;
                    insertChunkCount = coreResult.InsertChunkCount;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    DisableBatchMode("typeinstance", missedKeys.Count, ex);
                    dbMap = QueryTypeInstanceSingleFallback(missedKeys);
                    statementCount = missedKeys.Count;
                    insertChunkCount = 0;
                    usedFallback = true;
                }
            }

            foreach (var key in missedKeys)
            {
                if (!dbMap.TryGetValue(key, out var rows))
                {
                    rows = Array.Empty<ResolvedResourceRef>();
                }

                _typeInstanceCache.Set(key, rows);
                hitMap[key] = rows;
            }

            _logger.LogInformation(
                "traylookup.batch.typeinstance keyCount={KeyCount} dbFetchCount={DbFetchCount} matchedKeyCount={MatchedKeyCount} elapsedMs={ElapsedMs} statementCount={StatementCount} insertChunkCount={InsertChunkCount} roundTripsEstimate={RoundTripsEstimate} fallback={Fallback}",
                distinctKeys.Length,
                missedKeys.Count,
                dbMap.Count(pair => pair.Value.Length > 0),
                (DateTime.UtcNow - startedAt).TotalMilliseconds,
                statementCount,
                insertChunkCount,
                statementCount,
                usedFallback);
        }

        return hitMap;
    }

    public IReadOnlyDictionary<ulong, bool> QuerySupportedInstanceBatch(IReadOnlyCollection<ulong> instances)
    {
        var distinctInstances = instances
            .Distinct()
            .ToArray();
        if (distinctInstances.Length == 0)
        {
            return new Dictionary<ulong, bool>();
        }

        var hitMap = new Dictionary<ulong, bool>(distinctInstances.Length);
        var missedInstances = new List<ulong>(distinctInstances.Length);
        foreach (var instance in distinctInstances)
        {
            if (_supportedCache.TryGetValue(instance, out var cached))
            {
                hitMap[instance] = cached;
                continue;
            }

            missedInstances.Add(instance);
        }

        if (missedInstances.Count > 0)
        {
            var startedAt = DateTime.UtcNow;
            var usedFallback = false;
            int statementCount;
            int insertChunkCount;
            IReadOnlyDictionary<ulong, bool> dbMap;
            if (_batchModeDisabled)
            {
                LogBatchDisabled();
                dbMap = QuerySupportedSingleFallback(missedInstances);
                statementCount = missedInstances.Count;
                insertChunkCount = 0;
                usedFallback = true;
            }
            else
            {
                try
                {
                    var coreResult = QuerySupportedBatchCore(missedInstances);
                    dbMap = coreResult.Map;
                    statementCount = coreResult.StatementCount;
                    insertChunkCount = coreResult.InsertChunkCount;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    DisableBatchMode("supported", missedInstances.Count, ex);
                    dbMap = QuerySupportedSingleFallback(missedInstances);
                    statementCount = missedInstances.Count;
                    insertChunkCount = 0;
                    usedFallback = true;
                }
            }

            foreach (var instance in missedInstances)
            {
                var found = dbMap.TryGetValue(instance, out var matched) && matched;
                _supportedCache.Set(instance, found);
                hitMap[instance] = found;
            }

            _logger.LogInformation(
                "traylookup.batch.supported keyCount={KeyCount} dbFetchCount={DbFetchCount} matchedKeyCount={MatchedKeyCount} elapsedMs={ElapsedMs} statementCount={StatementCount} insertChunkCount={InsertChunkCount} roundTripsEstimate={RoundTripsEstimate} fallback={Fallback}",
                distinctInstances.Length,
                missedInstances.Count,
                dbMap.Count(pair => pair.Value),
                (DateTime.UtcNow - startedAt).TotalMilliseconds,
                statementCount,
                insertChunkCount,
                statementCount,
                usedFallback);
        }

        return hitMap;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private void DisableBatchMode(string method, int keyCount, Exception ex)
    {
        if (_batchModeDisabled)
        {
            return;
        }

        _batchModeDisabled = true;
        _logger.LogWarning(
            ex,
            "traylookup.batch.fallback reason={Reason} method={Method} keyCount={KeyCount} sessionId={SessionId}",
            ex.GetType().Name,
            method,
            keyCount,
            _sessionId);
        LogBatchDisabled();
    }

    private void LogBatchDisabled()
    {
        if (_batchModeDisabledLogged)
        {
            return;
        }

        _batchModeDisabledLogged = true;
        _logger.LogWarning("traylookup.batch.disabled sessionId={SessionId} inventoryVersion={InventoryVersion}", _sessionId, _inventoryVersion);
    }

    private IReadOnlyDictionary<TrayResourceKey, ResolvedResourceRef[]> QueryExactSingleFallback(IReadOnlyList<TrayResourceKey> keys)
    {
        var map = new Dictionary<TrayResourceKey, ResolvedResourceRef[]>(keys.Count);
        foreach (var key in keys)
        {
            map[key] = QueryExact(key);
        }

        return map;
    }

    private IReadOnlyDictionary<TypeInstanceKey, ResolvedResourceRef[]> QueryTypeInstanceSingleFallback(IReadOnlyList<TypeInstanceKey> keys)
    {
        var map = new Dictionary<TypeInstanceKey, ResolvedResourceRef[]>(keys.Count);
        foreach (var key in keys)
        {
            map[key] = QueryTypeInstance(key);
        }

        return map;
    }

    private IReadOnlyDictionary<ulong, bool> QuerySupportedSingleFallback(IReadOnlyList<ulong> instances)
    {
        var map = new Dictionary<ulong, bool>(instances.Count);
        foreach (var instance in instances)
        {
            map[instance] = HasSupportedInstance(instance);
        }

        return map;
    }

    private ResolvedResourceRef[] QueryExact(TrayResourceKey key)
    {
        return _connection.Query<LookupRow>(
                """
                SELECT
                    manifest.PackagePath AS FilePath,
                    entry.Type,
                    entry.[Group],
                    entry.Instance,
                    entry.IsDeleted,
                    entry.DataOffset,
                    entry.CompressedSize,
                    entry.UncompressedSize,
                    entry.CompressionType
                FROM TrayRootManifestPackage manifest
                INNER JOIN TrayRootManifest root ON root.ModsRootPath = manifest.ModsRootPath
                INNER JOIN TrayCacheEntry entry ON entry.FingerprintKey = manifest.FingerprintKey
                INNER JOIN TrayCachePackage package ON package.FingerprintKey = manifest.FingerprintKey
                WHERE manifest.ModsRootPath = @ModsRootPath
                  AND root.InventoryVersion = @InventoryVersion
                  AND package.ParseStatus = 1
                  AND entry.IsDeleted = 0
                  AND entry.Type = @Type
                  AND entry.[Group] = @Group
                  AND entry.Instance = @Instance
                ORDER BY manifest.PackagePath COLLATE NOCASE, entry.EntryIndex;
                """,
                new
                {
                    ModsRootPath = _modsRootPath,
                    InventoryVersion = _inventoryVersion,
                    key.Type,
                    key.Group,
                    key.Instance
                })
            .Select(ToResolved)
            .ToArray();
    }

    private ResolvedResourceRef[] QueryTypeInstance(TypeInstanceKey key)
    {
        return _connection.Query<LookupRow>(
                """
                SELECT
                    manifest.PackagePath AS FilePath,
                    entry.Type,
                    entry.[Group],
                    entry.Instance,
                    entry.IsDeleted,
                    entry.DataOffset,
                    entry.CompressedSize,
                    entry.UncompressedSize,
                    entry.CompressionType
                FROM TrayRootManifestPackage manifest
                INNER JOIN TrayRootManifest root ON root.ModsRootPath = manifest.ModsRootPath
                INNER JOIN TrayCacheEntry entry ON entry.FingerprintKey = manifest.FingerprintKey
                INNER JOIN TrayCachePackage package ON package.FingerprintKey = manifest.FingerprintKey
                WHERE manifest.ModsRootPath = @ModsRootPath
                  AND root.InventoryVersion = @InventoryVersion
                  AND package.ParseStatus = 1
                  AND entry.IsDeleted = 0
                  AND entry.Type = @Type
                  AND entry.Instance = @Instance
                ORDER BY manifest.PackagePath COLLATE NOCASE, entry.EntryIndex;
                """,
                new
                {
                    ModsRootPath = _modsRootPath,
                    InventoryVersion = _inventoryVersion,
                    key.Type,
                    key.Instance
                })
            .Select(ToResolved)
            .ToArray();
    }

    private BatchResourceLookupQueryResult<TrayResourceKey> QueryExactBatchCore(IReadOnlyList<TrayResourceKey> keys)
    {
        var statementCount = 0;
        var insertChunkCount = 0;
        _connection.Execute(
            """
            DROP TABLE IF EXISTS TempLookupExact;
            CREATE TEMP TABLE TempLookupExact (
                [Type] INTEGER NOT NULL,
                [Group] INTEGER NOT NULL,
                [Instance] INTEGER NOT NULL,
                [Ordinal] INTEGER NOT NULL,
                PRIMARY KEY ([Type], [Group], [Instance])
            );
            """);
        statementCount++;
        foreach (var chunk in ChunkBatch(keys, BatchInsertChunkSize))
        {
            var sql = new StringBuilder("INSERT INTO TempLookupExact ([Type], [Group], [Instance], [Ordinal]) VALUES ");
            var parameters = new DynamicParameters();
            for (var i = 0; i < chunk.Length; i++)
            {
                if (i > 0)
                {
                    sql.Append(", ");
                }

                sql.Append($"(@Type{i}, @Group{i}, @Instance{i}, @Ordinal{i})");
                var key = chunk[i];
                parameters.Add($"Type{i}", key.Type);
                parameters.Add($"Group{i}", key.Group);
                parameters.Add($"Instance{i}", key.Instance);
                parameters.Add($"Ordinal{i}", insertChunkCount * BatchInsertChunkSize + i);
            }

            _connection.Execute(sql.ToString(), parameters);
            statementCount++;
            insertChunkCount++;
        }

        var rows = _connection.Query<BatchLookupRow>(
            """
            SELECT
                key.[Type] AS KeyType,
                key.[Group] AS KeyGroup,
                key.[Instance] AS KeyInstance,
                manifest.PackagePath AS FilePath,
                entry.Type,
                entry.[Group],
                entry.Instance,
                entry.IsDeleted,
                entry.DataOffset,
                entry.CompressedSize,
                entry.UncompressedSize,
                entry.CompressionType
            FROM TempLookupExact key
            INNER JOIN TrayCacheEntry entry
                ON entry.Type = key.[Type]
               AND entry.[Group] = key.[Group]
               AND entry.Instance = key.[Instance]
            INNER JOIN TrayRootManifestPackage manifest
                ON manifest.FingerprintKey = entry.FingerprintKey
            INNER JOIN TrayRootManifest root
                ON root.ModsRootPath = manifest.ModsRootPath
            INNER JOIN TrayCachePackage package
                ON package.FingerprintKey = manifest.FingerprintKey
            WHERE manifest.ModsRootPath = @ModsRootPath
              AND root.InventoryVersion = @InventoryVersion
              AND package.ParseStatus = 1
              AND entry.IsDeleted = 0
            ORDER BY key.[Ordinal], manifest.PackagePath COLLATE NOCASE, entry.EntryIndex;
            """,
            new
            {
                ModsRootPath = _modsRootPath,
                InventoryVersion = _inventoryVersion
            });
        statementCount++;
        _connection.Execute("DROP TABLE IF EXISTS TempLookupExact;");
        statementCount++;
        var map = rows
            .GroupBy(row => new TrayResourceKey(row.KeyType, row.KeyGroup, row.KeyInstance))
            .ToDictionary(
                group => group.Key,
                group => group.Select(ToResolved).ToArray());
        return new BatchResourceLookupQueryResult<TrayResourceKey>(map, statementCount, insertChunkCount);
    }

    private BatchResourceLookupQueryResult<TypeInstanceKey> QueryTypeInstanceBatchCore(IReadOnlyList<TypeInstanceKey> keys)
    {
        var statementCount = 0;
        var insertChunkCount = 0;
        _connection.Execute(
            """
            DROP TABLE IF EXISTS TempLookupTypeInstance;
            CREATE TEMP TABLE TempLookupTypeInstance (
                [Type] INTEGER NOT NULL,
                [Instance] INTEGER NOT NULL,
                [Ordinal] INTEGER NOT NULL,
                PRIMARY KEY ([Type], [Instance])
            );
            """);
        statementCount++;
        foreach (var chunk in ChunkBatch(keys, BatchInsertChunkSize))
        {
            var sql = new StringBuilder("INSERT INTO TempLookupTypeInstance ([Type], [Instance], [Ordinal]) VALUES ");
            var parameters = new DynamicParameters();
            for (var i = 0; i < chunk.Length; i++)
            {
                if (i > 0)
                {
                    sql.Append(", ");
                }

                sql.Append($"(@Type{i}, @Instance{i}, @Ordinal{i})");
                var key = chunk[i];
                parameters.Add($"Type{i}", key.Type);
                parameters.Add($"Instance{i}", key.Instance);
                parameters.Add($"Ordinal{i}", insertChunkCount * BatchInsertChunkSize + i);
            }

            _connection.Execute(sql.ToString(), parameters);
            statementCount++;
            insertChunkCount++;
        }

        var rows = _connection.Query<BatchTypeInstanceLookupRow>(
            """
            SELECT
                key.[Type] AS KeyType,
                key.[Instance] AS KeyInstance,
                manifest.PackagePath AS FilePath,
                entry.Type,
                entry.[Group],
                entry.Instance,
                entry.IsDeleted,
                entry.DataOffset,
                entry.CompressedSize,
                entry.UncompressedSize,
                entry.CompressionType
            FROM TempLookupTypeInstance key
            INNER JOIN TrayCacheEntry entry
                ON entry.Type = key.[Type]
               AND entry.Instance = key.[Instance]
            INNER JOIN TrayRootManifestPackage manifest
                ON manifest.FingerprintKey = entry.FingerprintKey
            INNER JOIN TrayRootManifest root
                ON root.ModsRootPath = manifest.ModsRootPath
            INNER JOIN TrayCachePackage package
                ON package.FingerprintKey = manifest.FingerprintKey
            WHERE manifest.ModsRootPath = @ModsRootPath
              AND root.InventoryVersion = @InventoryVersion
              AND package.ParseStatus = 1
              AND entry.IsDeleted = 0
            ORDER BY key.[Ordinal], manifest.PackagePath COLLATE NOCASE, entry.EntryIndex;
            """,
            new
            {
                ModsRootPath = _modsRootPath,
                InventoryVersion = _inventoryVersion
            });
        statementCount++;
        _connection.Execute("DROP TABLE IF EXISTS TempLookupTypeInstance;");
        statementCount++;
        var map = rows
            .GroupBy(row => new TypeInstanceKey(row.KeyType, row.KeyInstance))
            .ToDictionary(
                group => group.Key,
                group => group.Select(ToResolved).ToArray());
        return new BatchResourceLookupQueryResult<TypeInstanceKey>(map, statementCount, insertChunkCount);
    }

    private BatchSupportedLookupQueryResult QuerySupportedBatchCore(IReadOnlyList<ulong> instances)
    {
        var statementCount = 0;
        var insertChunkCount = 0;
        _connection.Execute(
            """
            DROP TABLE IF EXISTS TempLookupSupportedInstance;
            CREATE TEMP TABLE TempLookupSupportedInstance (
                [Instance] INTEGER NOT NULL PRIMARY KEY
            );
            """);
        statementCount++;
        foreach (var chunk in ChunkBatch(instances, 400))
        {
            var sql = new StringBuilder("INSERT INTO TempLookupSupportedInstance ([Instance]) VALUES ");
            var parameters = new DynamicParameters();
            for (var i = 0; i < chunk.Length; i++)
            {
                if (i > 0)
                {
                    sql.Append(", ");
                }

                sql.Append($"(@Instance{i})");
                parameters.Add($"Instance{i}", chunk[i]);
            }

            _connection.Execute(sql.ToString(), parameters);
            statementCount++;
            insertChunkCount++;
        }

        var rows = _connection.Query<SupportedLookupRow>(
            """
            SELECT
                input.Instance,
                CASE WHEN EXISTS (
                    SELECT 1
                    FROM TrayRootManifestPackage manifest
                    INNER JOIN TrayRootManifest root ON root.ModsRootPath = manifest.ModsRootPath
                    INNER JOIN TrayCacheEntry entry ON entry.FingerprintKey = manifest.FingerprintKey
                    INNER JOIN TrayCachePackage package ON package.FingerprintKey = manifest.FingerprintKey
                    WHERE manifest.ModsRootPath = @ModsRootPath
                      AND root.InventoryVersion = @InventoryVersion
                      AND package.ParseStatus = 1
                      AND entry.IsDeleted = 0
                      AND entry.Instance = input.Instance
                      AND entry.Type IN @SupportedTypes
                ) THEN 1 ELSE 0 END AS HasMatch
            FROM TempLookupSupportedInstance input;
            """,
            new
            {
                ModsRootPath = _modsRootPath,
                InventoryVersion = _inventoryVersion,
                SupportedTypes
            });
        statementCount++;
        _connection.Execute("DROP TABLE IF EXISTS TempLookupSupportedInstance;");
        statementCount++;
        var map = rows.ToDictionary(row => row.Instance, row => row.HasMatch != 0);
        return new BatchSupportedLookupQueryResult(map, statementCount, insertChunkCount);
    }

    private static ResolvedResourceRef ToResolved(LookupRow row)
    {
        return new ResolvedResourceRef
        {
            Key = new TrayResourceKey(row.Type, row.Group, row.Instance),
            FilePath = row.FilePath,
            Entry = new PackageIndexEntry
            {
                Type = row.Type,
                Group = row.Group,
                Instance = row.Instance,
                IsDeleted = row.IsDeleted,
                DataOffset = row.DataOffset,
                CompressedSize = row.CompressedSize,
                UncompressedSize = row.UncompressedSize,
                CompressionType = row.CompressionType
            }
        };
    }

    private static ResolvedResourceRef ToResolved(BatchLookupRow row)
    {
        return ToResolved(new LookupRow
        {
            FilePath = row.FilePath,
            Type = row.Type,
            Group = row.Group,
            Instance = row.Instance,
            IsDeleted = row.IsDeleted,
            DataOffset = row.DataOffset,
            CompressedSize = row.CompressedSize,
            UncompressedSize = row.UncompressedSize,
            CompressionType = row.CompressionType
        });
    }

    private static ResolvedResourceRef ToResolved(BatchTypeInstanceLookupRow row)
    {
        return ToResolved(new LookupRow
        {
            FilePath = row.FilePath,
            Type = row.Type,
            Group = row.Group,
            Instance = row.Instance,
            IsDeleted = row.IsDeleted,
            DataOffset = row.DataOffset,
            CompressedSize = row.CompressedSize,
            UncompressedSize = row.UncompressedSize,
            CompressionType = row.CompressionType
        });
    }

    private static IReadOnlyList<T[]> ChunkBatch<T>(IReadOnlyCollection<T> items, int chunkSize)
    {
        if (items.Count == 0)
        {
            return Array.Empty<T[]>();
        }

        var materialized = items as T[] ?? items.ToArray();
        var chunks = new List<T[]>((materialized.Length + chunkSize - 1) / chunkSize);
        for (var offset = 0; offset < materialized.Length; offset += chunkSize)
        {
            var size = Math.Min(chunkSize, materialized.Length - offset);
            var chunk = new T[size];
            Array.Copy(materialized, offset, chunk, 0, size);
            chunks.Add(chunk);
        }

        return chunks;
    }

    private sealed record BatchResourceLookupQueryResult<TKey>(
        IReadOnlyDictionary<TKey, ResolvedResourceRef[]> Map,
        int StatementCount,
        int InsertChunkCount)
        where TKey : notnull;

    private sealed record BatchSupportedLookupQueryResult(
        IReadOnlyDictionary<ulong, bool> Map,
        int StatementCount,
        int InsertChunkCount);

    private sealed class LookupRow
    {
        public string FilePath { get; set; } = string.Empty;
        public uint Type { get; set; }
        public uint Group { get; set; }
        public ulong Instance { get; set; }
        public bool IsDeleted { get; set; }
        public long DataOffset { get; set; }
        public int CompressedSize { get; set; }
        public int UncompressedSize { get; set; }
        public ushort CompressionType { get; set; }
    }

    private sealed class BatchLookupRow
    {
        public uint KeyType { get; set; }
        public uint KeyGroup { get; set; }
        public ulong KeyInstance { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public uint Type { get; set; }
        public uint Group { get; set; }
        public ulong Instance { get; set; }
        public bool IsDeleted { get; set; }
        public long DataOffset { get; set; }
        public int CompressedSize { get; set; }
        public int UncompressedSize { get; set; }
        public ushort CompressionType { get; set; }
    }

    private sealed class BatchTypeInstanceLookupRow
    {
        public uint KeyType { get; set; }
        public ulong KeyInstance { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public uint Type { get; set; }
        public uint Group { get; set; }
        public ulong Instance { get; set; }
        public bool IsDeleted { get; set; }
        public long DataOffset { get; set; }
        public int CompressedSize { get; set; }
        public int UncompressedSize { get; set; }
        public ushort CompressionType { get; set; }
    }

    private sealed class SupportedLookupRow
    {
        public ulong Instance { get; set; }
        public int HasMatch { get; set; }
    }
}
