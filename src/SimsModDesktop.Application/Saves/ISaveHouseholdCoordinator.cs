using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.Application.Saves;

public interface ISaveHouseholdCoordinator
{
    IReadOnlyList<SaveFileEntry> GetSaveFiles(string savesRootPath);
    bool TryLoadHouseholds(string saveFilePath, out SaveHouseholdSnapshot? snapshot, out string error);
    bool TryGetPreviewCacheManifest(string saveFilePath, out SavePreviewCacheManifest manifest);
    bool IsPreviewCacheCurrent(string saveFilePath, SavePreviewCacheManifest manifest);
    string GetPreviewCacheRoot(string saveFilePath);
    Task<SavePreviewCacheBuildResult> BuildPreviewCacheAsync(
        string saveFilePath,
        IProgress<SavePreviewCacheBuildProgress>? progress = null,
        CancellationToken cancellationToken = default);
    void ClearPreviewCache(string saveFilePath);
    SaveHouseholdExportResult Export(SaveHouseholdExportRequest request);
}
