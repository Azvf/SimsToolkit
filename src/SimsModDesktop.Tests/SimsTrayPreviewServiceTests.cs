using SimsModDesktop.Models;
using SimsModDesktop.Services;

namespace SimsModDesktop.Tests;

public sealed class SimsTrayPreviewServiceTests
{
    [Fact]
    public async Task BuildPageAsync_BuildSizeFilter_FiltersLotsAndRoomsByParsedDimensions()
    {
        using var trayDir = new TempDirectory();
        CreateTrayFiles(trayDir.Path, "villa_small_20x20", ".trayitem", ".blueprint");
        CreateTrayFiles(trayDir.Path, "villa_large_50x40", ".trayitem", ".blueprint");

        var service = new SimsTrayPreviewService();
        var request = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            PresetTypeFilter = "Lot",
            BuildSizeFilter = "20 x 20",
            HouseholdSizeFilter = "All",
            PageSize = 50
        };

        var summary = await service.BuildSummaryAsync(request);
        var page = await service.BuildPageAsync(request, pageIndex: 1);

        Assert.Equal(1, summary.TotalItems);
        var item = Assert.Single(page.Items);
        Assert.Contains("20x20", item.ItemName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildPageAsync_HouseholdSizeFilter_FiltersByParsedMemberCount()
    {
        using var trayDir = new TempDirectory();
        CreateTrayFiles(trayDir.Path, "family_3sims", ".trayitem", ".householdbinary");
        CreateTrayFiles(trayDir.Path, "family_6sims", ".trayitem", ".householdbinary");

        var service = new SimsTrayPreviewService();
        var request = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            PresetTypeFilter = "Household",
            BuildSizeFilter = "All",
            HouseholdSizeFilter = "3",
            PageSize = 50
        };

        var summary = await service.BuildSummaryAsync(request);
        var page = await service.BuildPageAsync(request, pageIndex: 1);

        Assert.Equal(1, summary.TotalItems);
        var item = Assert.Single(page.Items);
        Assert.Contains("3sims", item.ItemName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildPageAsync_PopulatesThumbnailMetadataAndCleansMissingKeys()
    {
        using var trayDir = new TempDirectory();
        CreateTrayFiles(trayDir.Path, "0x00000001!0x0000000000000010", ".trayitem", ".bpi");

        var cleanupRecorder = new RecordingTrayThumbnailService();
        var service = new SimsTrayPreviewService(cleanupRecorder);
        var request = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            PageSize = 50
        };

        var page = await service.BuildPageAsync(request, pageIndex: 1);

        var item = Assert.Single(page.Items);
        Assert.Equal(Path.GetFullPath(trayDir.Path), item.TrayRootPath);
        Assert.Equal("0x0000000000000010", item.TrayInstanceId);
        Assert.False(string.IsNullOrWhiteSpace(item.ContentFingerprint));
        Assert.Equal(2, item.SourceFilePaths.Count);
        Assert.Equal(Path.GetFullPath(trayDir.Path), cleanupRecorder.LastTrayRootPath);
        Assert.Contains(item.TrayItemKey, cleanupRecorder.LastLiveKeys);
    }

    [Fact]
    public async Task BuildPageAsync_UsesTrayMetadataForDefaultDisplayFields()
    {
        using var trayDir = new TempDirectory();
        CreateTrayFiles(trayDir.Path, "0x00000001!0x0000000000000099", ".trayitem", ".householdbinary");
        var trayItemPath = Path.Combine(trayDir.Path, "0x00000001!0x0000000000000099.trayitem");

        var service = new SimsTrayPreviewService(
            trayThumbnailService: null,
            trayMetadataService: new FakeTrayMetadataService(new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase)
            {
                [trayItemPath] = new()
                {
                    TrayItemPath = trayItemPath,
                    ItemType = "Household",
                    Name = "Test Household",
                    Description = "Metadata description",
                    CreatorName = "Builder",
                    CreatorId = "12345",
                    FamilySize = 2,
                    Members =
                    [
                        new TrayMemberDisplayMetadata { SlotIndex = 1, FullName = "Alice Sim", Subtitle = "Adult • Female", Detail = "Human" },
                        new TrayMemberDisplayMetadata { SlotIndex = 2, FullName = "Bob Sim", Subtitle = "Adult • Male", Detail = "Human" }
                    ]
                }
            }));
        var request = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            PageSize = 50
        };

        var page = await service.BuildPageAsync(request, pageIndex: 1);

        var item = Assert.Single(page.Items);
        Assert.Equal("Test Household", item.DisplayTitle);
        Assert.Equal("by Builder", item.DisplaySubtitle);
        Assert.Equal("2 Sims", item.DisplayPrimaryMeta);
        Assert.Equal("Alice Sim, Bob Sim", item.DisplaySecondaryMeta);
        Assert.Equal("Metadata description", item.DisplayDescription);
        Assert.Equal("Builder 12345", item.AuthorId);
    }

    [Fact]
    public async Task BuildSummaryAsync_DefersMetadataUntilCurrentPage_WhenNoMetadataDependentFilters()
    {
        using var trayDir = new TempDirectory();
        CreateTrayFiles(trayDir.Path, "0x00000001!0x0000000000000010", ".trayitem", ".bpi");
        CreateTrayFiles(trayDir.Path, "0x00000001!0x0000000000000020", ".trayitem", ".bpi");
        var trayItemPathA = Path.Combine(trayDir.Path, "0x00000001!0x0000000000000010.trayitem");
        var trayItemPathB = Path.Combine(trayDir.Path, "0x00000001!0x0000000000000020.trayitem");

        var metadataService = new RecordingTrayMetadataService(new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase)
        {
            [trayItemPathA] = new()
            {
                TrayItemPath = trayItemPathA,
                Name = "Alpha"
            },
            [trayItemPathB] = new()
            {
                TrayItemPath = trayItemPathB,
                Name = "Beta"
            }
        });
        var service = new SimsTrayPreviewService(
            trayThumbnailService: null,
            trayMetadataService: metadataService);
        var request = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            PageSize = 1
        };

        var summary = await service.BuildSummaryAsync(request);

        Assert.Equal(2, summary.TotalItems);
        Assert.Empty(metadataService.RequestBatches);

        var page = await service.BuildPageAsync(request, pageIndex: 1);

        Assert.Single(page.Items);
        Assert.Single(metadataService.RequestBatches);
        Assert.Single(metadataService.RequestBatches[0]);
    }

    [Fact]
    public async Task BuildSummaryAsync_AuthorFilterOnlyLoadsMissingMetadataAfterPageWarmup()
    {
        using var trayDir = new TempDirectory();
        CreateTrayFiles(trayDir.Path, "0x00000001!0x0000000000000010", ".trayitem", ".bpi");
        CreateTrayFiles(trayDir.Path, "0x00000001!0x0000000000000020", ".trayitem", ".bpi");
        var trayItemPathA = Path.Combine(trayDir.Path, "0x00000001!0x0000000000000010.trayitem");
        var trayItemPathB = Path.Combine(trayDir.Path, "0x00000001!0x0000000000000020.trayitem");

        var metadataService = new RecordingTrayMetadataService(new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase)
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
                CreatorName = "Builder"
            }
        });
        var service = new SimsTrayPreviewService(
            trayThumbnailService: null,
            trayMetadataService: metadataService);
        var baseRequest = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            PageSize = 1
        };

        var basePage = await service.BuildPageAsync(baseRequest, pageIndex: 1);

        Assert.Single(basePage.Items);
        Assert.Single(metadataService.RequestBatches);
        var initialBatch = Assert.Single(metadataService.RequestBatches[0]);
        var expectedMissingPath = string.Equals(initialBatch, Path.GetFullPath(trayItemPathA), StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(trayItemPathB)
            : Path.GetFullPath(trayItemPathA);

        var authorRequest = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            AuthorFilter = "Builder",
            PageSize = 1
        };

        var filteredSummary = await service.BuildSummaryAsync(authorRequest);

        Assert.Equal(2, filteredSummary.TotalItems);
        Assert.Equal(2, metadataService.RequestBatches.Count);
        Assert.Equal(expectedMissingPath, Assert.Single(metadataService.RequestBatches[1]));

        var filteredPage = await service.BuildPageAsync(authorRequest, pageIndex: 1);

        Assert.Single(filteredPage.Items);
        Assert.Equal(2, metadataService.RequestBatches.Count);
    }

    [Fact]
    public async Task BuildSummaryAsync_AuthorFilterReusesPersistedMetadataAcrossServiceRestart()
    {
        using var trayDir = new TempDirectory();
        using var cacheDir = new TempDirectory();
        CreateTrayFiles(trayDir.Path, "0x00000001!0x0000000000000010", ".trayitem", ".bpi");
        CreateTrayFiles(trayDir.Path, "0x00000001!0x0000000000000020", ".trayitem", ".bpi");
        var trayItemPathA = Path.Combine(trayDir.Path, "0x00000001!0x0000000000000010.trayitem");
        var trayItemPathB = Path.Combine(trayDir.Path, "0x00000001!0x0000000000000020.trayitem");
        var metadataResults = new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase)
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
                CreatorName = "Builder"
            }
        };
        var sharedStore = new TrayMetadataIndexStore(cacheDir.Path);

        var warmMetadataService = new RecordingTrayMetadataService(metadataResults);
        var warmService = new SimsTrayPreviewService(
            trayThumbnailService: null,
            trayMetadataService: warmMetadataService,
            metadataIndexStore: sharedStore);
        var warmRequest = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            PageSize = 50
        };

        var warmPage = await warmService.BuildPageAsync(warmRequest, pageIndex: 1);

        Assert.Equal(2, warmPage.Items.Count);
        Assert.Single(warmMetadataService.RequestBatches);
        Assert.Equal(2, warmMetadataService.RequestBatches[0].Count);

        var restartedMetadataService = new RecordingTrayMetadataService(metadataResults);
        var restartedService = new SimsTrayPreviewService(
            trayThumbnailService: null,
            trayMetadataService: restartedMetadataService,
            metadataIndexStore: new TrayMetadataIndexStore(cacheDir.Path));
        var filteredRequest = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            AuthorFilter = "Builder",
            PageSize = 50
        };

        var filteredSummary = await restartedService.BuildSummaryAsync(filteredRequest);
        var filteredPage = await restartedService.BuildPageAsync(filteredRequest, pageIndex: 1);

        Assert.Equal(2, filteredSummary.TotalItems);
        Assert.Equal(2, filteredPage.Items.Count);
        Assert.Empty(restartedMetadataService.RequestBatches);
    }

    [Fact]
    public async Task BuildPageAsync_FoldsAuxiliaryHouseholdSgiIntoPrimaryHouseholdGroup()
    {
        using var trayDir = new TempDirectory();
        CreateTrayFiles(trayDir.Path, "0x00000000!0x001e161ef1390d53", ".householdbinary");
        CreateTrayFiles(trayDir.Path, "0x00000001!0x001e161ef1390d53", ".trayitem");
        CreateTrayFiles(trayDir.Path, "0xa6380902!0x001e161ef1390d53", ".hhi");
        CreateTrayFiles(trayDir.Path, "0x00000013!0x051e161ef1390d54", ".sgi");
        CreateTrayFiles(trayDir.Path, "0x00000023!0x051e161ef1390d55", ".sgi");

        var service = new SimsTrayPreviewService();
        var request = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            PageSize = 50
        };

        var summary = await service.BuildSummaryAsync(request);
        var page = await service.BuildPageAsync(request, pageIndex: 1);

        Assert.Equal(1, summary.TotalItems);
        var item = Assert.Single(page.Items);
        Assert.Equal("0x001e161ef1390d53", item.TrayItemKey);
        Assert.Equal(5, item.FileCount);
        Assert.Equal(5, item.SourceFilePaths.Count);
        Assert.Equal(2, item.ChildItems.Count);
        Assert.Equal("Member 1", item.ChildItems[0].ItemName);
        Assert.Equal("Member 2", item.ChildItems[1].ItemName);
        Assert.Contains(".sgi", item.Extensions, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x00000013", item.ResourceTypes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0x00000023", item.ResourceTypes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildPageAsync_FoldsHighByteShiftedHouseholdMembersAndKeepsThemExpandable()
    {
        using var trayDir = new TempDirectory();
        CreateTrayFiles(trayDir.Path, "0x00000000!0x00be161969c739bf", ".householdbinary");
        CreateTrayFiles(trayDir.Path, "0x00000001!0x00be161969c739bf", ".trayitem");
        CreateTrayFiles(trayDir.Path, "0x65aa1d03!0x00be161969c739bf", ".hhi");
        CreateTrayFiles(trayDir.Path, "0x65aa1d02!0x00be161969c739bf", ".hhi");
        CreateTrayFiles(trayDir.Path, "0x00000033!0x0cbe161969c739c2", ".sgi");
        CreateTrayFiles(trayDir.Path, "0x00000043!0x0cbe161969c739c3", ".sgi");

        var service = new SimsTrayPreviewService();
        var request = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            PageSize = 50
        };

        var summary = await service.BuildSummaryAsync(request);
        var page = await service.BuildPageAsync(request, pageIndex: 1);

        Assert.Equal(1, summary.TotalItems);
        var item = Assert.Single(page.Items);
        Assert.Equal("0x00be161969c739bf", item.TrayItemKey);
        Assert.Equal(6, item.FileCount);
        Assert.Equal(2, item.ChildItems.Count);
        Assert.Equal("0x0cbe161969c739c2", item.ChildItems[0].TrayItemKey);
        Assert.Equal("0x0cbe161969c739c3", item.ChildItems[1].TrayItemKey);
        Assert.Equal("Member 3", item.ChildItems[0].ItemName);
        Assert.Equal("Member 4", item.ChildItems[1].ItemName);
    }

    [Fact]
    public async Task BuildPageAsync_FoldsHighByteShiftedHouseholdMembersWhenBridgeKeysAreMissing()
    {
        using var trayDir = new TempDirectory();
        CreateTrayFiles(trayDir.Path, "0x00000000!0x00561617068017e6", ".householdbinary");
        CreateTrayFiles(trayDir.Path, "0x00000001!0x00561617068017e6", ".trayitem");
        CreateTrayFiles(trayDir.Path, "0xda69aa02!0x00561617068017e6", ".hhi");
        CreateTrayFiles(trayDir.Path, "0xda69aa03!0x00561617068017e6", ".hhi");
        CreateTrayFiles(trayDir.Path, "0x00000013!0x05561617068017e7", ".sgi");
        CreateTrayFiles(trayDir.Path, "0x00000023!0x05561617068017ec", ".sgi");
        CreateTrayFiles(trayDir.Path, "0x00000033!0x05561617068017ed", ".sgi");
        CreateTrayFiles(trayDir.Path, "0x00000043!0x05561617068017ee", ".sgi");

        var service = new SimsTrayPreviewService();
        var request = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            PageSize = 50
        };

        var summary = await service.BuildSummaryAsync(request);
        var page = await service.BuildPageAsync(request, pageIndex: 1);

        Assert.Equal(1, summary.TotalItems);
        var item = Assert.Single(page.Items);
        Assert.Equal("0x00561617068017e6", item.TrayItemKey);
        Assert.Equal(8, item.FileCount);
        Assert.Equal(8, item.SourceFilePaths.Count);
        Assert.Equal(4, item.ChildItems.Count);
        Assert.Equal("0x05561617068017e7", item.ChildItems[0].TrayItemKey);
        Assert.Equal("0x05561617068017ec", item.ChildItems[1].TrayItemKey);
        Assert.Equal("0x05561617068017ed", item.ChildItems[2].TrayItemKey);
        Assert.Equal("0x05561617068017ee", item.ChildItems[3].TrayItemKey);
        Assert.Equal("Member 1", item.ChildItems[0].ItemName);
        Assert.Equal("Member 2", item.ChildItems[1].ItemName);
        Assert.Equal("Member 3", item.ChildItems[2].ItemName);
        Assert.Equal("Member 4", item.ChildItems[3].ItemName);
    }

    [Fact]
    public async Task BuildPageAsync_FoldsChainedHouseholdMemberSgiIntoPrimaryHouseholdGroup()
    {
        using var trayDir = new TempDirectory();
        CreateTrayFiles(trayDir.Path, "0x00000000!0x0082161ee12e1026", ".householdbinary");
        CreateTrayFiles(trayDir.Path, "0x00000001!0x0082161ee12e1026", ".trayitem");
        CreateTrayFiles(trayDir.Path, "0x8366a602!0x0082161ee12e1026", ".hhi");
        CreateTrayFiles(trayDir.Path, "0x8366a603!0x0082161ee12e1026", ".hhi");
        CreateTrayFiles(trayDir.Path, "0x00000013!0x0982161ee12e1027", ".sgi");
        CreateTrayFiles(trayDir.Path, "0x00000023!0x0982161ee12e1029", ".sgi");

        var service = new SimsTrayPreviewService();
        var request = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            PageSize = 50
        };

        var summary = await service.BuildSummaryAsync(request);
        var page = await service.BuildPageAsync(request, pageIndex: 1);

        Assert.Equal(1, summary.TotalItems);
        var item = Assert.Single(page.Items);
        Assert.Equal("0x0082161ee12e1026", item.TrayItemKey);
        Assert.Equal(6, item.FileCount);
        Assert.Equal(2, item.ChildItems.Count);
        Assert.Equal("0x0982161ee12e1027", item.ChildItems[0].TrayItemKey);
        Assert.Equal("0x0982161ee12e1029", item.ChildItems[1].TrayItemKey);
        Assert.Equal("Member 1", item.ChildItems[0].ItemName);
        Assert.Equal("Member 2", item.ChildItems[1].ItemName);
    }

    [Fact]
    public async Task BuildPageAsync_DoesNotWaitForThumbnailCleanup()
    {
        using var trayDir = new TempDirectory();
        CreateTrayFiles(trayDir.Path, "0x00000001!0x0000000000000010", ".trayitem", ".bpi");

        var thumbnailService = new BlockingCleanupTrayThumbnailService();
        var service = new SimsTrayPreviewService(thumbnailService);
        var request = new SimsTrayPreviewRequest
        {
            TrayPath = trayDir.Path,
            PageSize = 50
        };

        var loadTask = service.BuildPageAsync(request, pageIndex: 1);
        var completedTask = await Task.WhenAny(loadTask, Task.Delay(TimeSpan.FromSeconds(1)));

        Assert.Same(loadTask, completedTask);

        var page = await loadTask;
        Assert.Single(page.Items);

        var cleanupStarted = await Task.WhenAny(thumbnailService.CleanupStarted, Task.Delay(TimeSpan.FromSeconds(1)));
        Assert.Same(thumbnailService.CleanupStarted, cleanupStarted);
        Assert.Contains("0x0000000000000010", thumbnailService.LastLiveKeys);

        thumbnailService.ReleaseCleanup();
        await thumbnailService.CleanupFinished;
    }

    private static void CreateTrayFiles(string rootPath, string baseName, params string[] extensions)
    {
        foreach (var extension in extensions)
        {
            var filePath = Path.Combine(rootPath, $"{baseName}{extension}");
            File.WriteAllText(filePath, "fixture");
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sims-tray-svc-{Guid.NewGuid():N}");
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

    private sealed class RecordingTrayThumbnailService : ITrayThumbnailService
    {
        public string LastTrayRootPath { get; private set; } = string.Empty;
        public IReadOnlyCollection<string> LastLiveKeys { get; private set; } = Array.Empty<string>();

        public Task<TrayThumbnailResult> GetThumbnailAsync(SimsTrayPreviewItem item, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayThumbnailResult());
        }

        public Task CleanupStaleEntriesAsync(string trayRootPath, IReadOnlyCollection<string> liveItemKeys, CancellationToken cancellationToken = default)
        {
            LastTrayRootPath = Path.GetFullPath(trayRootPath);
            LastLiveKeys = liveItemKeys.ToArray();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTrayMetadataService : ITrayMetadataService
    {
        private readonly IReadOnlyDictionary<string, TrayMetadataResult> _results;

        public FakeTrayMetadataService(IReadOnlyDictionary<string, TrayMetadataResult> results)
        {
            _results = results;
        }

        public Task<IReadOnlyDictionary<string, TrayMetadataResult>> GetMetadataAsync(
            IReadOnlyCollection<string> trayItemPaths,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_results);
        }
    }

    private sealed class RecordingTrayMetadataService : ITrayMetadataService
    {
        private readonly IReadOnlyDictionary<string, TrayMetadataResult> _results;

        public RecordingTrayMetadataService(IReadOnlyDictionary<string, TrayMetadataResult> results)
        {
            _results = results;
        }

        public List<IReadOnlyList<string>> RequestBatches { get; } = new();

        public Task<IReadOnlyDictionary<string, TrayMetadataResult>> GetMetadataAsync(
            IReadOnlyCollection<string> trayItemPaths,
            CancellationToken cancellationToken = default)
        {
            RequestBatches.Add(trayItemPaths.Select(Path.GetFullPath).ToArray());

            var resolved = trayItemPaths
                .Select(Path.GetFullPath)
                .Where(path => _results.ContainsKey(path))
                .ToDictionary(path => path, path => _results[path], StringComparer.OrdinalIgnoreCase);

            return Task.FromResult<IReadOnlyDictionary<string, TrayMetadataResult>>(resolved);
        }
    }

    private sealed class BlockingCleanupTrayThumbnailService : ITrayThumbnailService
    {
        private readonly TaskCompletionSource _cleanupStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cleanupRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyCollection<string> LastLiveKeys { get; private set; } = Array.Empty<string>();
        public Task CleanupStarted => _cleanupStarted.Task;
        public Task CleanupFinished => _cleanupRelease.Task;

        public Task<TrayThumbnailResult> GetThumbnailAsync(SimsTrayPreviewItem item, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TrayThumbnailResult());
        }

        public Task CleanupStaleEntriesAsync(
            string trayRootPath,
            IReadOnlyCollection<string> liveItemKeys,
            CancellationToken cancellationToken = default)
        {
            LastLiveKeys = liveItemKeys.ToArray();
            _cleanupStarted.TrySetResult();
            return _cleanupRelease.Task;
        }

        public void ReleaseCleanup()
        {
            _cleanupRelease.TrySetResult();
        }
    }
}
