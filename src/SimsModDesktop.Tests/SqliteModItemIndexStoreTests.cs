using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.TextureCompression;
using Microsoft.Data.Sqlite;

namespace SimsModDesktop.Tests;

public sealed class SqliteModItemIndexStoreTests
{
    [Fact]
    public async Task ReplacePackageAsync_PersistsCasMetadataAndLinkRoles()
    {
        using var temp = new TempDirectory();
        var store = new SqliteModItemIndexStore(temp.Path);
        var packagePath = Path.Combine(temp.Path, "demo.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var now = DateTime.UtcNow.Ticks;
        await store.ReplacePackageAsync(new ModItemIndexBuildResult
        {
            PackageState = new ModPackageIndexState
            {
                PackagePath = packagePath,
                FileLength = 4,
                LastWriteUtcTicks = now,
                PackageType = ".package",
                ScopeHint = "CAS",
                IndexedUtcTicks = now,
                ItemCount = 1,
                CasItemCount = 1,
                BuildBuyItemCount = 0,
                UnclassifiedEntityCount = 0,
                TextureResourceCount = 1,
                EditableTextureCount = 1,
                Status = "Ready"
            },
            Items =
            [
                new ModIndexedItemRecord
                {
                    ItemKey = "item-1",
                    PackagePath = packagePath,
                    PackageFingerprintLength = 4,
                    PackageFingerprintLastWriteUtcTicks = now,
                    EntityKind = "Cas",
                    EntitySubType = "Hair",
                    DisplayName = "Localized Hair",
                    SortName = "Localized Hair",
                    SearchText = "Localized Hair Hair Adult Female",
                    ScopeText = "Cas",
                    ThumbnailStatus = "TextureLinked",
                    PrimaryTextureResourceKey = "00B2D882:00000001:AAAABBBBCCCCDDDD",
                    PrimaryTextureFormat = "DXT5",
                    PrimaryTextureWidth = 1024,
                    PrimaryTextureHeight = 1024,
                    TextureCount = 1,
                    EditableTextureCount = 1,
                    HasTextureData = true,
                    SourceResourceKey = "034AEECB:00000001:ABCDEF1200000001",
                    SourceGroupText = "00000001",
                    CreatedUtcTicks = now,
                    UpdatedUtcTicks = now,
                    PartNameRaw = "part_debug_hair",
                    DisplayNameSource = "Stbl",
                    TitleKey = 0x11111111,
                    PartDescriptionKey = 0x22222222,
                    BodyTypeNumeric = 2,
                    BodyTypeText = "Hair",
                    BodySubTypeNumeric = 0,
                    AgeGenderFlags = 0x00002010,
                    AgeGenderText = "YoungAdult, Female",
                    SpeciesNumeric = 1,
                    SpeciesText = "Human",
                    OutfitId = 0xDEADBEEF,
                    TextureCandidates =
                    [
                        new ModPackageTextureCandidate
                        {
                            ResourceKeyText = "00B2D882:00000001:AAAABBBBCCCCDDDD",
                            ContainerKind = "DDS",
                            Format = "DXT5",
                            Width = 1024,
                            Height = 1024,
                            MipMapCount = 10,
                            Editable = true,
                            SuggestedAction = "Keep",
                            Notes = "Primary diffuse",
                            SizeBytes = 512,
                            LinkRole = "Diffuse"
                        }
                    ]
                }
            ]
        });

        var page = await store.QueryPageAsync(new ModItemCatalogQuery
        {
            ModsRoot = temp.Path,
            SearchQuery = string.Empty,
            EntityKindFilter = "All",
            SubTypeFilter = "All",
            SortBy = "Name",
            PageIndex = 1,
            PageSize = 50
        });
        var inspect = await store.TryGetInspectAsync("item-1");

        Assert.Single(page.Items);
        Assert.Equal("Hair", page.Items[0].BodyTypeText);
        Assert.Equal("Stbl", page.Items[0].DisplayNameSource);
        Assert.NotNull(inspect);
        Assert.Equal("Hair", inspect!.BodyTypeText);
        Assert.Equal("Stbl", inspect.DisplayNameSource);
        Assert.Single(inspect.TextureRows);
        Assert.Equal("Diffuse", inspect.TextureRows[0].LinkRole);
    }

    [Fact]
    public async Task QueryPageAsync_WithSearchQuery_UsesFtsAndKeepsNoSearchPathWorking()
    {
        using var temp = new TempDirectory();
        var store = new SqliteModItemIndexStore(temp.Path);
        var packagePath = Path.Combine(temp.Path, "demo.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);
        var now = DateTime.UtcNow.Ticks;

        await store.ReplacePackageAsync(new ModItemIndexBuildResult
        {
            PackageState = new ModPackageIndexState
            {
                PackagePath = packagePath,
                FileLength = 4,
                LastWriteUtcTicks = now,
                PackageType = ".package",
                ScopeHint = "Mixed",
                IndexedUtcTicks = now,
                ItemCount = 2,
                CasItemCount = 1,
                BuildBuyItemCount = 1,
                UnclassifiedEntityCount = 0,
                TextureResourceCount = 0,
                EditableTextureCount = 0,
                Status = "Ready"
            },
            Items =
            [
                new ModIndexedItemRecord
                {
                    ItemKey = "item-hair",
                    PackagePath = packagePath,
                    PackageFingerprintLength = 4,
                    PackageFingerprintLastWriteUtcTicks = now,
                    EntityKind = "Cas",
                    EntitySubType = "Hair",
                    DisplayName = "Localized Hair",
                    SortName = "Localized Hair",
                    SearchText = "Localized Hair Hair Adult Female",
                    ScopeText = "Cas",
                    ThumbnailStatus = "Placeholder",
                    TextureCount = 0,
                    EditableTextureCount = 0,
                    HasTextureData = false,
                    SourceResourceKey = "034AEECB:00000001:0000000000000001",
                    SourceGroupText = "00000001",
                    CreatedUtcTicks = now,
                    UpdatedUtcTicks = now,
                    SortKeyStable = "Cas:Hair:0000000000000001",
                    DisplayStage = ModItemDisplayStage.Fast,
                    ThumbnailStage = ModItemThumbnailStage.None,
                    TextureStage = ModItemTextureStage.Pending,
                    LastFastParsedUtcTicks = now,
                    PendingDeepRefresh = true,
                    DisplayNameSource = "Fallback",
                    TextureCandidates = Array.Empty<ModPackageTextureCandidate>()
                },
                new ModIndexedItemRecord
                {
                    ItemKey = "item-object",
                    PackagePath = packagePath,
                    PackageFingerprintLength = 4,
                    PackageFingerprintLastWriteUtcTicks = now,
                    EntityKind = "BuildBuy",
                    EntitySubType = "Object",
                    DisplayName = "Modern Sofa",
                    SortName = "Modern Sofa",
                    SearchText = "Modern Sofa BuildBuy Object",
                    ScopeText = "BuildBuy",
                    ThumbnailStatus = "Placeholder",
                    TextureCount = 0,
                    EditableTextureCount = 0,
                    HasTextureData = false,
                    SourceResourceKey = "319E4F1D:00000001:0000000000000002",
                    SourceGroupText = "00000001",
                    CreatedUtcTicks = now,
                    UpdatedUtcTicks = now,
                    SortKeyStable = "BuildBuy:Object:0000000000000002",
                    DisplayStage = ModItemDisplayStage.Fast,
                    ThumbnailStage = ModItemThumbnailStage.None,
                    TextureStage = ModItemTextureStage.Pending,
                    LastFastParsedUtcTicks = now,
                    PendingDeepRefresh = true,
                    DisplayNameSource = "Fallback",
                    TextureCandidates = Array.Empty<ModPackageTextureCandidate>()
                }
            ]
        });

        var filtered = await store.QueryPageAsync(new ModItemCatalogQuery
        {
            ModsRoot = temp.Path,
            SearchQuery = "Local Hair",
            EntityKindFilter = "All",
            SubTypeFilter = "All",
            SortBy = "Name",
            PageIndex = 1,
            PageSize = 50
        });

        var unfiltered = await store.QueryPageAsync(new ModItemCatalogQuery
        {
            ModsRoot = temp.Path,
            SearchQuery = string.Empty,
            EntityKindFilter = "All",
            SubTypeFilter = "All",
            SortBy = "Name",
            PageIndex = 1,
            PageSize = 50
        });

        Assert.Single(filtered.Items);
        Assert.Equal("item-hair", filtered.Items[0].ItemKey);
        Assert.Equal(2, unfiltered.Items.Count);
    }

    [Fact]
    public async Task CountIndexedPackagesAsync_RebuildsOldSchema()
    {
        using var temp = new TempDirectory();
        var dbPath = Path.Combine(temp.Path, "app-cache.db");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadWriteCreate;Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE ModItemIndexSchemaMeta (SchemaVersion INTEGER NOT NULL);
                INSERT INTO ModItemIndexSchemaMeta (SchemaVersion) VALUES (1);
                CREATE TABLE ModIndexedItems (ItemKey TEXT PRIMARY KEY, DisplayName TEXT NOT NULL);
                """;
            command.ExecuteNonQuery();
        }

        var store = new SqliteModItemIndexStore(temp.Path);
        var count = await store.CountIndexedPackagesAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ReplacePackageFastAsync_ThenApplyItemEnrichmentBatchAsync_UpdatesDeepFieldsInPlace()
    {
        using var temp = new TempDirectory();
        var store = new SqliteModItemIndexStore(temp.Path);
        var packagePath = Path.Combine(temp.Path, "demo.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);

        var now = DateTime.UtcNow.Ticks;
        await store.ReplacePackageFastAsync(new ModItemFastIndexBuildResult
        {
            PackageState = new ModPackageIndexState
            {
                PackagePath = packagePath,
                FileLength = 4,
                LastWriteUtcTicks = now,
                PackageType = ".package",
                ScopeHint = "CAS",
                IndexedUtcTicks = now,
                ItemCount = 1,
                CasItemCount = 1,
                BuildBuyItemCount = 0,
                UnclassifiedEntityCount = 0,
                TextureResourceCount = 0,
                EditableTextureCount = 0,
                Status = "FastReady"
            },
            Items =
            [
                new ModIndexedItemRecord
                {
                    ItemKey = "item-1",
                    PackagePath = packagePath,
                    PackageFingerprintLength = 4,
                    PackageFingerprintLastWriteUtcTicks = now,
                    EntityKind = "Cas",
                    EntitySubType = "Hair",
                    DisplayName = "Hair 0x00000001",
                    SortName = "Hair 0x00000001",
                    SearchText = "Hair 0x00000001",
                    ScopeText = "Cas",
                    ThumbnailStatus = "Placeholder",
                    TextureCount = 0,
                    EditableTextureCount = 0,
                    HasTextureData = false,
                    SourceResourceKey = "034AEECB:00000001:ABCDEF1200000001",
                    SourceGroupText = "00000001",
                    CreatedUtcTicks = now,
                    UpdatedUtcTicks = now,
                    SortKeyStable = "Cas:Hair:0000000000000001",
                    DisplayStage = ModItemDisplayStage.Fast,
                    ThumbnailStage = ModItemThumbnailStage.None,
                    TextureStage = ModItemTextureStage.Pending,
                    LastFastParsedUtcTicks = now,
                    PendingDeepRefresh = true,
                    DisplayNameSource = "Fallback",
                    TextureCandidates = Array.Empty<ModPackageTextureCandidate>()
                }
            ]
        });

        await store.ApplyItemEnrichmentBatchAsync(new ModItemEnrichmentBatch
        {
            PackageState = new ModPackageIndexState
            {
                PackagePath = packagePath,
                FileLength = 4,
                LastWriteUtcTicks = now,
                PackageType = ".package",
                ScopeHint = "CAS",
                IndexedUtcTicks = now,
                ItemCount = 1,
                CasItemCount = 1,
                BuildBuyItemCount = 0,
                UnclassifiedEntityCount = 0,
                TextureResourceCount = 1,
                EditableTextureCount = 1,
                Status = "Ready"
            },
            Items =
            [
                new ModIndexedItemRecord
                {
                    ItemKey = "item-1",
                    PackagePath = packagePath,
                    PackageFingerprintLength = 4,
                    PackageFingerprintLastWriteUtcTicks = now,
                    EntityKind = "Cas",
                    EntitySubType = "Hair",
                    DisplayName = "Localized Hair",
                    SortName = "Localized Hair",
                    SearchText = "Localized Hair Hair Adult Female",
                    ScopeText = "Cas",
                    ThumbnailStatus = "TextureLinked",
                    PrimaryTextureResourceKey = "00B2D882:00000001:AAAABBBBCCCCDDDD",
                    PrimaryTextureFormat = "DXT5",
                    PrimaryTextureWidth = 1024,
                    PrimaryTextureHeight = 1024,
                    TextureCount = 1,
                    EditableTextureCount = 1,
                    HasTextureData = true,
                    SourceResourceKey = "034AEECB:00000001:ABCDEF1200000001",
                    SourceGroupText = "00000001",
                    CreatedUtcTicks = now,
                    UpdatedUtcTicks = now,
                    SortKeyStable = "Cas:Hair:0000000000000001",
                    DisplayStage = ModItemDisplayStage.Complete,
                    ThumbnailStage = ModItemThumbnailStage.None,
                    TextureStage = ModItemTextureStage.Ready,
                    LastFastParsedUtcTicks = now,
                    LastDeepParsedUtcTicks = now + 1,
                    PendingDeepRefresh = false,
                    DisplayNameSource = "Stbl",
                    BodyTypeText = "Hair",
                    AgeGenderText = "YoungAdult, Female",
                    TextureCandidates =
                    [
                        new ModPackageTextureCandidate
                        {
                            ResourceKeyText = "00B2D882:00000001:AAAABBBBCCCCDDDD",
                            ContainerKind = "DDS",
                            Format = "DXT5",
                            Width = 1024,
                            Height = 1024,
                            MipMapCount = 10,
                            Editable = true,
                            SuggestedAction = "Keep",
                            Notes = "Primary diffuse",
                            SizeBytes = 512,
                            LinkRole = "Diffuse"
                        }
                    ]
                }
            ],
            AffectedItemKeys = ["item-1"]
        });

        var page = await store.QueryPageAsync(new ModItemCatalogQuery
        {
            ModsRoot = temp.Path,
            SearchQuery = string.Empty,
            EntityKindFilter = "All",
            SubTypeFilter = "All",
            SortBy = "Name",
            PageIndex = 1,
            PageSize = 50
        });

        Assert.Single(page.Items);
        Assert.Equal("Localized Hair", page.Items[0].DisplayName);
        Assert.True(page.Items[0].HasDeepData);
        Assert.Equal(ModItemDisplayStage.Complete, page.Items[0].DisplayStage);
        Assert.Equal(ModItemTextureStage.Ready, page.Items[0].TextureStage);
        Assert.False(page.Items[0].ShowMetadataPlaceholder);
    }

    [Fact]
    public async Task ApplyItemEnrichmentBatchesAsync_DeduplicatesSearchRowsByItemKey()
    {
        using var temp = new TempDirectory();
        var store = new SqliteModItemIndexStore(temp.Path);
        var packagePath = Path.Combine(temp.Path, "dedup.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);
        var now = DateTime.UtcNow.Ticks;

        await store.ApplyItemEnrichmentBatchesAsync(
        [
            new ModItemEnrichmentBatch
            {
                PackageState = new ModPackageIndexState
                {
                    PackagePath = packagePath,
                    FileLength = 4,
                    LastWriteUtcTicks = now,
                    PackageType = ".package",
                    ScopeHint = "CAS",
                    IndexedUtcTicks = now,
                    ItemCount = 1,
                    CasItemCount = 1,
                    BuildBuyItemCount = 0,
                    UnclassifiedEntityCount = 0,
                    TextureResourceCount = 0,
                    EditableTextureCount = 0,
                    Status = "Ready"
                },
                Items =
                [
                    new ModIndexedItemRecord
                    {
                        ItemKey = "item-dup",
                        PackagePath = packagePath,
                        PackageFingerprintLength = 4,
                        PackageFingerprintLastWriteUtcTicks = now,
                        EntityKind = "Cas",
                        EntitySubType = "Hair",
                        DisplayName = "First",
                        SortName = "First",
                        SearchText = "First Search",
                        ScopeText = "Cas",
                        ThumbnailStatus = "Placeholder",
                        TextureCount = 0,
                        EditableTextureCount = 0,
                        HasTextureData = false,
                        SourceResourceKey = "034AEECB:00000001:0000000000000001",
                        SourceGroupText = "00000001",
                        CreatedUtcTicks = now,
                        UpdatedUtcTicks = now,
                        SortKeyStable = "Cas:Hair:0000000000000001",
                        DisplayStage = ModItemDisplayStage.Fast,
                        ThumbnailStage = ModItemThumbnailStage.None,
                        TextureStage = ModItemTextureStage.Pending,
                        LastFastParsedUtcTicks = now,
                        PendingDeepRefresh = true,
                        DisplayNameSource = "Fallback",
                        TextureCandidates = Array.Empty<ModPackageTextureCandidate>()
                    },
                    new ModIndexedItemRecord
                    {
                        ItemKey = "item-dup",
                        PackagePath = packagePath,
                        PackageFingerprintLength = 4,
                        PackageFingerprintLastWriteUtcTicks = now,
                        EntityKind = "Cas",
                        EntitySubType = "Hair",
                        DisplayName = "Second",
                        SortName = "Second",
                        SearchText = "Second Search",
                        ScopeText = "Cas",
                        ThumbnailStatus = "Placeholder",
                        TextureCount = 0,
                        EditableTextureCount = 0,
                        HasTextureData = false,
                        SourceResourceKey = "034AEECB:00000001:0000000000000001",
                        SourceGroupText = "00000001",
                        CreatedUtcTicks = now,
                        UpdatedUtcTicks = now + 1,
                        SortKeyStable = "Cas:Hair:0000000000000001",
                        DisplayStage = ModItemDisplayStage.Complete,
                        ThumbnailStage = ModItemThumbnailStage.None,
                        TextureStage = ModItemTextureStage.Pending,
                        LastFastParsedUtcTicks = now,
                        LastDeepParsedUtcTicks = now + 1,
                        PendingDeepRefresh = false,
                        DisplayNameSource = "Fallback",
                        TextureCandidates = Array.Empty<ModPackageTextureCandidate>()
                    }
                ],
                AffectedItemKeys = ["item-dup"]
            }
        ]);

        var dbPath = Path.Combine(temp.Path, "app-cache.db");
        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadWrite;Pooling=False;");
        await connection.OpenAsync();
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(1) FROM ModIndexedItemsSearch WHERE ItemKey = @ItemKey;";
        countCommand.Parameters.AddWithValue("@ItemKey", "item-dup");
        var rowCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

        await using var textCommand = connection.CreateCommand();
        textCommand.CommandText = "SELECT SearchText FROM ModIndexedItemsSearch WHERE ItemKey = @ItemKey LIMIT 1;";
        textCommand.Parameters.AddWithValue("@ItemKey", "item-dup");
        var searchText = Convert.ToString(await textCommand.ExecuteScalarAsync());

        Assert.Equal(1, rowCount);
        Assert.Equal("Second Search", searchText);
    }

    [Fact]
    public async Task ApplyItemEnrichmentBatchesAsync_DeduplicatesTextureRowsByItemAndResourceKey()
    {
        using var temp = new TempDirectory();
        var store = new SqliteModItemIndexStore(temp.Path);
        var packagePath = Path.Combine(temp.Path, "texture-dedup.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);
        var now = DateTime.UtcNow.Ticks;
        const string duplicatedResourceKey = "00B2D882:00000001:AAAABBBBCCCCDDDD";

        await store.ApplyItemEnrichmentBatchesAsync(
        [
            new ModItemEnrichmentBatch
            {
                PackageState = new ModPackageIndexState
                {
                    PackagePath = packagePath,
                    FileLength = 4,
                    LastWriteUtcTicks = now,
                    PackageType = ".package",
                    ScopeHint = "CAS",
                    IndexedUtcTicks = now,
                    ItemCount = 1,
                    CasItemCount = 1,
                    BuildBuyItemCount = 0,
                    UnclassifiedEntityCount = 0,
                    TextureResourceCount = 1,
                    EditableTextureCount = 1,
                    Status = "Ready"
                },
                Items =
                [
                    new ModIndexedItemRecord
                    {
                        ItemKey = "item-texture-dup",
                        PackagePath = packagePath,
                        PackageFingerprintLength = 4,
                        PackageFingerprintLastWriteUtcTicks = now,
                        EntityKind = "Cas",
                        EntitySubType = "Hair",
                        DisplayName = "Texture First",
                        SortName = "Texture First",
                        SearchText = "Texture First",
                        ScopeText = "Cas",
                        ThumbnailStatus = "TextureLinked",
                        PrimaryTextureResourceKey = duplicatedResourceKey,
                        PrimaryTextureFormat = "DXT5",
                        PrimaryTextureWidth = 1024,
                        PrimaryTextureHeight = 1024,
                        TextureCount = 1,
                        EditableTextureCount = 1,
                        HasTextureData = true,
                        SourceResourceKey = "034AEECB:00000001:0000000000000001",
                        SourceGroupText = "00000001",
                        CreatedUtcTicks = now,
                        UpdatedUtcTicks = now,
                        SortKeyStable = "Cas:Hair:0000000000000001",
                        DisplayStage = ModItemDisplayStage.Complete,
                        ThumbnailStage = ModItemThumbnailStage.None,
                        TextureStage = ModItemTextureStage.Ready,
                        LastFastParsedUtcTicks = now,
                        LastDeepParsedUtcTicks = now,
                        PendingDeepRefresh = false,
                        DisplayNameSource = "Fallback",
                        TextureCandidates =
                        [
                            new ModPackageTextureCandidate
                            {
                                ResourceKeyText = duplicatedResourceKey,
                                ContainerKind = "DDS",
                                Format = "DXT5",
                                Width = 1024,
                                Height = 1024,
                                MipMapCount = 10,
                                Editable = true,
                                SuggestedAction = "Keep",
                                Notes = "first note",
                                SizeBytes = 512,
                                LinkRole = "Diffuse"
                            }
                        ]
                    },
                    new ModIndexedItemRecord
                    {
                        ItemKey = "item-texture-dup",
                        PackagePath = packagePath,
                        PackageFingerprintLength = 4,
                        PackageFingerprintLastWriteUtcTicks = now,
                        EntityKind = "Cas",
                        EntitySubType = "Hair",
                        DisplayName = "Texture Second",
                        SortName = "Texture Second",
                        SearchText = "Texture Second",
                        ScopeText = "Cas",
                        ThumbnailStatus = "TextureLinked",
                        PrimaryTextureResourceKey = duplicatedResourceKey,
                        PrimaryTextureFormat = "DXT5",
                        PrimaryTextureWidth = 1024,
                        PrimaryTextureHeight = 1024,
                        TextureCount = 1,
                        EditableTextureCount = 1,
                        HasTextureData = true,
                        SourceResourceKey = "034AEECB:00000001:0000000000000001",
                        SourceGroupText = "00000001",
                        CreatedUtcTicks = now,
                        UpdatedUtcTicks = now + 1,
                        SortKeyStable = "Cas:Hair:0000000000000001",
                        DisplayStage = ModItemDisplayStage.Complete,
                        ThumbnailStage = ModItemThumbnailStage.None,
                        TextureStage = ModItemTextureStage.Ready,
                        LastFastParsedUtcTicks = now,
                        LastDeepParsedUtcTicks = now + 1,
                        PendingDeepRefresh = false,
                        DisplayNameSource = "Fallback",
                        TextureCandidates =
                        [
                            new ModPackageTextureCandidate
                            {
                                ResourceKeyText = duplicatedResourceKey,
                                ContainerKind = "DDS",
                                Format = "DXT5",
                                Width = 1024,
                                Height = 1024,
                                MipMapCount = 10,
                                Editable = true,
                                SuggestedAction = "Keep",
                                Notes = "second note",
                                SizeBytes = 1024,
                                LinkRole = "Diffuse"
                            }
                        ]
                    }
                ],
                AffectedItemKeys = ["item-texture-dup"]
            }
        ]);

        var dbPath = Path.Combine(temp.Path, "app-cache.db");
        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadWrite;Pooling=False;");
        await connection.OpenAsync();
        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(1) FROM ModIndexedItemTextures WHERE ItemKey = @ItemKey;";
        countCommand.Parameters.AddWithValue("@ItemKey", "item-texture-dup");
        var rowCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());

        await using var noteCommand = connection.CreateCommand();
        noteCommand.CommandText = "SELECT Notes FROM ModIndexedItemTextures WHERE ItemKey = @ItemKey AND ResourceKeyText = @ResourceKey LIMIT 1;";
        noteCommand.Parameters.AddWithValue("@ItemKey", "item-texture-dup");
        noteCommand.Parameters.AddWithValue("@ResourceKey", duplicatedResourceKey);
        var note = Convert.ToString(await noteCommand.ExecuteScalarAsync());

        Assert.Equal(1, rowCount);
        Assert.Equal("second note", note);
    }

    [Fact]
    public async Task QueryPageAsync_UsesInjectedQueryCacheAndInvalidatesOnWrite()
    {
        using var temp = new TempDirectory();
        var queryCache = new RecordingListQueryCache();
        var store = new SqliteModItemIndexStore(temp.Path, queryCache: queryCache);
        var packagePath = Path.Combine(temp.Path, "bounded.package");
        File.WriteAllBytes(packagePath, [1, 2, 3, 4]);
        var now = DateTime.UtcNow.Ticks;

        await store.ReplacePackageAsync(new ModItemIndexBuildResult
        {
            PackageState = new ModPackageIndexState
            {
                PackagePath = packagePath,
                FileLength = 4,
                LastWriteUtcTicks = now,
                PackageType = ".package",
                ScopeHint = "CAS",
                IndexedUtcTicks = now,
                ItemCount = 1,
                CasItemCount = 1,
                BuildBuyItemCount = 0,
                UnclassifiedEntityCount = 0,
                TextureResourceCount = 0,
                EditableTextureCount = 0,
                Status = "Ready"
            },
            Items =
            [
                new ModIndexedItemRecord
                {
                    ItemKey = "item-cache",
                    PackagePath = packagePath,
                    PackageFingerprintLength = 4,
                    PackageFingerprintLastWriteUtcTicks = now,
                    EntityKind = "Cas",
                    EntitySubType = "Hair",
                    DisplayName = "Cache Item",
                    SortName = "Cache Item",
                    SearchText = "Cache Item",
                    ScopeText = "Cas",
                    ThumbnailStatus = "Placeholder",
                    TextureCount = 0,
                    EditableTextureCount = 0,
                    HasTextureData = false,
                    SourceResourceKey = "034AEECB:00000001:0000000000000001",
                    SourceGroupText = "00000001",
                    CreatedUtcTicks = now,
                    UpdatedUtcTicks = now,
                    SortKeyStable = "Cas:Hair:0000000000000001",
                    DisplayStage = ModItemDisplayStage.Fast,
                    ThumbnailStage = ModItemThumbnailStage.None,
                    TextureStage = ModItemTextureStage.Pending,
                    LastFastParsedUtcTicks = now,
                    PendingDeepRefresh = true,
                    DisplayNameSource = "Fallback",
                    TextureCandidates = Array.Empty<ModPackageTextureCandidate>()
                }
            ]
        });

        var query = new ModItemCatalogQuery
        {
            ModsRoot = temp.Path,
            SearchQuery = string.Empty,
            EntityKindFilter = "All",
            SubTypeFilter = "All",
            SortBy = "Name",
            PageIndex = 1,
            PageSize = 20
        };

        var firstPage = await store.QueryPageAsync(query);
        var pageSetCountAfterFirstQuery = queryCache.SetCounts.GetValueOrDefault("modindex.page");
        var countSetCountAfterFirstQuery = queryCache.SetCounts.GetValueOrDefault("modindex.count");

        var secondPage = await store.QueryPageAsync(query);
        var pageSetCountAfterSecondQuery = queryCache.SetCounts.GetValueOrDefault("modindex.page");
        var countSetCountAfterSecondQuery = queryCache.SetCounts.GetValueOrDefault("modindex.count");

        await store.ReplacePackageAsync(new ModItemIndexBuildResult
        {
            PackageState = new ModPackageIndexState
            {
                PackagePath = packagePath,
                FileLength = 4,
                LastWriteUtcTicks = now + 1,
                PackageType = ".package",
                ScopeHint = "CAS",
                IndexedUtcTicks = now + 1,
                ItemCount = 1,
                CasItemCount = 1,
                BuildBuyItemCount = 0,
                UnclassifiedEntityCount = 0,
                TextureResourceCount = 0,
                EditableTextureCount = 0,
                Status = "Ready"
            },
            Items =
            [
                new ModIndexedItemRecord
                {
                    ItemKey = "item-cache",
                    PackagePath = packagePath,
                    PackageFingerprintLength = 4,
                    PackageFingerprintLastWriteUtcTicks = now + 1,
                    EntityKind = "Cas",
                    EntitySubType = "Hair",
                    DisplayName = "Cache Item Updated",
                    SortName = "Cache Item Updated",
                    SearchText = "Cache Item Updated",
                    ScopeText = "Cas",
                    ThumbnailStatus = "Placeholder",
                    TextureCount = 0,
                    EditableTextureCount = 0,
                    HasTextureData = false,
                    SourceResourceKey = "034AEECB:00000001:0000000000000001",
                    SourceGroupText = "00000001",
                    CreatedUtcTicks = now,
                    UpdatedUtcTicks = now + 1,
                    SortKeyStable = "Cas:Hair:0000000000000001",
                    DisplayStage = ModItemDisplayStage.Fast,
                    ThumbnailStage = ModItemThumbnailStage.None,
                    TextureStage = ModItemTextureStage.Pending,
                    LastFastParsedUtcTicks = now + 1,
                    PendingDeepRefresh = true,
                    DisplayNameSource = "Fallback",
                    TextureCandidates = Array.Empty<ModPackageTextureCandidate>()
                }
            ]
        });

        var thirdPage = await store.QueryPageAsync(query);

        Assert.Single(firstPage.Items);
        Assert.Single(secondPage.Items);
        Assert.Single(thirdPage.Items);
        Assert.Equal("Cache Item", firstPage.Items[0].DisplayName);
        Assert.Equal("Cache Item", secondPage.Items[0].DisplayName);
        Assert.Equal("Cache Item Updated", thirdPage.Items[0].DisplayName);
        Assert.Equal(pageSetCountAfterFirstQuery, pageSetCountAfterSecondQuery);
        Assert.Equal(countSetCountAfterFirstQuery, countSetCountAfterSecondQuery);
        Assert.True(queryCache.TryGetHits.GetValueOrDefault("modindex.page") >= 1);
        Assert.True(queryCache.InvalidateCounts.GetValueOrDefault("modindex.page") >= 2);
        Assert.True(queryCache.SetCounts.GetValueOrDefault("modindex.page") > pageSetCountAfterSecondQuery);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mod-index-store-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class RecordingListQueryCache : IListQueryCache
    {
        private readonly Dictionary<string, object> _entries = new(StringComparer.Ordinal);

        public Dictionary<string, int> TryGetHits { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> SetCounts { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, int> InvalidateCounts { get; } = new(StringComparer.Ordinal);

        public bool TryGet<TValue>(string domain, string key, out TValue? value)
        {
            if (_entries.TryGetValue(BuildKey(domain, key), out var stored) && stored is TValue typed)
            {
                TryGetHits[domain] = TryGetHits.GetValueOrDefault(domain) + 1;
                value = typed;
                return true;
            }

            value = default;
            return false;
        }

        public void Set<TValue>(string domain, string key, TValue value)
        {
            _entries[BuildKey(domain, key)] = value!;
            SetCounts[domain] = SetCounts.GetValueOrDefault(domain) + 1;
        }

        public void InvalidateDomain(string domain, string? keyPrefix = null)
        {
            var prefix = BuildKey(domain, keyPrefix ?? string.Empty);
            foreach (var key in _entries.Keys.Where(candidate => candidate.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
            {
                _entries.Remove(key);
            }

            InvalidateCounts[domain] = InvalidateCounts.GetValueOrDefault(domain) + 1;
        }

        public void Clear()
        {
            _entries.Clear();
        }

        private static string BuildKey(string domain, string key)
        {
            return domain + "|" + key;
        }
    }
}
