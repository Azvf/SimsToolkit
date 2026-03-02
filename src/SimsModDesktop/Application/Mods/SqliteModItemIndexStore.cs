using Dapper;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Infrastructure.Persistence;

namespace SimsModDesktop.Application.Mods;

public sealed class SqliteModItemIndexStore : IModItemIndexStore
{
    private const int SchemaVersion = 3;
    private readonly AppCacheDatabase _database;

    public SqliteModItemIndexStore()
        : this(new AppCacheDatabase())
    {
    }

    public SqliteModItemIndexStore(string cacheRootPath)
        : this(new AppCacheDatabase(cacheRootPath))
    {
    }

    private SqliteModItemIndexStore(AppCacheDatabase database)
    {
        _database = database;
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

    public Task ReplacePackageFastAsync(ModItemFastIndexBuildResult buildResult, CancellationToken cancellationToken = default)
    {
        return ReplacePackageCoreAsync(buildResult.PackageState, buildResult.Items, cancellationToken);
    }

    public Task ApplyItemEnrichmentBatchAsync(ModItemEnrichmentBatch batch, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        UpsertPackageState(connection, transaction, batch.PackageState);
        foreach (var item in batch.Items)
        {
            UpsertItem(connection, transaction, item);
            ReplaceTextures(connection, transaction, item);
        }

        transaction.Commit();
        return Task.CompletedTask;
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
        return Task.CompletedTask;
    }

    public Task<ModItemCatalogPage> QueryPageAsync(ModItemCatalogQuery query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenConnection();

        var pageIndex = Math.Max(1, query.PageIndex);
        var pageSize = Math.Max(1, query.PageSize);
        var offset = (pageIndex - 1) * pageSize;
        var searchPattern = string.IsNullOrWhiteSpace(query.SearchQuery) ? null : $"%{query.SearchQuery.Trim()}%";
        var kindFilter = NormalizeFilter(query.EntityKindFilter);
        var subTypeFilter = NormalizeFilter(query.SubTypeFilter);
        var rootPrefix = string.IsNullOrWhiteSpace(query.ModsRoot) ? null : $"{NormalizePath(query.ModsRoot)}%";
        const string whereClause = """
            WHERE (@RootPrefix IS NULL OR PackagePath LIKE @RootPrefix)
              AND (@KindFilter IS NULL OR EntityKind = @KindFilter)
              AND (@SubTypeFilter IS NULL OR EntitySubType = @SubTypeFilter)
              AND (@SearchPattern IS NULL OR SearchText LIKE @SearchPattern)
            """;

        var totalItems = connection.ExecuteScalar<int>(
            $"""
            SELECT COUNT(*)
            FROM ModIndexedItems
            {whereClause};
            """,
            new { RootPrefix = rootPrefix, KindFilter = kindFilter, SubTypeFilter = subTypeFilter, SearchPattern = searchPattern });

        var rows = connection.Query<IndexedItemRow>(
            $"""
            SELECT *
            FROM ModIndexedItems
            {whereClause}
            ORDER BY {ResolveOrderBy(query.SortBy)}
            LIMIT @PageSize OFFSET @Offset;
            """,
            new
            {
                RootPrefix = rootPrefix,
                KindFilter = kindFilter,
                SubTypeFilter = subTypeFilter,
                SearchPattern = searchPattern,
                PageSize = pageSize,
                Offset = offset
            });

        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        return Task.FromResult(new ModItemCatalogPage
        {
            Items = rows.Select(ToListRow).ToArray(),
            TotalItems = totalItems,
            PageIndex = Math.Min(pageIndex, totalPages),
            PageSize = pageSize,
            TotalPages = totalPages
        });
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
            DELETE FROM ModIndexedItemTextures WHERE ItemKey IN (
                SELECT ItemKey FROM ModIndexedItems WHERE PackagePath = @PackagePath
            );
            DELETE FROM ModIndexedItems WHERE PackagePath = @PackagePath;
            DELETE FROM ModPackageIndexState WHERE PackagePath = @PackagePath;
            """,
            new { PackagePath = NormalizePath(packagePath) },
            transaction);
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
    }

    private static void UpsertItem(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, ModIndexedItemRecord item)
    {
        connection.Execute(UpsertItemSql, CreateItemParameters(item), transaction);
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

    private static string ResolveOrderBy(string? sortBy)
    {
        return (sortBy ?? string.Empty).Trim() switch
        {
            "Package" => "PackagePath ASC, SortKeyStable ASC",
            "Name" => "SortKeyStable ASC, SortName ASC",
            _ => "SortKeyStable ASC, UpdatedUtcTicks DESC"
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

            CREATE INDEX IF NOT EXISTS IX_ModIndexedItems_PackagePath ON ModIndexedItems (PackagePath);
            CREATE INDEX IF NOT EXISTS IX_ModIndexedItems_EntityKind_SubType ON ModIndexedItems (EntityKind, EntitySubType);
            CREATE INDEX IF NOT EXISTS IX_ModIndexedItems_SortName ON ModIndexedItems (SortName);
            CREATE INDEX IF NOT EXISTS IX_ModIndexedItems_SearchText ON ModIndexedItems (SearchText);
            CREATE INDEX IF NOT EXISTS IX_ModIndexedItems_SortKeyStable ON ModIndexedItems (SortKeyStable);
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
}
