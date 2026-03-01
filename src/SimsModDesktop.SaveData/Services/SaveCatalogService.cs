using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.SaveData.Services;

public sealed class SaveCatalogService : ISaveCatalogService
{
    public IReadOnlyList<SaveFileEntry> GetPrimarySaveFiles(string savesRootPath)
    {
        if (string.IsNullOrWhiteSpace(savesRootPath) || !Directory.Exists(savesRootPath))
        {
            return Array.Empty<SaveFileEntry>();
        }

        try
        {
            return new DirectoryInfo(savesRootPath)
                .EnumerateFiles("*.save", SearchOption.TopDirectoryOnly)
                .Where(file => !file.Name.Contains(".ver", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(file => new SaveFileEntry
                {
                    FilePath = file.FullName,
                    FileName = file.Name,
                    LastWriteTimeLocal = file.LastWriteTime,
                    LengthBytes = file.Length
                })
                .ToList();
        }
        catch
        {
            return Array.Empty<SaveFileEntry>();
        }
    }
}
