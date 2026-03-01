using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.SaveData.Services;

public interface ISaveHouseholdReader
{
    SaveHouseholdSnapshot Load(string saveFilePath);
}
