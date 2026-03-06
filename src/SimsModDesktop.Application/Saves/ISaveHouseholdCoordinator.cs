using SimsModDesktop.Application.TrayPreview;
using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.Application.Saves;

public interface ISaveHouseholdCoordinator
{
    IReadOnlyList<SaveFileEntry> GetSaveFiles(string savesRootPath);
    bool TryLoadHouseholds(string saveFilePath, out SaveHouseholdSnapshot? snapshot, out string error);
    bool TryGetPreviewDescriptor(string saveFilePath, out SavePreviewDescriptorManifest manifest);
    bool IsPreviewDescriptorCurrent(string saveFilePath, SavePreviewDescriptorManifest manifest);
    PreviewSourceRef GetPreviewSource(string saveFilePath);
    Task<SavePreviewDescriptorBuildResult> BuildPreviewDescriptorAsync(
        string saveFilePath,
        IProgress<SavePreviewDescriptorBuildProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task<string?> EnsurePreviewArtifactAsync(
        string saveFilePath,
        string householdKey,
        string purpose,
        CancellationToken cancellationToken = default);
    void ClearPreviewData(string saveFilePath);
    SaveHouseholdExportResult Export(SaveHouseholdExportRequest request);
}
