using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.Preview;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Infrastructure.Saves;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.SaveData.Services;

namespace SimsModDesktop.Tests;

public sealed class SaveHouseholdCoordinatorTests
{
    [Fact]
    public async Task BuildPreviewDescriptorAsync_DelegatesToDescriptorBuilder()
    {
        var builder = new RecordingDescriptorBuilder();
        var coordinator = CreateCoordinator(builder: builder);

        var result = await coordinator.BuildPreviewDescriptorAsync("slot_00000001.save");

        Assert.True(result.Succeeded);
        Assert.Equal("slot_00000001.save", builder.LastSaveFilePath);
    }

    [Fact]
    public async Task EnsurePreviewArtifactAsync_DelegatesToArtifactProvider()
    {
        var artifactProvider = new StubSavePreviewArtifactProvider
        {
            Result = "preview-root"
        };
        var coordinator = CreateCoordinator(artifactProvider: artifactProvider);

        var result = await coordinator.EnsurePreviewArtifactAsync("slot_00000003.save", "household-1", "analysis");

        Assert.Equal("preview-root", result);
        Assert.Equal("slot_00000003.save", artifactProvider.LastSaveFilePath);
        Assert.Equal("household-1", artifactProvider.LastHouseholdKey);
        Assert.Equal("analysis", artifactProvider.LastPurpose);
    }

    [Fact]
    public void ClearPreviewData_ClearsDescriptorAndArtifactState()
    {
        var previewStore = new StubSavePreviewDescriptorStore();
        var artifactProvider = new StubSavePreviewArtifactProvider();
        var coordinator = CreateCoordinator(previewStore: previewStore, artifactProvider: artifactProvider);

        coordinator.ClearPreviewData("slot_00000005.save");

        Assert.Equal("slot_00000005.save", previewStore.LastClearedSaveFilePath);
        Assert.Equal("slot_00000005.save", artifactProvider.LastClearedSaveFilePath);
    }

    [Fact]
    public void GetPreviewSource_ReturnsSaveDescriptorSource()
    {
        var coordinator = CreateCoordinator();

        var source = coordinator.GetPreviewSource("slot_00000004.save");

        Assert.Equal(PreviewSourceKind.SaveDescriptor, source.Kind);
        Assert.EndsWith("slot_00000004.save", source.SourceKey, StringComparison.OrdinalIgnoreCase);
    }

    private static SaveHouseholdCoordinator CreateCoordinator(
        RecordingDescriptorBuilder? builder = null,
        StubSavePreviewDescriptorStore? previewStore = null,
        StubSavePreviewArtifactProvider? artifactProvider = null)
    {
        return new SaveHouseholdCoordinator(
            new StubSaveCatalogService(),
            new StubSaveHouseholdReader(),
            new StubHouseholdTrayExporter(),
            previewStore ?? new StubSavePreviewDescriptorStore(),
            builder ?? new RecordingDescriptorBuilder(),
            artifactProvider ?? new StubSavePreviewArtifactProvider(),
            new StubTrayMetadataService(),
            new StubPreviewQueryService());
    }

    private sealed class RecordingDescriptorBuilder : ISavePreviewDescriptorBuilder
    {
        public string? LastSaveFilePath { get; private set; }

        public Task<SavePreviewDescriptorBuildResult> BuildAsync(
            string saveFilePath,
            IProgress<SavePreviewDescriptorBuildProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastSaveFilePath = saveFilePath;
            return Task.FromResult(new SavePreviewDescriptorBuildResult
            {
                Succeeded = true,
                Manifest = new SavePreviewDescriptorManifest
                {
                    SourceSavePath = saveFilePath,
                    DescriptorSchemaVersion = "save-preview-descriptor-v1"
                }
            });
        }
    }

    private sealed class StubSaveCatalogService : ISaveCatalogService
    {
        public IReadOnlyList<SaveFileEntry> GetPrimarySaveFiles(string savesRootPath) => Array.Empty<SaveFileEntry>();
    }

    private sealed class StubSaveHouseholdReader : ISaveHouseholdReader
    {
        public SaveHouseholdSnapshot Load(string saveFilePath) => new() { SavePath = saveFilePath };
    }

    private sealed class StubHouseholdTrayExporter : IHouseholdTrayExporter
    {
        public SaveHouseholdExportResult Export(SaveHouseholdExportRequest request) => new();
    }

    private sealed class StubSavePreviewDescriptorStore : ISavePreviewDescriptorStore
    {
        public string? LastClearedSaveFilePath { get; private set; }

        public bool IsDescriptorCurrent(string saveFilePath, SavePreviewDescriptorManifest manifest) => false;

        public bool TryLoadDescriptor(string saveFilePath, out SavePreviewDescriptorManifest manifest)
        {
            manifest = new SavePreviewDescriptorManifest();
            return false;
        }

        public void SaveDescriptor(string saveFilePath, SavePreviewDescriptorManifest manifest)
        {
        }

        public void ClearDescriptor(string saveFilePath)
        {
            LastClearedSaveFilePath = saveFilePath;
        }
    }

    private sealed class StubSavePreviewArtifactProvider : ISavePreviewArtifactProvider
    {
        public string? Result { get; set; }
        public string? LastSaveFilePath { get; private set; }
        public string? LastHouseholdKey { get; private set; }
        public string? LastPurpose { get; private set; }
        public string? LastClearedSaveFilePath { get; private set; }

        public Task<string?> EnsureBundleAsync(
            string saveFilePath,
            string householdKey,
            string purpose,
            CancellationToken cancellationToken = default)
        {
            LastSaveFilePath = saveFilePath;
            LastHouseholdKey = householdKey;
            LastPurpose = purpose;
            return Task.FromResult(Result);
        }

        public void Clear(string saveFilePath)
        {
            LastClearedSaveFilePath = saveFilePath;
        }
    }

    private sealed class StubTrayMetadataService : ITrayMetadataService
    {
        public Task<IReadOnlyDictionary<string, TrayMetadataResult>> GetMetadataAsync(
            IReadOnlyCollection<string> trayItemPaths,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<string, TrayMetadataResult>>(
                new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private sealed class StubPreviewQueryService : IPreviewQueryService
    {
        public bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result)
        {
            result = null!;
            return false;
        }

        public Task<TrayPreviewLoadResult> LoadAsync(TrayPreviewInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TrayPreviewLoadResult
            {
                Summary = new SimsTrayPreviewSummary { TotalItems = 1 },
                Page = new SimsTrayPreviewPage
                {
                    PageIndex = 1,
                    TotalPages = 1,
                    TotalItems = 1
                },
                LoadedPageCount = 1
            });

        public Task<TrayPreviewPageResult> LoadPageAsync(
            TrayPreviewInput input,
            int requestedPageIndex,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TrayPreviewPageResult
            {
                Page = new SimsTrayPreviewPage
                {
                    PageIndex = requestedPageIndex,
                    TotalPages = 1,
                    TotalItems = 1
                },
                LoadedPageCount = 1,
                FromCache = false
            });

        public void Invalidate(PreviewSourceRef? source = null)
        {
        }

        public void Reset()
        {
        }
    }
}
