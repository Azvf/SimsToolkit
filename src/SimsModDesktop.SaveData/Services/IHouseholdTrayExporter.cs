using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.SaveData.Services;

public interface IHouseholdTrayExporter
{
    SaveHouseholdExportResult Export(SaveHouseholdExportRequest request);
}
