using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.SaveData.Services;

public interface ISaveCatalogService
{
    IReadOnlyList<SaveFileEntry> GetPrimarySaveFiles(string savesRootPath);
}
