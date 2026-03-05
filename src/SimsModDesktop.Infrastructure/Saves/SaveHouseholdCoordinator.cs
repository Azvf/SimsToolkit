using SimsModDesktop.Application.Services;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.SaveData.Services;

namespace SimsModDesktop.Infrastructure.Saves;

public sealed class SaveHouseholdCoordinator : ISaveHouseholdCoordinator
{
    private readonly ISaveCatalogService _saveCatalogService;
    private readonly ISaveHouseholdReader _saveHouseholdReader;
    private readonly IHouseholdTrayExporter _householdTrayExporter;
    private readonly ISavePreviewCacheStore _savePreviewCacheStore;
    private readonly ISavePreviewCacheBuilder _savePreviewCacheBuilder;
    private readonly ITrayMetadataService _trayMetadataService;
    private readonly ISimsTrayPreviewService _simsTrayPreviewService;
    private readonly IConfigurationProvider _configurationProvider;
    private readonly Dictionary<string, SaveHouseholdSnapshot> _snapshotCache =
        new(StringComparer.OrdinalIgnoreCase);

    public SaveHouseholdCoordinator(
        ISaveCatalogService saveCatalogService,
        ISaveHouseholdReader saveHouseholdReader,
        IHouseholdTrayExporter householdTrayExporter,
        ISavePreviewCacheStore savePreviewCacheStore,
        ISavePreviewCacheBuilder savePreviewCacheBuilder,
        ITrayMetadataService trayMetadataService,
        ISimsTrayPreviewService simsTrayPreviewService,
        IConfigurationProvider configurationProvider)
    {
        _saveCatalogService = saveCatalogService;
        _saveHouseholdReader = saveHouseholdReader;
        _householdTrayExporter = householdTrayExporter;
        _savePreviewCacheStore = savePreviewCacheStore;
        _savePreviewCacheBuilder = savePreviewCacheBuilder;
        _trayMetadataService = trayMetadataService;
        _simsTrayPreviewService = simsTrayPreviewService;
        _configurationProvider = configurationProvider;
    }

    public IReadOnlyList<SaveFileEntry> GetSaveFiles(string savesRootPath)
    {
        return _saveCatalogService.GetPrimarySaveFiles(savesRootPath);
    }

    public bool TryLoadHouseholds(string saveFilePath, out SaveHouseholdSnapshot? snapshot, out string error)
    {
        snapshot = null;
        error = string.Empty;

        try
        {
            snapshot = _saveHouseholdReader.Load(saveFilePath);
            _snapshotCache[NormalizePath(saveFilePath)] = snapshot;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryGetPreviewCacheManifest(string saveFilePath, out SavePreviewCacheManifest manifest)
    {
        return _savePreviewCacheStore.TryLoad(saveFilePath, out manifest);
    }

    public bool IsPreviewCacheCurrent(string saveFilePath, SavePreviewCacheManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return _savePreviewCacheStore.IsCurrent(saveFilePath, manifest);
    }

    public string GetPreviewCacheRoot(string saveFilePath)
    {
        return _savePreviewCacheStore.GetCacheRootPath(saveFilePath);
    }

    public async Task<SavePreviewCacheBuildResult> BuildPreviewCacheAsync(
        string saveFilePath,
        IProgress<SavePreviewCacheBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var round2ParallelEnabled = await _configurationProvider
            .GetConfigurationAsync<bool?>("Performance.Round2.SavePreviewParallelEnabled", cancellationToken)
            .ConfigureAwait(false);
        var options = round2ParallelEnabled == false
            ? new SavePreviewBuildOptions
            {
                WorkerCount = 1,
                ContinueOnItemFailure = true
            }
            : null;
        return await _savePreviewCacheBuilder
            .BuildAsync(saveFilePath, options, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public void ClearPreviewCache(string saveFilePath)
    {
        _savePreviewCacheStore.Clear(saveFilePath);
    }

    public SaveHouseholdExportResult Export(SaveHouseholdExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = _householdTrayExporter.Export(request);
        if (!result.Succeeded)
        {
            return result;
        }

        var trayItemPath = result.WrittenFiles.FirstOrDefault(path => path.EndsWith(".trayitem", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(trayItemPath))
        {
            return WithWarning(result, "The export did not produce a .trayitem file.");
        }

        try
        {
            var metadata = _trayMetadataService
                .GetMetadataAsync(new[] { trayItemPath })
                .GetAwaiter()
                .GetResult();
            if (metadata.Count == 0)
            {
                return WithWarning(result, "The generated .trayitem could not be parsed.");
            }

            var summary = _simsTrayPreviewService
                .BuildSummaryAsync(new SimsTrayPreviewRequest
                {
                    TrayPath = result.ExportDirectory
                })
                .GetAwaiter()
                .GetResult();
            if (summary.TotalItems < 1)
            {
                return WithWarning(result, "The exported bundle did not pass local tray preview validation.");
            }

            return result;
        }
        catch (Exception ex)
        {
            return WithWarning(result, $"Post-export validation skipped: {ex.Message}");
        }
    }

    private static SaveHouseholdExportResult WithWarning(SaveHouseholdExportResult result, string warning)
    {
        var warnings = result.Warnings is { Count: > 0 }
            ? result.Warnings.Concat(new[] { warning }).ToArray()
            : new[] { warning };
        return new SaveHouseholdExportResult
        {
            Succeeded = result.Succeeded,
            ExportDirectory = result.ExportDirectory,
            InstanceIdHex = result.InstanceIdHex,
            WrittenFiles = result.WrittenFiles,
            Warnings = warnings,
            Error = result.Error
        };
    }

    private static string NormalizePath(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Path.GetFullPath(value.Trim());
    }
}
