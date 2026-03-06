using SimsModDesktop.Application.Saves;
using SimsModDesktop.Infrastructure.Saves;
using SimsModDesktop.Infrastructure.Tray;

namespace SimsModDesktop.Tests;

public sealed class PreviewHelperTests
{
    [Fact]
    public void SaveDescriptorPreviewSourceReader_Read_MapsDescriptorEntriesIntoRootSnapshot()
    {
        using var cacheDir = new TempDirectory();
        using var saveDir = new TempDirectory();
        var saveFilePath = Path.Combine(saveDir.Path, "slot_00000001.save");
        File.WriteAllText(saveFilePath, "save-fixture");

        var manifest = new SavePreviewDescriptorManifest
        {
            SourceSavePath = Path.GetFullPath(saveFilePath),
            SourceLength = new FileInfo(saveFilePath).Length,
            SourceLastWriteTimeUtc = File.GetLastWriteTimeUtc(saveFilePath),
            DescriptorSchemaVersion = "save-preview-descriptor-v1",
            BuildStartedUtc = DateTime.UtcNow,
            BuildCompletedUtc = DateTime.UtcNow,
            TotalHouseholdCount = 1,
            ExportableHouseholdCount = 1,
            ReadyHouseholdCount = 1,
            BlockedHouseholdCount = 0,
            Entries =
            [
                new SavePreviewDescriptorEntry
                {
                    HouseholdId = 42,
                    TrayItemKey = "tray-key-42",
                    StableInstanceIdHex = "0x0000000000000042",
                    HouseholdName = "The Testers",
                    HomeZoneName = "Willow Creek",
                    HouseholdSize = 3,
                    CanExport = true,
                    BuildState = "Ready",
                    SearchText = "The Testers Willow Creek",
                    DisplayTitle = "The Testers",
                    DisplaySubtitle = "Willow Creek",
                    DisplayDescription = "Fixture household",
                    DisplayPrimaryMeta = "3 Sims",
                    DisplaySecondaryMeta = "Tester One, Tester Two",
                    DisplayTertiaryMeta = string.Empty
                }
            ]
        };

        var store = new SavePreviewDescriptorStore(cacheDir.Path);
        store.SaveDescriptor(saveFilePath, manifest);
        var reader = new SaveDescriptorPreviewSourceReader(store);

        var snapshot = reader.Read(Path.GetFullPath(saveFilePath), TrayPreviewSnapshotPersistence.BuildRootFingerprint);

        Assert.Equal(PreviewSourceKind.SaveDescriptor, snapshot.SourceKind);
        Assert.Equal(Path.GetFullPath(saveFilePath), snapshot.SourceKey);
        Assert.Single(snapshot.RowDescriptors);
        var row = Assert.Single(snapshot.RowDescriptors);
        Assert.Equal("tray-key-42", row.Group.Key);
        Assert.Equal("The Testers", row.ItemName);
        Assert.Equal("3 sims", row.FileListPreview);
        Assert.NotNull(row.SaveDescriptorEntry);
        Assert.Equal(42UL, row.SaveDescriptorEntry!.HouseholdId);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.RootFingerprint));
    }

    [Fact]
    public void PreviewProjectionEngine_BuildProjectedSnapshot_AuthorFilter_UsesMetadataIndex()
    {
        using var cacheDir = new TempDirectory();
        using var trayDir = new TempDirectory();
        var trayItemPathA = CreateFixtureFile(trayDir.Path, "0x00000001!0x0000000000000010.trayitem");
        var trayItemPathB = CreateFixtureFile(trayDir.Path, "0x00000001!0x0000000000000020.trayitem");

        var metadataFacade = new PreviewMetadataFacade(
            new TrayMetadataIndexStore(cacheDir.Path),
            new StubTrayMetadataService(new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase)
            {
                [trayItemPathA] = new()
                {
                    TrayItemPath = trayItemPathA,
                    Name = "Alpha",
                    CreatorName = "Builder"
                },
                [trayItemPathB] = new()
                {
                    TrayItemPath = trayItemPathB,
                    Name = "Beta",
                    CreatorName = "Other"
                }
            }));
        var engine = new PreviewProjectionEngine(metadataFacade);

        var snapshot = new RootSnapshot
        {
            SourceKind = PreviewSourceKind.TrayRoot,
            SourceKey = trayDir.Path,
            NormalizedTrayRoot = trayDir.Path,
            DirectoryWriteUtcTicks = Directory.GetLastWriteTimeUtc(trayDir.Path).Ticks,
            RootFingerprint = "root-fixture",
            CachedAtUtc = DateTime.UtcNow,
            RowDescriptors =
            [
                CreateRow("0x0000000000000010", trayItemPathA, "Alpha"),
                CreateRow("0x0000000000000020", trayItemPathB, "Beta")
            ]
        };

        var projected = engine.BuildProjectedSnapshot(
            new SimsTrayPreviewRequest
            {
                PreviewSource = PreviewSourceRef.ForTrayRoot(trayDir.Path),
                AuthorFilter = "Builder",
                PageSize = 50
            },
            snapshot,
            cacheKey: "fixture-cache",
            CancellationToken.None);

        Assert.Equal("fixture-cache", projected.CacheKey);
        Assert.Single(projected.RowDescriptors);
        Assert.Equal("0x0000000000000010", projected.RowDescriptors[0].Group.Key);
        Assert.Equal(1, projected.Summary.TotalItems);
    }

    [Fact]
    public void TrayPreviewSnapshotPersistence_RoundTripsRootSnapshot()
    {
        var childGroup = new GroupAccumulator("0xchild")
        {
            ItemName = "Member 1",
            TrayInstanceId = "0xchild",
            TrayItemPath = @"C:\tray\member.sgi",
            FileCount = 1,
            TotalBytes = 12,
            LatestWriteTimeUtc = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };
        childGroup.Extensions.Add(".sgi");
        childGroup.ResourceTypes.Add("0x00000013");
        childGroup.FileNames.Add("member.sgi");
        childGroup.SourceFiles.Add(@"C:\tray\member.sgi");

        var rootGroup = new GroupAccumulator("0xroot")
        {
            ItemName = "Fixture Household",
            TrayInstanceId = "0xroot",
            TrayItemPath = @"C:\tray\fixture.trayitem",
            HasHouseholdAnchorFile = true,
            FileCount = 2,
            TotalBytes = 64,
            LatestWriteTimeUtc = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };
        rootGroup.Extensions.Add(".trayitem");
        rootGroup.ResourceTypes.Add("0x00000001");
        rootGroup.FileNames.Add("fixture.trayitem");
        rootGroup.SourceFiles.Add(@"C:\tray\fixture.trayitem");

        var snapshot = new RootSnapshot
        {
            SourceKind = PreviewSourceKind.TrayRoot,
            SourceKey = @"C:\tray",
            NormalizedTrayRoot = @"C:\tray",
            DirectoryWriteUtcTicks = 1234,
            RootFingerprint = "fingerprint",
            CachedAtUtc = DateTime.UtcNow,
            RowDescriptors =
            [
                new PreviewRowDescriptor
                {
                    Group = rootGroup,
                    ChildGroups = [childGroup],
                    PresetType = "Household",
                    ItemName = "Fixture Household",
                    FileListPreview = "fixture.trayitem|member.sgi",
                    NormalizedFallbackSearchText = "fixturehousehold",
                    FileCount = 3,
                    TotalBytes = 76,
                    LatestWriteTimeLocal = new DateTime(2025, 1, 2, 11, 4, 5, DateTimeKind.Local)
                }
            ]
        };

        var persisted = TrayPreviewSnapshotPersistence.ToRootSnapshotRecord(snapshot);
        var restored = TrayPreviewSnapshotPersistence.CreateRootSnapshot(persisted);

        Assert.Equal(snapshot.RootFingerprint, restored.RootFingerprint);
        Assert.Single(restored.RowDescriptors);
        var restoredRow = Assert.Single(restored.RowDescriptors);
        Assert.Equal("0xroot", restoredRow.Group.Key);
        Assert.Single(restoredRow.ChildGroups);
        Assert.Equal("0xchild", restoredRow.ChildGroups[0].Key);
        Assert.Contains(".trayitem", restoredRow.Group.Extensions);
        Assert.Contains(".sgi", restoredRow.ChildGroups[0].Extensions);
    }

    private static string CreateFixtureFile(string rootPath, string fileName)
    {
        var filePath = Path.Combine(rootPath, fileName);
        File.WriteAllText(filePath, "fixture");
        return Path.GetFullPath(filePath);
    }

    private static PreviewRowDescriptor CreateRow(string key, string trayItemPath, string title)
    {
        var group = new GroupAccumulator(key)
        {
            ItemName = title,
            TrayInstanceId = key,
            TrayItemPath = trayItemPath,
            HasHouseholdAnchorFile = true,
            FileCount = 1,
            TotalBytes = 16,
            LatestWriteTimeUtc = DateTime.UtcNow
        };
        group.Extensions.Add(".trayitem");
        group.FileNames.Add(Path.GetFileName(trayItemPath));
        group.SourceFiles.Add(trayItemPath);

        return new PreviewRowDescriptor
        {
            Group = group,
            ChildGroups = Array.Empty<GroupAccumulator>(),
            PresetType = "Lot",
            ItemName = title,
            FileListPreview = Path.GetFileName(trayItemPath),
            NormalizedFallbackSearchText = title.ToLowerInvariant(),
            FileCount = 1,
            TotalBytes = 16,
            LatestWriteTimeLocal = DateTime.Now
        };
    }

    private sealed class StubTrayMetadataService : ITrayMetadataService
    {
        private readonly IReadOnlyDictionary<string, TrayMetadataResult> _results;

        public StubTrayMetadataService(IReadOnlyDictionary<string, TrayMetadataResult> results)
        {
            _results = results;
        }

        public Task<IReadOnlyDictionary<string, TrayMetadataResult>> GetMetadataAsync(
            IReadOnlyCollection<string> trayItemPaths,
            CancellationToken cancellationToken = default)
        {
            var resolved = trayItemPaths
                .Where(path => _results.ContainsKey(path))
                .ToDictionary(path => path, path => _results[path], StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IReadOnlyDictionary<string, TrayMetadataResult>>(resolved);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"preview-helper-{Guid.NewGuid():N}");
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
