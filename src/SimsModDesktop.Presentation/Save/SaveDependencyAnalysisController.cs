using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.Warmup;
using SimsModDesktop.Presentation.Services;

namespace SimsModDesktop.Presentation.Save;

public sealed class SaveDependencyAnalysisController
{
    private readonly ISaveHouseholdCoordinator _coordinator;
    private readonly ITrayDependenciesLauncher _trayDependenciesLauncher;
    private readonly ISaveWarmupService? _saveWarmupService;

    public SaveDependencyAnalysisController(
        ISaveHouseholdCoordinator coordinator,
        ITrayDependenciesLauncher trayDependenciesLauncher,
        ISaveWarmupService? saveWarmupService = null)
    {
        _coordinator = coordinator;
        _trayDependenciesLauncher = trayDependenciesLauncher;
        _saveWarmupService = saveWarmupService;
    }

    public async Task<string> AnalyzeAsync(string saveFilePath, string trayItemKey, CancellationToken cancellationToken = default)
    {
        var trayPath = _saveWarmupService is not null
            ? await _saveWarmupService.EnsureArtifactReadyAsync(
                saveFilePath,
                trayItemKey,
                "tray-dependency-analysis",
                cancellationToken)
            : await _coordinator
                .EnsurePreviewArtifactAsync(
                    saveFilePath,
                    trayItemKey,
                    "tray-dependency-analysis",
                    cancellationToken)
                .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(trayPath) || !Directory.Exists(trayPath))
        {
            return "The selected save household is not ready for dependency analysis yet.";
        }

        await _trayDependenciesLauncher.RunForTrayItemAsync(trayPath, trayItemKey);
        return "Started tray dependency analysis for the selected save household.";
    }
}
