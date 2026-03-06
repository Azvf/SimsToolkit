using SimsModDesktop.Application.Mods;

namespace SimsModDesktop.Application.Warmup;

public interface IStartupPrewarmService
{
    void QueueTrayDependencyStartupPrewarm(string modsRootPath, Func<bool>? isForegroundBusy = null);

    void QueueModsQueryStartupPrewarm(ModItemCatalogQuery query, Func<bool>? isForegroundBusy = null);

    void QueueSaveStartupPrewarm(
        string saveFilePath,
        string? selectedHouseholdKey,
        Func<bool>? isForegroundBusy = null);

    void Reset();
}
