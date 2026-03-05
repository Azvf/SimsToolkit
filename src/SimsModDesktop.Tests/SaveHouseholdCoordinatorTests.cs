using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Models;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.Infrastructure.Saves;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.SaveData.Services;

namespace SimsModDesktop.Tests;

public sealed class SaveHouseholdCoordinatorTests
{
    [Fact]
    public async Task BuildPreviewCacheAsync_WhenRound2Disabled_UsesSingleWorkerFallback()
    {
        var builder = new RecordingSavePreviewCacheBuilder();
        var coordinator = CreateCoordinator(builder, new Dictionary<string, object?>
        {
            ["Performance.Round2.SavePreviewParallelEnabled"] = false
        });

        var result = await coordinator.BuildPreviewCacheAsync("slot_00000001.save");

        Assert.True(result.Succeeded);
        Assert.NotNull(builder.LastOptions);
        Assert.Equal(1, builder.LastOptions!.WorkerCount);
        Assert.True(builder.LastOptions.ContinueOnItemFailure);
    }

    [Fact]
    public async Task BuildPreviewCacheAsync_WhenRound2Enabled_UsesDefaultBuilderOptions()
    {
        var builder = new RecordingSavePreviewCacheBuilder();
        var coordinator = CreateCoordinator(builder, new Dictionary<string, object?>
        {
            ["Performance.Round2.SavePreviewParallelEnabled"] = true
        });

        var result = await coordinator.BuildPreviewCacheAsync("slot_00000002.save");

        Assert.True(result.Succeeded);
        Assert.Null(builder.LastOptions);
    }

    private static SaveHouseholdCoordinator CreateCoordinator(
        RecordingSavePreviewCacheBuilder builder,
        IReadOnlyDictionary<string, object?> configValues)
    {
        return new SaveHouseholdCoordinator(
            new StubSaveCatalogService(),
            new StubSaveHouseholdReader(),
            new StubHouseholdTrayExporter(),
            new StubSavePreviewCacheStore(),
            builder,
            new StubTrayMetadataService(),
            new StubSimsTrayPreviewService(),
            new StubConfigurationProvider(configValues));
    }

    private sealed class RecordingSavePreviewCacheBuilder : ISavePreviewCacheBuilder
    {
        public SavePreviewBuildOptions? LastOptions { get; private set; }

        public Task<SavePreviewCacheBuildResult> BuildAsync(
            string saveFilePath,
            IProgress<SavePreviewCacheBuildProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("This overload is not expected in coordinator tests.");
        }

        public Task<SavePreviewCacheBuildResult> BuildAsync(
            string saveFilePath,
            SavePreviewBuildOptions? options,
            IProgress<SavePreviewCacheBuildProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(new SavePreviewCacheBuildResult
            {
                Succeeded = true,
                CacheRootPath = "cache-root"
            });
        }
    }

    private sealed class StubConfigurationProvider : IConfigurationProvider
    {
        private readonly IReadOnlyDictionary<string, object?> _values;

        public StubConfigurationProvider(IReadOnlyDictionary<string, object?> values)
        {
            _values = values;
        }

        public Task<T?> GetConfigurationAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (_values.TryGetValue(key, out var value) && value is T typed)
            {
                return Task.FromResult<T?>(typed);
            }

            return Task.FromResult<T?>(default);
        }

        public Task SetConfigurationAsync<T>(string key, T value, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_values.ContainsKey(key));
        }

        public Task<bool> RemoveConfigurationAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<IReadOnlyList<string>> GetAllKeysAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(_values.Keys.ToArray());
        }

        public Task<bool> IsPlatformSpecificAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public string GetPlatformSpecificPrefix()
        {
            return string.Empty;
        }

        public T? GetDefaultValue<T>(string key)
        {
            return default;
        }

        public Task<bool> ResetToDefaultAsync(string key, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }

        public Task<IReadOnlyDictionary<string, object?>> GetConfigurationsAsync(
            IReadOnlyList<string> keys,
            CancellationToken cancellationToken = default)
        {
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                values[key] = _values.TryGetValue(key, out var value) ? value : null;
            }

            return Task.FromResult<IReadOnlyDictionary<string, object?>>(values);
        }

        public Task SetConfigurationsAsync(
            IReadOnlyDictionary<string, object> configurations,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubSaveCatalogService : ISaveCatalogService
    {
        public IReadOnlyList<SaveFileEntry> GetPrimarySaveFiles(string savesRootPath)
        {
            return Array.Empty<SaveFileEntry>();
        }
    }

    private sealed class StubSaveHouseholdReader : ISaveHouseholdReader
    {
        public SaveHouseholdSnapshot Load(string saveFilePath)
        {
            return new SaveHouseholdSnapshot { SavePath = saveFilePath };
        }
    }

    private sealed class StubHouseholdTrayExporter : IHouseholdTrayExporter
    {
        public SaveHouseholdExportResult Export(SaveHouseholdExportRequest request)
        {
            return new SaveHouseholdExportResult();
        }
    }

    private sealed class StubSavePreviewCacheStore : ISavePreviewCacheStore
    {
        public string GetCacheRootPath(string saveFilePath)
        {
            return string.Empty;
        }

        public bool IsCurrent(string saveFilePath, SavePreviewCacheManifest manifest)
        {
            return false;
        }

        public bool TryLoad(string saveFilePath, out SavePreviewCacheManifest manifest)
        {
            manifest = new SavePreviewCacheManifest();
            return false;
        }

        public void Save(string saveFilePath, SavePreviewCacheManifest manifest)
        {
        }

        public void Clear(string saveFilePath)
        {
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

    private sealed class StubSimsTrayPreviewService : ISimsTrayPreviewService
    {
        public Task<SimsTrayPreviewSummary> BuildSummaryAsync(
            SimsTrayPreviewRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SimsTrayPreviewSummary());
        }

        public Task<SimsTrayPreviewPage> BuildPageAsync(
            SimsTrayPreviewRequest request,
            int pageIndex,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SimsTrayPreviewPage());
        }

        public void Invalidate(string? trayRootPath = null)
        {
        }
    }
}
