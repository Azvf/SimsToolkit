using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.Application.Saves;

public interface ISaveHouseholdCoordinator
{
    IReadOnlyList<SaveFileEntry> GetSaveFiles(string savesRootPath);
    bool TryLoadHouseholds(string saveFilePath, out SaveHouseholdSnapshot? snapshot, out string error);
    SaveHouseholdExportResult Export(SaveHouseholdExportRequest request);
}
