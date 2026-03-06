using Dapper;
using System.Diagnostics;
using SimsModDesktop.Application.Caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Infrastructure.Persistence;

namespace SimsModDesktop.Infrastructure.Mods;

public sealed class SqliteModItemIndexStore : IModItemIndexStore
{
    private const int SchemaVersion = 5;
    private const string CountCacheDomain = "modindex.count";
    private const string PageCacheDomain = "modindex.page";
    private readonly AppCacheDatabase _database;
    private readonly ILogger<SqliteModItemIndexStore> _logger;
    private readonly IListQueryCache? _queryCache;

    public SqliteModItemIndexStore(
        ILogger<SqliteModItemIndexStore>? logger = null,
        IListQueryCache? queryCache = null)
        : this(new AppCacheDatabase(), logger, queryCache)
    {
    }

    public SqliteModItemIndexStore(
        string cacheRootPath,
        ILogger<SqliteModItemIndexStore>? logger = null,
        IListQueryCache? queryCache = null)
        : this(new AppCacheDatabase(cacheRootPath), logger, queryCache)
    {
    }

    private SqliteModItemIndexStore(
        AppCacheDatabase database,
        ILogger<SqliteModItemIndexStore>? logger,
        IListQueryCache? queryCache)
    {
        _database = database;
        _logger = logger ?? NullLogger<SqliteModItemIndexStore>.Instance;
        _queryCache = queryCache;
    }

    public Task<ModPackageIndexState?> TryGetPackageStateAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenConnection();
        var row = connection.QuerySingleOrDefault<PackageStateRow>(
            "SELECT * FROM ModPackageIndexState WHERE PackagePath = @PackagePath LIMIT 1;",
            new { PackagePath = NormalizePath(packagePath) });
        return Task.FromResult(row is null ? null : ToPackageState(row));
    }

    public Task ReplacePackagesFastAsync(IReadOnlyList<ModItemFastIndexBuildResult> buildResults, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (buildResults.Count == 0)
        {
            return Task.CompletedTask;
        }

        var startedAt = Stopwatch.StartNew();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var packagePaths = buildResults
            .Select(result => result.PackageState.PackagePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        DeletePackageRowsBulk(connection, transaction, packagePaths);
        UpsertPackageStatesBulk(connection, transaction, buildResults.Select(result => result.PackageState));

        var allItems = buildResults
            .SelectMany(result => result.Items)
            .ToArray();
        if (allItems.Length > 0)
        {
            connection.Execute(
                InsertItemSql,
                allItems.Select(CreateItemParameters),
                transaction);
            ReplaceItemSearchRowsBulk(connection, transaction, allItems);
            ReplaceTexturesBulk(connection, transaction, allItems, deleteExistingPerItemRows: false);
        }

        transaction.Commit();
        InvalidateQueryCache();
        startedAt.Stop();
        _logger.LogInformation(
            "modindex.write.setbatch Stage={Stage} PackageCount={PackageCount} ItemCount={ItemCount} TextureCount={TextureCount} ElapsedMs={ElapsedMs}",
            "fast",
            packagePaths.Length,
            allItems.Length,
            allItems.Sum(item => item.TextureCandidates.Count),
            startedAt.ElapsedMilliseconds);
        return Task.CompletedTask;
    }

    public Task ReplacePackageFastAsync(ModItemFastIndexBuildResult buildResult, CancellationToken cancellationToken = default)
    {
        return ReplacePackagesFastAsync([buildResult], cancellationToken);
    }

    public Task ApplyItemEnrichmentBatchesAsync(IReadOnlyList<ModItemEnrichmentBatch> batches, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (batches.Count == 0)
        {
            return Task.CompletedTask;
        }

        var startedAt = Stopwatch.StartNew();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        UpsertPackageStatesBulk(connection, transaction, batches.Select(batch => batch.PackageState));

        var allItems = batches
            .SelectMany(batch => batch.Items)
            .ToArray();
        if (allItems.Length > 0)
        {
            connection.Execute(
                UpsertItemSql,
                allItems.Select(CreateItemParameters),
                transaction);
            ReplaceItemSearchRowsBulk(connection, transaction, allItems);
            ReplaceTexturesBulk(connection, transaction, allItems, deleteExistingPerItemRows: true);
        }

        transaction.Commit();
        InvalidateQueryCache();
        startedAt.Stop();
        _logger.LogInformation(
            "modindex.write.setbatch Stage={Stage} PackageCount={PackageCount} ItemCount={ItemCount} TextureCount={TextureCount} ElapsedMs={ElapsedMs}",
            "deep",
            batches.Count,
            allItems.Length,
            allItems.Sum(item => item.TextureCandidates.Count),
            startedAt.ElapsedMilliseconds);
        return Task.CompletedTask;
    }

    public Task ApplyItemEnrichmentBatchAsync(ModItemEnrichmentBatch batch, CancellationToken cancellationToken = default)
    {
        return ApplyItemEnrichmentBatchesAsync([batch], cancellationToken);
    }

    public Task ReplacePackageAsync(ModItemIndexBuildResult buildResult, CancellationToken cancellationToken = default)
    {
        return ReplacePackageCoreAsync(buildResult.PackageState, buildResult.Items, cancellationToken);
    }

    public Task DeletePackageAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        DeletePackageRows(connection, transaction, NormalizePath(packagePath));
        transaction.Commit();
        InvalidateQueryCache();
        return Task.CompletedTask;
    }

    public Task<ModItemCatalogPage> QueryPageAsync(ModItemCatalogQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Max(1, query.PageSize);
        var offset = (pageIndex - 1) * pageSize;
        var ftsQuery = BuildFtsQuery(query.SearchQuery);
        var kindFilter = NormalizeFilter(query.EntityKindFilter);
        var subTypeFilter = NormalizeFilter(query.SubTypeFilter);
        var rootPrefix = string.IsNullOrWhiteSpace(query.ModsRoot) ? null : $"{NormalizePath(query.ModsRoot)}%";
        _logger.LogDebug(
            "modsearch.fts.querybuild mode={Mode} rawSearchLength={RawSearchLength} tokenCount={TokenCount} ftsPattern={FtsPattern} kindFilter={KindFilter} subTypeFilter={SubTypeFilter} hasRootPrefix={HasRootPrefix} sortBy={SortBy}",
            ftsQuery is null ? "browse" : "fts",
            query.SearchQuery?.Length ?? 0,
            CountFtsTokens(ftsQuery),
            ftsQuery ?? string.Empty,
            kindFilter ?? string.Empty,
            subTypeFilter ?? string.Empty,
            rootPrefix is not null,
            query.SortBy);
        var parameters = new
        {
            RootPrefix = rootPrefix,
            KindFilter = kindFilter,
            SubTypeFilter = subTypeFilter,
            SearchPattern = ftsQuery,
            PageSize = pageSize,
            Offset = offset
        };
        var countCacheKey = new QueryCountCacheKey(
            rootPrefix ?? string.Empty,
            kindFilter ?? string.Empty,
            subTypeFilter ?? string.Empty,
            ftsQuery ?? string.Empty);
        var pageCacheKey = BuildPageCacheKey(
            rootPrefix,
            kindFilter,
            subTypeFilter,
            ftsQuery,
            query.SortBy,
            pageIndex,
            pageSize);

        if (TryGetCachedPage(pageCacheKey, out var cachedPage))
        {
            _logger.LogDebug(
                "modindex.querycache.hit pageKey={PageKey} pageIndex={PageIndex} pageSize={PageSize}",
                pageCacheKey,
                pageIndex,
                pageSize);
            return Task.FromResult(cachedPage);
        }

        using var connection = OpenConnection();

        int totalItems;
        IEnumerable<IndexedItemRow> rows;
        if (ftsQuery is null)
        {
            const string whereClause = """
                WHERE (@RootPrefix IS NULL OR items.PackagePath LIKE @RootPrefix)
                  AND (@KindFilter IS NULL OR items.EntityKind = @KindFilter)
                  AND (@SubTypeFilter IS NULL OR items.EntitySubType = @SubTypeFilter)
                """;
            if (!TryGetCachedCount(countCacheKey, out totalItems))
            {
                totalItems = connection.ExecuteScalar<int>(
                    $"""
                    SELECT COUNT(*)
                    FROM ModIndexedItems items
                    {whereClause};
                    """,
                    parameters);
                SetCachedCount(countCacheKey, totalItems);
            }

            LogQueryPlan(
                connection,
                $"""
                SELECT items.*
                FROM ModIndexedItems items
                {whereClause}
                ORDER BY {ResolveOrderBy(query.SortBy)}
                LIMIT @PageSize OFFSET @Offset;
                """,
                parameters,
                "browse");

            rows = connection.Query<IndexedItemRow>(
                $"""
                SELECT items.*
                FROM ModIndexedItems items
                {whereClause}
                ORDER BY {ResolveOrderBy(query.SortBy)}
                LIMIT @PageSize OFFSET @Offset;
                """,
                parameters);
        }
        else
        {
            const string whereClause = """
                WHERE search.SearchText MATCH @SearchPattern
                  AND (@RootPrefix IS NULL OR items.PackagePath LIKE @RootPrefix)
                  AND (@KindFilter IS NULL OR items.EntityKind = @KindFilter)
                  AND (@SubTypeFilter IS NULL OR items.EntitySubType = @SubTypeFilter)
                """;
            if (!TryGetCachedCount(countCacheKey, out totalItems))
            {
                totalItems = connection.ExecuteScalar<int>(
                    $"""
                    SELECT COUNT(*)
                    FROM ModIndexedItems items
                    INNER JOIN ModIndexedItemsSearch search ON search.ItemKey = items.ItemKey
                    {whereClause};
                    """,
                    parameters);
                SetCachedCount(countCacheKey, totalItems);
            }

            LogQueryPlan(
                connection,
                $"""
                SELECT items.*
                FROM ModIndexedItems items
                INNER JOIN ModIndexedItemsSearch search ON search.ItemKey = items.ItemKey
                {whereClause}
                ORDER BY {ResolveOrderBy(query.SortBy)}
                LIMIT @PageSize OFFSET @Offset;
                """,
                parameters,
                "fts");

            rows = connection.Query<IndexedItemRow>(
                $"""
                SELECT items.*
                FROM ModIndexedItems items
                INNER JOIN ModIndexedItemsSearch search ON search.ItemKey = items.ItemKey
                {whereClause}
                ORDER BY {ResolveOrderBy(query.SortBy)}
                LIMIT @PageSize OFFSET @Offset;
                """,
                parameters);
        }

        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        var page = new ModItemCatalogPage
        {
            Items = rows.Select(ToListRow).ToArray(),
            TotalItems = totalItems,
            PageIndex = Math.Min(pageIndex, totalPages),
            PageSize = pageSize,
            TotalPages = totalPages
        };
        SetCachedPage(pageCacheKey, page);
        _logger.LogDebug(
            "modindex.querycache.miss pageKey={PageKey} pageIndex={PageIndex} pageSize={PageSize}",
            pageCacheKey,
            pageIndex,
            pageSize);
        return Task.FromResult(page);
    }

    public Task<ModItemInspectDetail?> TryGetInspectAsync(string itemKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenConnection();
        var item = connection.QuerySingleOrDefault<IndexedItemRow>(
            "SELECT * FROM ModIndexedItems WHERE ItemKey = @ItemKey LIMIT 1;",
            new { ItemKey = itemKey.Trim() });

        if (item is null)
        {
            return Task.FromResult<ModItemInspectDetail?>(null);
        }

        var textures = connection.Query<IndexedTextureRow>(
            "SELECT * FROM ModIndexedItemTextures WHERE ItemKey = @ItemKey ORDER BY SortOrder ASC;",
            new { ItemKey = item.ItemKey })
            .Select(ToTextureCandidate)
            .ToArray();

        var state = connection.QuerySingleOrDefault<PackageStateRow>(
            "SELECT * FROM ModPackageIndexState WHERE PackagePath = @PackagePath LIMIT 1;",
            new { item.PackagePath });

        return Task.FromResult<ModItemInspectDetail?>(new ModItemInspectDetail
        {
            ItemKey = item.ItemKey,
            DisplayName = item.DisplayName,
            EntityKind = item.EntityKind,
            EntitySubType = item.EntitySubType,
            PackagePath = item.PackagePath,
            SourceResourceKey = item.SourceResourceKey,
            SourceGroupText = item.SourceGroupText,
            UpdatedUtcTicks = item.UpdatedUtcTicks,
            HasTextureData = item.HasTextureData != 0,
            PrimaryTextureResourceKey = item.PrimaryTextureResourceKey,
            UnclassifiedEntityCountForPackage = state?.UnclassifiedEntityCount ?? 0,
            TextureCount = item.TextureCount,
            EditableTextureCount = item.EditableTextureCount,
            PartNameRaw = item.PartNameRaw,
            DisplayNameSource = item.DisplayNameSource,
            TitleKey = item.TitleKey is null ? null : (uint)item.TitleKey.Value,
            PartDescriptionKey = item.PartDescriptionKey is null ? null : (uint)item.PartDescriptionKey.Value,
            BodyTypeText = item.BodyTypeText,
            AgeGenderText = item.AgeGenderText,
            SpeciesText = item.SpeciesText,
            OutfitId = item.OutfitId is null ? null : (uint)item.OutfitId.Value,
            DisplayStage = ParseDisplayStage(item.DisplayStage),
            ThumbnailStage = ParseThumbnailStage(item.ThumbnailStage),
            TextureStage = ParseTextureStage(item.TextureStage),
            PendingDeepRefresh = item.PendingDeepRefresh != 0,
            TextureRows = textures
        });
    }

    public Task<int> CountIndexedPackagesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenConnection();
        return Task.FromResult(connection.ExecuteScalar<int>("SELECT COUNT(*) FROM ModPackageIndexState;"));
    }

    private Task ReplacePackageCoreAsync(
        ModPackageIndexState packageState,
        IReadOnlyList<ModIndexedItemRecord> items,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        DeletePackageRows(connection, transaction, packageState.PackagePath);
        UpsertPackageState(connection, transaction, packageState);
        foreach (var item in items)
        {
            InsertItem(connection, transaction, item);
            ReplaceTextures(connection, transaction, item);
        }

        transaction.Commit();
        InvalidateQueryCache();
        return Task.CompletedTask;
    }

    private System.Data.IDbConnection OpenConnection()
    {
        var connection = _database.OpenConnection();
        EnsureSchema(connection);
        return connection;
    }

    private static void DeletePackageRows(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, string packagePath)
    {
        connection.Execute(
            """
            DELETE FROM ModIndexedItemsSearch WHERE ItemKey IN (
                SELECT ItemKey FROM ModIndexedItems WHERE PackagePath = @PackagePath
            );
            DELETE FROM ModIndexedItemTextures WHERE ItemKey IN (
                SELECT ItemKey FROM ModIndexedItems WHERE PackagePath = @PackagePath
            );
            DELETE FROM ModIndexedItems WHERE PackagePath = @PackagePath;
            DELETE FROM ModPackageIndexState WHERE PackagePath = @PackagePath;
            """,
            new { PackagePath = NormalizePath(packagePath) },
            transaction);
    }

    private static void DeletePackageRowsBulk(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        IReadOnlyList<string> packagePaths)
    {
        if (packagePaths.Count == 0)
        {
            return;
        }

        foreach (var chunk in Chunk(packagePaths, 200))
        {
            connection.Execute(
                """
                DELETE FROM ModIndexedItemsSearch WHERE ItemKey IN (
                    SELECT ItemKey FROM ModIndexedItems WHERE PackagePath IN @PackagePaths
                );
                DELETE FROM ModIndexedItemTextures WHERE ItemKey IN (
                    SELECT ItemKey FROM ModIndexedItems WHERE PackagePath IN @PackagePaths
                );
                DELETE FROM ModIndexedItems WHERE PackagePath IN @PackagePaths;
                DELETE FROM ModPackageIndexState WHERE PackagePath IN @PackagePaths;
                """,
                new { PackagePaths = chunk },
                transaction);
        }
    }

    private static void UpsertPackageStatesBulk(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        IEnumerable<ModPackageIndexState> packageStates)
    {
        var parameters = packageStates
            .Select(packageState => new
            {
                PackagePath = NormalizePath(packageState.PackagePath),
                packageState.FileLength,
                packageState.LastWriteUtcTicks,
                packageState.PackageType,
                packageState.ScopeHint,
                packageState.IndexedUtcTicks,
                packageState.ItemCount,
                packageState.CasItemCount,
                packageState.BuildBuyItemCount,
                packageState.UnclassifiedEntityCount,
                packageState.TextureResourceCount,
                packageState.EditableTextureCount,
                packageState.Status,
                packageState.FailureMessage
            })
            .ToArray();
        if (parameters.Length == 0)
        {
            return;
        }

        connection.Execute(
            """
            INSERT INTO ModPackageIndexState (
                PackagePath, FileLength, LastWriteUtcTicks, PackageType, ScopeHint, IndexedUtcTicks, ItemCount,
                CasItemCount, BuildBuyItemCount, UnclassifiedEntityCount, TextureResourceCount, EditableTextureCount,
                Status, FailureMessage
            ) VALUES (
                @PackagePath, @FileLength, @LastWriteUtcTicks, @PackageType, @ScopeHint, @IndexedUtcTicks, @ItemCount,
                @CasItemCount, @BuildBuyItemCount, @UnclassifiedEntityCount, @TextureResourceCount, @EditableTextureCount,
                @Status, @FailureMessage
            )
            ON CONFLICT(PackagePath) DO UPDATE SET
                FileLength = excluded.FileLength,
                LastWriteUtcTicks = excluded.LastWriteUtcTicks,
                PackageType = excluded.PackageType,
                ScopeHint = excluded.ScopeHint,
                IndexedUtcTicks = excluded.IndexedUtcTicks,
                ItemCount = excluded.ItemCount,
                CasItemCount = excluded.CasItemCount,
                BuildBuyItemCount = excluded.BuildBuyItemCount,
                UnclassifiedEntityCount = excluded.UnclassifiedEntityCount,
                TextureResourceCount = excluded.TextureResourceCount,
                EditableTextureCount = excluded.EditableTextureCount,
                Status = excluded.Status,
                FailureMessage = excluded.FailureMessage;
            """,
            parameters,
            transaction);
    }

    private void ReplaceItemSearchRowsBulk(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        IReadOnlyList<ModIndexedItemRecord> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        var dedupedRows = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            dedupedRows[item.ItemKey] = item.SearchText;
        }

        _logger.LogDebug(
            "modindex.search.dedup inputCount={InputCount} dedupedCount={DedupedCount} duplicateCount={DuplicateCount}",
            items.Count,
            dedupedRows.Count,
            items.Count - dedupedRows.Count);

        var itemKeys = dedupedRows.Keys
            .ToArray();
        foreach (var chunk in Chunk(itemKeys, 400))
        {
            connection.Execute(
                "DELETE FROM ModIndexedItemsSearch WHERE ItemKey IN @ItemKeys;",
                new { ItemKeys = chunk },
                transaction);
        }

        connection.Execute(
            """
            INSERT INTO ModIndexedItemsSearch (ItemKey, SearchText)
            VALUES (@ItemKey, @SearchText);
            """,
            dedupedRows.Select(item => new
            {
                ItemKey = item.Key,
                SearchText = item.Value
            }),
            transaction);
    }

    private static void ReplaceTexturesBulk(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        IReadOnlyList<ModIndexedItemRecord> items,
        bool deleteExistingPerItemRows)
    {
        if (items.Count == 0)
        {
            return;
        }

        if (deleteExistingPerItemRows)
        {
            var itemKeys = items
                .Select(item => item.ItemKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var chunk in Chunk(itemKeys, 400))
            {
                connection.Execute(
                    "DELETE FROM ModIndexedItemTextures WHERE ItemKey IN @ItemKeys;",
                    new { ItemKeys = chunk },
                    transaction);
            }
        }

        var textureRows = BuildTextureRows(items);
        if (textureRows.Length == 0)
        {
            return;
        }

        connection.Execute(
            """
            INSERT INTO ModIndexedItemTextures (
                ItemKey, ResourceKeyText, ContainerKind, Format, Width, Height, MipMapCount, Editable,
                SuggestedAction, Notes, SizeBytes, IsPrimary, SortOrder, LinkRole
            ) VALUES (
                @ItemKey, @ResourceKeyText, @ContainerKind, @Format, @Width, @Height, @MipMapCount, @Editable,
                @SuggestedAction, @Notes, @SizeBytes, @IsPrimary, @SortOrder, @LinkRole
            );
            """,
            textureRows,
            transaction);
    }

    private static BulkTextureRow[] BuildTextureRows(IReadOnlyList<ModIndexedItemRecord> items)
    {
        var rows = new List<BulkTextureRow>();
        var dedupeIndexes = new Dictionary<TextureDedupKey, int>();
        foreach (var item in items)
        {
            for (var index = 0; index < item.TextureCandidates.Count; index++)
            {
                var texture = item.TextureCandidates[index];
                var row = new BulkTextureRow
                {
                    ItemKey = item.ItemKey,
                    ResourceKeyText = texture.ResourceKeyText,
                    ContainerKind = texture.ContainerKind,
                    Format = texture.Format,
                    Width = texture.Width,
                    Height = texture.Height,
                    MipMapCount = texture.MipMapCount,
                    Editable = texture.Editable ? 1 : 0,
                    SuggestedAction = texture.SuggestedAction,
                    Notes = texture.Notes,
                    SizeBytes = texture.SizeBytes,
                    IsPrimary = string.Equals(texture.ResourceKeyText, item.PrimaryTextureResourceKey, StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                    SortOrder = index,
                    LinkRole = string.IsNullOrWhiteSpace(texture.LinkRole) ? "Fallback" : texture.LinkRole
                };
                var dedupeKey = new TextureDedupKey(row.ItemKey, row.ResourceKeyText);
                if (dedupeIndexes.TryGetValue(dedupeKey, out var existingIndex))
                {
                    rows[existingIndex] = row;
                    continue;
                }

                dedupeIndexes[dedupeKey] = rows.Count;
                rows.Add(row);
            }
        }

        return rows.ToArray();
    }

    private static void UpsertPackageState(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, ModPackageIndexState packageState)
    {
        connection.Execute(
            """
            INSERT INTO ModPackageIndexState (
                PackagePath, FileLength, LastWriteUtcTicks, PackageType, ScopeHint, IndexedUtcTicks, ItemCount,
                CasItemCount, BuildBuyItemCount, UnclassifiedEntityCount, TextureResourceCount, EditableTextureCount,
                Status, FailureMessage
            ) VALUES (
                @PackagePath, @FileLength, @LastWriteUtcTicks, @PackageType, @ScopeHint, @IndexedUtcTicks, @ItemCount,
                @CasItemCount, @BuildBuyItemCount, @UnclassifiedEntityCount, @TextureResourceCount, @EditableTextureCount,
                @Status, @FailureMessage
            )
            ON CONFLICT(PackagePath) DO UPDATE SET
                FileLength = excluded.FileLength,
                LastWriteUtcTicks = excluded.LastWriteUtcTicks,
                PackageType = excluded.PackageType,
                ScopeHint = excluded.ScopeHint,
                IndexedUtcTicks = excluded.IndexedUtcTicks,
                ItemCount = excluded.ItemCount,
                CasItemCount = excluded.CasItemCount,
                BuildBuyItemCount = excluded.BuildBuyItemCount,
                UnclassifiedEntityCount = excluded.UnclassifiedEntityCount,
                TextureResourceCount = excluded.TextureResourceCount,
                EditableTextureCount = excluded.EditableTextureCount,
                Status = excluded.Status,
                FailureMessage = excluded.FailureMessage;
            """,
            new
            {
                PackagePath = NormalizePath(packageState.PackagePath),
                packageState.FileLength,
                packageState.LastWriteUtcTicks,
                packageState.PackageType,
                packageState.ScopeHint,
                packageState.IndexedUtcTicks,
                packageState.ItemCount,
                packageState.CasItemCount,
                packageState.BuildBuyItemCount,
                packageState.UnclassifiedEntityCount,
                packageState.TextureResourceCount,
                packageState.EditableTextureCount,
                packageState.Status,
                packageState.FailureMessage
            },
            transaction);
    }

    private static void InsertItem(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, ModIndexedItemRecord item)
    {
        connection.Execute(InsertItemSql, CreateItemParameters(item), transaction);
        ReplaceItemSearchRow(connection, transaction, item);
    }

    private static void UpsertItem(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, ModIndexedItemRecord item)
    {
        connection.Execute(UpsertItemSql, CreateItemParameters(item), transaction);
        ReplaceItemSearchRow(connection, transaction, item);
    }

    private static object CreateItemParameters(ModIndexedItemRecord item)
    {
        return new
        {
            item.ItemKey,
            PackagePath = NormalizePath(item.PackagePath),
            item.PackageFingerprintLength,
            item.PackageFingerprintLastWriteUtcTicks,
            item.EntityKind,
            item.EntitySubType,
            item.DisplayName,
            item.SortName,
            item.SearchText,
            item.ScopeText,
            item.ThumbnailStatus,
            item.PrimaryTextureResourceKey,
            item.PrimaryTextureFormat,
            item.PrimaryTextureWidth,
            item.PrimaryTextureHeight,
            item.TextureCount,
            item.EditableTextureCount,
            HasTextureData = item.HasTextureData ? 1 : 0,
            item.SourceResourceKey,
            item.SourceGroupText,
            item.CreatedUtcTicks,
            item.UpdatedUtcTicks,
            item.PartNameRaw,
            DisplayNameSource = string.IsNullOrWhiteSpace(item.DisplayNameSource) ? "Fallback" : item.DisplayNameSource,
            TitleKey = item.TitleKey is uint titleKey ? (long?)titleKey : null,
            PartDescriptionKey = item.PartDescriptionKey is uint descriptionKey ? (long?)descriptionKey : null,
            BodyTypeNumeric = item.BodyTypeNumeric is uint bodyType ? (long?)bodyType : null,
            item.BodyTypeText,
            BodySubTypeNumeric = item.BodySubTypeNumeric is uint bodySubType ? (long?)bodySubType : null,
            AgeGenderFlags = item.AgeGenderFlags is uint ageGender ? (long?)ageGender : null,
            item.AgeGenderText,
            SpeciesNumeric = item.SpeciesNumeric is uint species ? (long?)species : null,
            item.SpeciesText,
            OutfitId = item.OutfitId is uint outfitId ? (long?)outfitId : null,
            SortKeyStable = string.IsNullOrWhiteSpace(item.SortKeyStable) ? item.SortName : item.SortKeyStable,
            DisplayStage = item.DisplayStage.ToString(),
            ThumbnailStage = item.ThumbnailStage.ToString(),
            TextureStage = item.TextureStage.ToString(),
            item.ThumbnailCacheKey,
            LastFastParsedUtcTicks = item.LastFastParsedUtcTicks == 0 ? item.UpdatedUtcTicks : item.LastFastParsedUtcTicks,
            item.LastDeepParsedUtcTicks,
            PendingDeepRefresh = item.PendingDeepRefresh ? 1 : 0
        };
    }

    private static void ReplaceTextures(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, ModIndexedItemRecord item)
    {
        connection.Execute(
            "DELETE FROM ModIndexedItemTextures WHERE ItemKey = @ItemKey;",
            new { item.ItemKey },
            transaction);

        for (var index = 0; index < item.TextureCandidates.Count; index++)
        {
            var texture = item.TextureCandidates[index];
            connection.Execute(
                """
                INSERT INTO ModIndexedItemTextures (
                    ItemKey, ResourceKeyText, ContainerKind, Format, Width, Height, MipMapCount, Editable,
                    SuggestedAction, Notes, SizeBytes, IsPrimary, SortOrder, LinkRole
                ) VALUES (
                    @ItemKey, @ResourceKeyText, @ContainerKind, @Format, @Width, @Height, @MipMapCount, @Editable,
                    @SuggestedAction, @Notes, @SizeBytes, @IsPrimary, @SortOrder, @LinkRole
                );
                """,
                new
                {
                    item.ItemKey,
                    texture.ResourceKeyText,
                    texture.ContainerKind,
                    texture.Format,
                    texture.Width,
                    texture.Height,
                    texture.MipMapCount,
                    Editable = texture.Editable ? 1 : 0,
                    texture.SuggestedAction,
                    texture.Notes,
                    texture.SizeBytes,
                    IsPrimary = string.Equals(texture.ResourceKeyText, item.PrimaryTextureResourceKey, StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                    SortOrder = index,
                    LinkRole = string.IsNullOrWhiteSpace(texture.LinkRole) ? "Fallback" : texture.LinkRole
                },
                transaction);
        }
    }

    private static ModPackageIndexState ToPackageState(PackageStateRow row)
    {
        return new ModPackageIndexState
        {
            PackagePath = row.PackagePath,
            FileLength = row.FileLength,
            LastWriteUtcTicks = row.LastWriteUtcTicks,
            PackageType = row.PackageType,
            ScopeHint = row.ScopeHint,
            IndexedUtcTicks = row.IndexedUtcTicks,
            ItemCount = row.ItemCount,
            CasItemCount = row.CasItemCount,
            BuildBuyItemCount = row.BuildBuyItemCount,
            UnclassifiedEntityCount = row.UnclassifiedEntityCount,
            TextureResourceCount = row.TextureResourceCount,
            EditableTextureCount = row.EditableTextureCount,
            Status = row.Status,
            FailureMessage = row.FailureMessage
        };
    }

    private static ModItemListRow ToListRow(IndexedItemRow row)
    {
        var sizeText = row.PrimaryTextureWidth is int width && row.PrimaryTextureHeight is int height
            ? $"{width} x {height}"
            : row.TextureStage == nameof(ModItemTextureStage.Pending)
                ? "Pending"
                : "No texture";
        var displayStage = ParseDisplayStage(row.DisplayStage);
        var thumbnailStage = ParseThumbnailStage(row.ThumbnailStage);
        var textureStage = ParseTextureStage(row.TextureStage);

        return new ModItemListRow
        {
            ItemKey = row.ItemKey,
            DisplayName = row.DisplayName,
            EntityKind = row.EntityKind,
            EntitySubType = row.EntitySubType,
            PackagePath = row.PackagePath,
            PackageName = Path.GetFileNameWithoutExtension(row.PackagePath),
            ScopeText = row.ScopeText,
            ThumbnailStatus = row.ThumbnailStatus,
            TextureCount = row.TextureCount,
            EditableTextureCount = row.EditableTextureCount,
            TextureSummaryText = textureStage == ModItemTextureStage.Pending
                ? "Analyzing textures..."
                : $"Textures {row.TextureCount} | Editable {row.EditableTextureCount} | {sizeText}",
            PrimaryTextureResourceKey = row.PrimaryTextureResourceKey,
            PrimaryTextureFormat = row.PrimaryTextureFormat,
            PrimaryTextureWidth = row.PrimaryTextureWidth,
            PrimaryTextureHeight = row.PrimaryTextureHeight,
            BodyTypeText = row.BodyTypeText,
            AgeGenderText = row.AgeGenderText,
            DisplayNameSource = row.DisplayNameSource,
            UpdatedUtcTicks = row.UpdatedUtcTicks,
            DisplayStage = displayStage,
            ThumbnailStage = thumbnailStage,
            TextureStage = textureStage,
            HasDeepData = displayStage != ModItemDisplayStage.Fast || textureStage == ModItemTextureStage.Ready,
            ShowThumbnailPlaceholder = thumbnailStage != ModItemThumbnailStage.Ready,
            ShowMetadataPlaceholder = displayStage == ModItemDisplayStage.Fast,
            StableSubtitleText = row.EntityKind.Equals("Cas", StringComparison.OrdinalIgnoreCase)
                ? $"CAS | {row.BodyTypeText ?? row.EntitySubType}"
                : $"{row.EntityKind} | {row.EntitySubType}",
            SortKeyStable = row.SortKeyStable
        };
    }

    private static ModPackageTextureCandidate ToTextureCandidate(IndexedTextureRow row)
    {
        return new ModPackageTextureCandidate
        {
            ResourceKeyText = row.ResourceKeyText,
            ContainerKind = row.ContainerKind,
            Format = row.Format,
            Width = row.Width,
            Height = row.Height,
            MipMapCount = row.MipMapCount,
            Editable = row.Editable != 0,
            SuggestedAction = row.SuggestedAction,
            Notes = row.Notes,
            SizeBytes = row.SizeBytes,
            LinkRole = row.LinkRole
        };
    }

    private static string? NormalizeFilter(string value)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, "All", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path.Trim());
    }

    private bool TryGetCachedCount(QueryCountCacheKey key, out int value)
    {
        if (_queryCache is not null)
        {
            return _queryCache.TryGet<int>(CountCacheDomain, BuildCountCacheKey(key), out value);
        }

        value = default;
        return false;
    }

    private void SetCachedCount(QueryCountCacheKey key, int value)
    {
        _queryCache?.Set(CountCacheDomain, BuildCountCacheKey(key), value);
    }

    private bool TryGetCachedPage(string key, out ModItemCatalogPage page)
    {
        if (_queryCache is not null)
        {
            if (_queryCache.TryGet(PageCacheDomain, key, out ModItemCatalogPage? cachedPage) &&
                cachedPage is not null)
            {
                page = cachedPage;
                return true;
            }
        }

        page = default!;
        return false;
    }

    private void SetCachedPage(string key, ModItemCatalogPage page)
    {
        _queryCache?.Set(PageCacheDomain, key, page);
    }

    private void InvalidateQueryCache()
    {
        _queryCache?.InvalidateDomain(CountCacheDomain);
        _queryCache?.InvalidateDomain(PageCacheDomain);
    }

    private void LogQueryPlan(System.Data.IDbConnection connection, string sql, object parameters, string mode)
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var plan = connection.Query<QueryPlanRow>(
                $"EXPLAIN QUERY PLAN {sql}",
                parameters)
            .Select(row => row.Detail)
            .Where(detail => !string.IsNullOrWhiteSpace(detail))
            .ToArray();
        if (plan.Length == 0)
        {
            return;
        }

        _logger.LogDebug(
            "modsearch.queryplan mode={Mode} detail={PlanDetail}",
            mode,
            string.Join(" | ", plan));
    }

    private static IReadOnlyList<T[]> Chunk<T>(IReadOnlyCollection<T> items, int chunkSize)
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

    private static string ResolveOrderBy(string? sortBy)
    {
        return (sortBy ?? string.Empty).Trim() switch
        {
            "Package" => "items.PackagePath ASC, items.SortKeyStable ASC",
            "Name" => "items.SortKeyStable ASC, items.SortName ASC",
            _ => "items.SortKeyStable ASC, items.UpdatedUtcTicks DESC"
        };
    }

    private static ModItemDisplayStage ParseDisplayStage(string? value)
    {
        return Enum.TryParse<ModItemDisplayStage>(value, true, out var parsed)
            ? parsed
            : ModItemDisplayStage.Fast;
    }

    private static ModItemThumbnailStage ParseThumbnailStage(string? value)
    {
        return Enum.TryParse<ModItemThumbnailStage>(value, true, out var parsed)
            ? parsed
            : ModItemThumbnailStage.None;
    }

    private static ModItemTextureStage ParseTextureStage(string? value)
    {
        return Enum.TryParse<ModItemTextureStage>(value, true, out var parsed)
            ? parsed
            : ModItemTextureStage.Pending;
    }

    private static void EnsureSchema(System.Data.IDbConnection connection)
    {
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS ModItemIndexSchemaMeta (
                SchemaVersion INTEGER NOT NULL
            );
            """);

        var version = connection.QuerySingleOrDefault<int?>("SELECT SchemaVersion FROM ModItemIndexSchemaMeta LIMIT 1;");
        if (version != SchemaVersion)
        {
            connection.Execute(
                """
                DROP TABLE IF EXISTS ModIndexedItemTextures;
                DROP TABLE IF EXISTS ModIndexedItemsSearch;
                DROP TABLE IF EXISTS ModIndexedItems;
                DROP TABLE IF EXISTS ModPackageIndexState;
                DELETE FROM ModItemIndexSchemaMeta;
                """);
        }

        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS ModPackageIndexState (
                PackagePath TEXT PRIMARY KEY,
                FileLength INTEGER NOT NULL,
                LastWriteUtcTicks INTEGER NOT NULL,
                PackageType TEXT NOT NULL,
                ScopeHint TEXT NOT NULL,
                IndexedUtcTicks INTEGER NOT NULL,
                ItemCount INTEGER NOT NULL,
                CasItemCount INTEGER NOT NULL,
                BuildBuyItemCount INTEGER NOT NULL,
                UnclassifiedEntityCount INTEGER NOT NULL,
                TextureResourceCount INTEGER NOT NULL,
                EditableTextureCount INTEGER NOT NULL,
                Status TEXT NOT NULL,
                FailureMessage TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS ModIndexedItems (
                ItemKey TEXT PRIMARY KEY,
                PackagePath TEXT NOT NULL,
                PackageFingerprintLength INTEGER NOT NULL,
                PackageFingerprintLastWriteUtcTicks INTEGER NOT NULL,
                EntityKind TEXT NOT NULL,
                EntitySubType TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                SortName TEXT NOT NULL,
                SearchText TEXT NOT NULL,
                ScopeText TEXT NOT NULL,
                ThumbnailStatus TEXT NOT NULL,
                PrimaryTextureResourceKey TEXT NULL,
                PrimaryTextureFormat TEXT NULL,
                PrimaryTextureWidth INTEGER NULL,
                PrimaryTextureHeight INTEGER NULL,
                TextureCount INTEGER NOT NULL,
                EditableTextureCount INTEGER NOT NULL,
                HasTextureData INTEGER NOT NULL,
                SourceResourceKey TEXT NOT NULL,
                SourceGroupText TEXT NOT NULL,
                CreatedUtcTicks INTEGER NOT NULL,
                UpdatedUtcTicks INTEGER NOT NULL,
                PartNameRaw TEXT NULL,
                DisplayNameSource TEXT NOT NULL,
                TitleKey INTEGER NULL,
                PartDescriptionKey INTEGER NULL,
                BodyTypeNumeric INTEGER NULL,
                BodyTypeText TEXT NULL,
                BodySubTypeNumeric INTEGER NULL,
                AgeGenderFlags INTEGER NULL,
                AgeGenderText TEXT NULL,
                SpeciesNumeric INTEGER NULL,
                SpeciesText TEXT NULL,
                OutfitId INTEGER NULL,
                SortKeyStable TEXT NOT NULL,
                DisplayStage TEXT NOT NULL,
                ThumbnailStage TEXT NOT NULL,
                TextureStage TEXT NOT NULL,
                ThumbnailCacheKey TEXT NULL,
                LastFastParsedUtcTicks INTEGER NOT NULL,
                LastDeepParsedUtcTicks INTEGER NULL,
                PendingDeepRefresh INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ModIndexedItemTextures (
                ItemKey TEXT NOT NULL,
                ResourceKeyText TEXT NOT NULL,
                ContainerKind TEXT NOT NULL,
                Format TEXT NOT NULL,
                Width INTEGER NOT NULL,
                Height INTEGER NOT NULL,
                MipMapCount INTEGER NOT NULL,
                Editable INTEGER NOT NULL,
                SuggestedAction TEXT NOT NULL,
                Notes TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL,
                IsPrimary INTEGER NOT NULL,
                SortOrder INTEGER NOT NULL,
                LinkRole TEXT NOT NULL,
                PRIMARY KEY (ItemKey, ResourceKeyText)
            );

            CREATE VIRTUAL TABLE IF NOT EXISTS ModIndexedItemsSearch USING fts5(
                ItemKey UNINDEXED,
                SearchText,
                tokenize = 'unicode61 remove_diacritics 2'
            );

            CREATE INDEX IF NOT EXISTS IX_ModIndexedItems_PackagePath ON ModIndexedItems (PackagePath);
            CREATE INDEX IF NOT EXISTS IX_ModIndexedItems_EntityKind_SubType ON ModIndexedItems (EntityKind, EntitySubType);
            CREATE INDEX IF NOT EXISTS IX_ModIndexedItems_SortName ON ModIndexedItems (SortName);
            CREATE INDEX IF NOT EXISTS IX_ModIndexedItems_SortKeyStable ON ModIndexedItems (SortKeyStable);
            CREATE INDEX IF NOT EXISTS IX_ModIndexedItems_PackagePath_SortKeyStable_UpdatedUtc
                ON ModIndexedItems (PackagePath, SortKeyStable, UpdatedUtcTicks DESC);
            CREATE INDEX IF NOT EXISTS IX_ModIndexedItems_KindSubType_SortKeyStable_UpdatedUtc
                ON ModIndexedItems (EntityKind, EntitySubType, SortKeyStable, UpdatedUtcTicks DESC);
            """);

        if (version != SchemaVersion)
        {
            connection.Execute(
                "INSERT INTO ModItemIndexSchemaMeta (SchemaVersion) VALUES (@SchemaVersion);",
                new { SchemaVersion });
        }
    }

    private const string InsertItemSql = """
        INSERT INTO ModIndexedItems (
            ItemKey, PackagePath, PackageFingerprintLength, PackageFingerprintLastWriteUtcTicks,
            EntityKind, EntitySubType, DisplayName, SortName, SearchText, ScopeText, ThumbnailStatus,
            PrimaryTextureResourceKey, PrimaryTextureFormat, PrimaryTextureWidth, PrimaryTextureHeight,
            TextureCount, EditableTextureCount, HasTextureData, SourceResourceKey, SourceGroupText,
            CreatedUtcTicks, UpdatedUtcTicks, PartNameRaw, DisplayNameSource, TitleKey, PartDescriptionKey,
            BodyTypeNumeric, BodyTypeText, BodySubTypeNumeric, AgeGenderFlags, AgeGenderText,
            SpeciesNumeric, SpeciesText, OutfitId, SortKeyStable, DisplayStage, ThumbnailStage,
            TextureStage, ThumbnailCacheKey, LastFastParsedUtcTicks, LastDeepParsedUtcTicks, PendingDeepRefresh
        ) VALUES (
            @ItemKey, @PackagePath, @PackageFingerprintLength, @PackageFingerprintLastWriteUtcTicks,
            @EntityKind, @EntitySubType, @DisplayName, @SortName, @SearchText, @ScopeText, @ThumbnailStatus,
            @PrimaryTextureResourceKey, @PrimaryTextureFormat, @PrimaryTextureWidth, @PrimaryTextureHeight,
            @TextureCount, @EditableTextureCount, @HasTextureData, @SourceResourceKey, @SourceGroupText,
            @CreatedUtcTicks, @UpdatedUtcTicks, @PartNameRaw, @DisplayNameSource, @TitleKey, @PartDescriptionKey,
            @BodyTypeNumeric, @BodyTypeText, @BodySubTypeNumeric, @AgeGenderFlags, @AgeGenderText,
            @SpeciesNumeric, @SpeciesText, @OutfitId, @SortKeyStable, @DisplayStage, @ThumbnailStage,
            @TextureStage, @ThumbnailCacheKey, @LastFastParsedUtcTicks, @LastDeepParsedUtcTicks, @PendingDeepRefresh
        );
        """;

    private const string UpsertItemSql = """
        INSERT INTO ModIndexedItems (
            ItemKey, PackagePath, PackageFingerprintLength, PackageFingerprintLastWriteUtcTicks,
            EntityKind, EntitySubType, DisplayName, SortName, SearchText, ScopeText, ThumbnailStatus,
            PrimaryTextureResourceKey, PrimaryTextureFormat, PrimaryTextureWidth, PrimaryTextureHeight,
            TextureCount, EditableTextureCount, HasTextureData, SourceResourceKey, SourceGroupText,
            CreatedUtcTicks, UpdatedUtcTicks, PartNameRaw, DisplayNameSource, TitleKey, PartDescriptionKey,
            BodyTypeNumeric, BodyTypeText, BodySubTypeNumeric, AgeGenderFlags, AgeGenderText,
            SpeciesNumeric, SpeciesText, OutfitId, SortKeyStable, DisplayStage, ThumbnailStage,
            TextureStage, ThumbnailCacheKey, LastFastParsedUtcTicks, LastDeepParsedUtcTicks, PendingDeepRefresh
        ) VALUES (
            @ItemKey, @PackagePath, @PackageFingerprintLength, @PackageFingerprintLastWriteUtcTicks,
            @EntityKind, @EntitySubType, @DisplayName, @SortName, @SearchText, @ScopeText, @ThumbnailStatus,
            @PrimaryTextureResourceKey, @PrimaryTextureFormat, @PrimaryTextureWidth, @PrimaryTextureHeight,
            @TextureCount, @EditableTextureCount, @HasTextureData, @SourceResourceKey, @SourceGroupText,
            @CreatedUtcTicks, @UpdatedUtcTicks, @PartNameRaw, @DisplayNameSource, @TitleKey, @PartDescriptionKey,
            @BodyTypeNumeric, @BodyTypeText, @BodySubTypeNumeric, @AgeGenderFlags, @AgeGenderText,
            @SpeciesNumeric, @SpeciesText, @OutfitId, @SortKeyStable, @DisplayStage, @ThumbnailStage,
            @TextureStage, @ThumbnailCacheKey, @LastFastParsedUtcTicks, @LastDeepParsedUtcTicks, @PendingDeepRefresh
        )
        ON CONFLICT(ItemKey) DO UPDATE SET
            PackagePath = excluded.PackagePath,
            PackageFingerprintLength = excluded.PackageFingerprintLength,
            PackageFingerprintLastWriteUtcTicks = excluded.PackageFingerprintLastWriteUtcTicks,
            EntityKind = excluded.EntityKind,
            EntitySubType = excluded.EntitySubType,
            DisplayName = excluded.DisplayName,
            SortName = excluded.SortName,
            SearchText = excluded.SearchText,
            ScopeText = excluded.ScopeText,
            ThumbnailStatus = excluded.ThumbnailStatus,
            PrimaryTextureResourceKey = excluded.PrimaryTextureResourceKey,
            PrimaryTextureFormat = excluded.PrimaryTextureFormat,
            PrimaryTextureWidth = excluded.PrimaryTextureWidth,
            PrimaryTextureHeight = excluded.PrimaryTextureHeight,
            TextureCount = excluded.TextureCount,
            EditableTextureCount = excluded.EditableTextureCount,
            HasTextureData = excluded.HasTextureData,
            SourceResourceKey = excluded.SourceResourceKey,
            SourceGroupText = excluded.SourceGroupText,
            UpdatedUtcTicks = excluded.UpdatedUtcTicks,
            PartNameRaw = excluded.PartNameRaw,
            DisplayNameSource = excluded.DisplayNameSource,
            TitleKey = excluded.TitleKey,
            PartDescriptionKey = excluded.PartDescriptionKey,
            BodyTypeNumeric = excluded.BodyTypeNumeric,
            BodyTypeText = excluded.BodyTypeText,
            BodySubTypeNumeric = excluded.BodySubTypeNumeric,
            AgeGenderFlags = excluded.AgeGenderFlags,
            AgeGenderText = excluded.AgeGenderText,
            SpeciesNumeric = excluded.SpeciesNumeric,
            SpeciesText = excluded.SpeciesText,
            OutfitId = excluded.OutfitId,
            SortKeyStable = excluded.SortKeyStable,
            DisplayStage = excluded.DisplayStage,
            ThumbnailStage = excluded.ThumbnailStage,
            TextureStage = excluded.TextureStage,
            ThumbnailCacheKey = excluded.ThumbnailCacheKey,
            LastFastParsedUtcTicks = excluded.LastFastParsedUtcTicks,
            LastDeepParsedUtcTicks = excluded.LastDeepParsedUtcTicks,
            PendingDeepRefresh = excluded.PendingDeepRefresh;
        """;

    private static void ReplaceItemSearchRow(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, ModIndexedItemRecord item)
    {
        connection.Execute(
            """
            DELETE FROM ModIndexedItemsSearch
            WHERE ItemKey = @ItemKey;

            INSERT INTO ModIndexedItemsSearch (ItemKey, SearchText)
            VALUES (@ItemKey, @SearchText);
            """,
            new
            {
                item.ItemKey,
                item.SearchText
            },
            transaction);
    }

    private static string? BuildFtsQuery(string? searchQuery)
    {
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            return null;
        }

        var tokens = searchQuery
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeFtsToken)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
        if (tokens.Length == 0)
        {
            return null;
        }

        return string.Join(" AND ", tokens.Select(token => $"{token}*"));
    }

    private static int CountFtsTokens(string? ftsQuery)
    {
        if (string.IsNullOrWhiteSpace(ftsQuery))
        {
            return 0;
        }

        return ftsQuery
            .Split(" AND ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
    }

    private static string? NormalizeFtsToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var normalized = new string(token
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized;
    }

    private sealed class PackageStateRow
    {
        public string PackagePath { get; set; } = string.Empty;
        public long FileLength { get; set; }
        public long LastWriteUtcTicks { get; set; }
        public string PackageType { get; set; } = string.Empty;
        public string ScopeHint { get; set; } = string.Empty;
        public long IndexedUtcTicks { get; set; }
        public int ItemCount { get; set; }
        public int CasItemCount { get; set; }
        public int BuildBuyItemCount { get; set; }
        public int UnclassifiedEntityCount { get; set; }
        public int TextureResourceCount { get; set; }
        public int EditableTextureCount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? FailureMessage { get; set; }
    }

    private sealed class IndexedItemRow
    {
        public string ItemKey { get; set; } = string.Empty;
        public string PackagePath { get; set; } = string.Empty;
        public long PackageFingerprintLength { get; set; }
        public long PackageFingerprintLastWriteUtcTicks { get; set; }
        public string EntityKind { get; set; } = string.Empty;
        public string EntitySubType { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string SortName { get; set; } = string.Empty;
        public string SearchText { get; set; } = string.Empty;
        public string ScopeText { get; set; } = string.Empty;
        public string ThumbnailStatus { get; set; } = string.Empty;
        public string? PrimaryTextureResourceKey { get; set; }
        public string? PrimaryTextureFormat { get; set; }
        public int? PrimaryTextureWidth { get; set; }
        public int? PrimaryTextureHeight { get; set; }
        public int TextureCount { get; set; }
        public int EditableTextureCount { get; set; }
        public int HasTextureData { get; set; }
        public string SourceResourceKey { get; set; } = string.Empty;
        public string SourceGroupText { get; set; } = string.Empty;
        public long CreatedUtcTicks { get; set; }
        public long UpdatedUtcTicks { get; set; }
        public string? PartNameRaw { get; set; }
        public string DisplayNameSource { get; set; } = "Fallback";
        public long? TitleKey { get; set; }
        public long? PartDescriptionKey { get; set; }
        public long? BodyTypeNumeric { get; set; }
        public string? BodyTypeText { get; set; }
        public long? BodySubTypeNumeric { get; set; }
        public long? AgeGenderFlags { get; set; }
        public string? AgeGenderText { get; set; }
        public long? SpeciesNumeric { get; set; }
        public string? SpeciesText { get; set; }
        public long? OutfitId { get; set; }
        public string SortKeyStable { get; set; } = string.Empty;
        public string DisplayStage { get; set; } = nameof(ModItemDisplayStage.Fast);
        public string ThumbnailStage { get; set; } = nameof(ModItemThumbnailStage.None);
        public string TextureStage { get; set; } = nameof(ModItemTextureStage.Pending);
        public string? ThumbnailCacheKey { get; set; }
        public long LastFastParsedUtcTicks { get; set; }
        public long? LastDeepParsedUtcTicks { get; set; }
        public int PendingDeepRefresh { get; set; }
    }

    private sealed class IndexedTextureRow
    {
        public string ResourceKeyText { get; set; } = string.Empty;
        public string ContainerKind { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int MipMapCount { get; set; }
        public int Editable { get; set; }
        public string SuggestedAction { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public int SizeBytes { get; set; }
        public string LinkRole { get; set; } = "Fallback";
    }

    private sealed class QueryPlanRow
    {
        public string Detail { get; set; } = string.Empty;
    }

    private sealed class BulkTextureRow
    {
        public string ItemKey { get; set; } = string.Empty;
        public string ResourceKeyText { get; set; } = string.Empty;
        public string ContainerKind { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int MipMapCount { get; set; }
        public int Editable { get; set; }
        public string SuggestedAction { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public int SizeBytes { get; set; }
        public int IsPrimary { get; set; }
        public int SortOrder { get; set; }
        public string LinkRole { get; set; } = "Fallback";
    }

    private readonly record struct QueryCountCacheKey(
        string RootPrefix,
        string KindFilter,
        string SubTypeFilter,
        string SearchPattern);

    private static string BuildCountCacheKey(QueryCountCacheKey key)
    {
        return string.Join(
            "\u001F",
            key.RootPrefix,
            key.KindFilter,
            key.SubTypeFilter,
            key.SearchPattern);
    }

    private static string BuildPageCacheKey(
        string? rootPrefix,
        string? kindFilter,
        string? subTypeFilter,
        string? searchPattern,
        string? sortBy,
        int pageIndex,
        int pageSize)
    {
        return string.Join(
            "\u001F",
            rootPrefix ?? string.Empty,
            kindFilter ?? string.Empty,
            subTypeFilter ?? string.Empty,
            searchPattern ?? string.Empty,
            (sortBy ?? string.Empty).Trim(),
            pageIndex.ToString(),
            pageSize.ToString());
    }

    private readonly record struct TextureDedupKey(
        string ItemKey,
        string ResourceKeyText);
}
