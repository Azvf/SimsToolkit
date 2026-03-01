using SimsModDesktop.Models;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.SaveData.Services;
using SimsModDesktop.Services;

namespace SimsModDesktop.Application.Saves;

public sealed class SaveHouseholdCoordinator : ISaveHouseholdCoordinator
{
    private readonly ISaveCatalogService _saveCatalogService;
    private readonly ISaveHouseholdReader _saveHouseholdReader;
    private readonly IHouseholdTrayExporter _householdTrayExporter;
    private readonly ITrayMetadataService _trayMetadataService;
    private readonly ISimsTrayPreviewService _simsTrayPreviewService;
    private readonly Dictionary<string, SaveHouseholdSnapshot> _snapshotCache =
        new(StringComparer.OrdinalIgnoreCase);

    public SaveHouseholdCoordinator(
        ISaveCatalogService saveCatalogService,
        ISaveHouseholdReader saveHouseholdReader,
        IHouseholdTrayExporter householdTrayExporter,
        ITrayMetadataService trayMetadataService,
        ISimsTrayPreviewService simsTrayPreviewService)
    {
        _saveCatalogService = saveCatalogService;
        _saveHouseholdReader = saveHouseholdReader;
        _householdTrayExporter = householdTrayExporter;
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
            _snapshotCache[NormalizePath(saveFilePath)] = snapshot;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
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
            RollbackExport(result.ExportDirectory);
            return Failed("The export did not produce a .trayitem file.");
        }

        try
        {
            var metadata = _trayMetadataService
                .GetMetadataAsync(new[] { trayItemPath })
                .GetAwaiter()
                .GetResult();
            if (metadata.Count == 0)
            {
                RollbackExport(result.ExportDirectory);
                return Failed("The generated .trayitem could not be parsed.");
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
                RollbackExport(result.ExportDirectory);
                return Failed("The exported bundle did not pass local tray preview validation.");
            }

            return result;
        }
        catch (Exception ex)
        {
            RollbackExport(result.ExportDirectory);
            return Failed(ex.Message);
        }
    }

    private static void RollbackExport(string exportDirectory)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(exportDirectory) && Directory.Exists(exportDirectory))
            {
                Directory.Delete(exportDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static string NormalizePath(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Path.GetFullPath(value.Trim());
    }

    private static SaveHouseholdExportResult Failed(string error)
    {
        return new SaveHouseholdExportResult
        {
            Succeeded = false,
            Error = error,
            WrittenFiles = Array.Empty<string>(),
            Warnings = Array.Empty<string>()
        };
    }
}
