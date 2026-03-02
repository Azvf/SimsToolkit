using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.TextureCompression;

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
}
