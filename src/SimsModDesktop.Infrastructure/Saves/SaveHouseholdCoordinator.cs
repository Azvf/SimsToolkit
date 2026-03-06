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
    private readonly ISavePreviewDescriptorStore _savePreviewDescriptorStore;
    private readonly ISavePreviewDescriptorBuilder _savePreviewDescriptorBuilder;
    private readonly ISavePreviewArtifactProvider _savePreviewArtifactProvider;
    private readonly ITrayMetadataService _trayMetadataService;
    private readonly ISimsTrayPreviewService _simsTrayPreviewService;

    public SaveHouseholdCoordinator(
        ISaveCatalogService saveCatalogService,
        ISaveHouseholdReader saveHouseholdReader,
        IHouseholdTrayExporter householdTrayExporter,
        ISavePreviewDescriptorStore savePreviewDescriptorStore,
        ISavePreviewDescriptorBuilder savePreviewDescriptorBuilder,
        ISavePreviewArtifactProvider savePreviewArtifactProvider,
        ITrayMetadataService trayMetadataService,
        ISimsTrayPreviewService simsTrayPreviewService)
    {
        _saveCatalogService = saveCatalogService;
        _saveHouseholdReader = saveHouseholdReader;
        _householdTrayExporter = householdTrayExporter;
        _savePreviewDescriptorStore = savePreviewDescriptorStore;
        _savePreviewDescriptorBuilder = savePreviewDescriptorBuilder;
        _savePreviewArtifactProvider = savePreviewArtifactProvider;
        _trayMetadataService = trayMetadataService;
        _simsTrayPreviewService = simsTrayPreviewService;
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
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryGetPreviewDescriptor(string saveFilePath, out SavePreviewDescriptorManifest manifest)
    {
        return _savePreviewDescriptorStore.TryLoadDescriptor(saveFilePath, out manifest);
    }

    public bool IsPreviewDescriptorCurrent(string saveFilePath, SavePreviewDescriptorManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        return _savePreviewDescriptorStore.IsDescriptorCurrent(saveFilePath, manifest);
    }

    public PreviewSourceRef GetPreviewSource(string saveFilePath)
    {
        return PreviewSourceRef.ForSaveDescriptor(NormalizePath(saveFilePath));
    }

    public async Task<SavePreviewDescriptorBuildResult> BuildPreviewDescriptorAsync(
        string saveFilePath,
        IProgress<SavePreviewDescriptorBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await _savePreviewDescriptorBuilder
            .BuildAsync(saveFilePath, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<string?> EnsurePreviewArtifactAsync(
        string saveFilePath,
        string householdKey,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        return _savePreviewArtifactProvider.EnsureBundleAsync(
            saveFilePath,
            householdKey,
            purpose,
            cancellationToken);
    }

    public void ClearPreviewData(string saveFilePath)
    {
        _savePreviewDescriptorStore.ClearDescriptor(saveFilePath);
        _savePreviewArtifactProvider.Clear(saveFilePath);
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
                    PreviewSource = PreviewSourceRef.ForTrayRoot(result.ExportDirectory)
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
