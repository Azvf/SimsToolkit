using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.SaveData.Services;

namespace SimsModDesktop.Tests;

public sealed class SavePreviewCacheBuilderTests
{
    [Fact]
    public async Task BuildAsync_WhenExporterThrows_ContinueOnItemFailure_StillBuildsManifest()
    {
        using var temp = new TempDirectory("save-preview-builder");
        var saveFilePath = Path.Combine(temp.Path, "slot_00000001.save");
        File.WriteAllBytes(saveFilePath, [1, 2, 3, 4]);

        var snapshot = BuildSnapshot(saveFilePath, [1, 2]);
        var reader = new FakeReader(snapshot);
        var exporter = new FakeExporter(
            request =>
            {
                if (request.HouseholdId == 1)
                {
                    throw new InvalidOperationException("export-fail-1");
                }

                return new SaveHouseholdExportResult
                {
                    Succeeded = true,
                    InstanceIdHex = "0x2",
                    WrittenFiles = new[] { Path.Combine(temp.Path, "ok.trayitem") }
                };
            });
        var store = new FakeStore(Path.Combine(temp.Path, "cache"));
        var builder = new SavePreviewCacheBuilder(reader, exporter, store, NullLogger<SavePreviewCacheBuilder>.Instance);

        var result = await builder.BuildAsync(
            saveFilePath,
            new SavePreviewBuildOptions
            {
                WorkerCount = 1,
                ContinueOnItemFailure = true
            });

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Manifest);
        Assert.Equal(1, result.Manifest!.ReadyHouseholdCount);
        Assert.Equal(1, result.Manifest.FailedHouseholdCount);
        Assert.Equal(2, result.Manifest.Entries.Count);
        Assert.Equal("Failed", result.Manifest.Entries[0].BuildState);
        Assert.Equal("Ready", result.Manifest.Entries[1].BuildState);
    }

    [Fact]
    public async Task BuildAsync_WhenExporterThrows_AndFailFastEnabled_StopsRemainingItems()
    {
        using var temp = new TempDirectory("save-preview-builder");
        var saveFilePath = Path.Combine(temp.Path, "slot_00000002.save");
        File.WriteAllBytes(saveFilePath, [1, 2, 3, 4]);

        var snapshot = BuildSnapshot(saveFilePath, [11, 22]);
        var reader = new FakeReader(snapshot);
        var exporter = new FakeExporter(
            request =>
            {
                if (request.HouseholdId == 11)
                {
                    throw new InvalidOperationException("export-fail-11");
                }

                return new SaveHouseholdExportResult
                {
                    Succeeded = true,
                    InstanceIdHex = "0x22",
                    WrittenFiles = new[] { Path.Combine(temp.Path, "ok.trayitem") }
                };
            });
        var store = new FakeStore(Path.Combine(temp.Path, "cache"));
        var builder = new SavePreviewCacheBuilder(reader, exporter, store, NullLogger<SavePreviewCacheBuilder>.Instance);

        var result = await builder.BuildAsync(
            saveFilePath,
            new SavePreviewBuildOptions
            {
                WorkerCount = 1,
                ContinueOnItemFailure = false
            });

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Manifest);
        Assert.Equal(1, exporter.CallCount);
        Assert.Equal(1, result.Manifest!.FailedHouseholdCount);
        Assert.Equal(2, result.Manifest.Entries.Count);
        Assert.Equal("Failed", result.Manifest.Entries[0].BuildState);
        Assert.Equal("Failed", result.Manifest.Entries[1].BuildState);
        Assert.Equal("Build interrupted before household was processed.", result.Manifest.Entries[1].LastError);
    }

    private static SaveHouseholdSnapshot BuildSnapshot(string saveFilePath, IReadOnlyList<ulong> householdIds)
    {
        var households = householdIds
            .Select(id => new SaveHouseholdItem
            {
                HouseholdId = id,
                Name = $"Household-{id}",
                HomeZoneName = "Zone",
                HouseholdSize = 1,
                CanExport = true
            })
            .ToArray();
        return new SaveHouseholdSnapshot
        {
            SavePath = saveFilePath,
            Households = households
        };
    }

    private sealed class FakeReader : ISaveHouseholdReader
    {
        private readonly SaveHouseholdSnapshot _snapshot;

        public FakeReader(SaveHouseholdSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public SaveHouseholdSnapshot Load(string saveFilePath)
        {
            return _snapshot;
        }
    }

    private sealed class FakeExporter : IHouseholdTrayExporter
    {
        private readonly Func<SaveHouseholdExportRequest, SaveHouseholdExportResult> _factory;

        public FakeExporter(Func<SaveHouseholdExportRequest, SaveHouseholdExportResult> factory)
        {
            _factory = factory;
        }

        public int CallCount { get; private set; }

        public SaveHouseholdExportResult Export(SaveHouseholdExportRequest request)
        {
            CallCount++;
            return _factory(request);
        }
    }

    private sealed class FakeStore : ISavePreviewCacheStore
    {
        private readonly string _cacheRoot;
        private SavePreviewCacheManifest? _manifest;

        public FakeStore(string cacheRoot)
        {
            _cacheRoot = cacheRoot;
        }

        public string GetCacheRootPath(string saveFilePath)
        {
            return _cacheRoot;
        }

        public bool IsCurrent(string saveFilePath, SavePreviewCacheManifest manifest)
        {
            return false;
        }

        public bool TryLoad(string saveFilePath, out SavePreviewCacheManifest manifest)
        {
            manifest = _manifest ?? new SavePreviewCacheManifest();
            return _manifest is not null;
        }

        public void Save(string saveFilePath, SavePreviewCacheManifest manifest)
        {
            _manifest = manifest;
        }

        public void Clear(string saveFilePath)
        {
            _manifest = null;
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory(string prefix)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
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
