using SimsModDesktop.Application.Caching;
using SimsModDesktop.Application.Configuration;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.Application.Warmup;
using SimsModDesktop.Presentation.Services;
using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.Presentation.Save;

public sealed class SavePreviewLifecycleController
{
    private readonly ISaveHouseholdCoordinator _coordinator;
    private readonly ISaveWarmupService? _saveWarmupService;
    private readonly IUiActivityMonitor _uiActivityMonitor;
    private readonly IConfigurationProvider? _configurationProvider;

    public SavePreviewLifecycleController(
        ISaveHouseholdCoordinator coordinator,
        IUiActivityMonitor uiActivityMonitor,
        IConfigurationProvider? configurationProvider = null,
        ISaveWarmupService? saveWarmupService = null)
    {
        _coordinator = coordinator;
        _uiActivityMonitor = uiActivityMonitor;
        _configurationProvider = configurationProvider;
        _saveWarmupService = saveWarmupService;
    }

    public Task<IReadOnlyList<SaveFileEntry>> GetSaveFilesAsync(string savesRootPath)
    {
        return Task.Run(() => _coordinator.GetSaveFiles(savesRootPath));
    }

    public bool TryGetPreviewDescriptor(string saveFilePath, out SavePreviewDescriptorManifest manifest)
    {
        return _coordinator.TryGetPreviewDescriptor(saveFilePath, out manifest);
    }

    public bool IsPreviewDescriptorCurrent(string saveFilePath, SavePreviewDescriptorManifest manifest)
    {
        return _coordinator.IsPreviewDescriptorCurrent(saveFilePath, manifest);
    }

    public async Task ClearPreviewDataAsync(string saveFilePath)
    {
        _saveWarmupService?.CancelWarmup(saveFilePath, "clear-preview-data");
        await Task.Run(() => _coordinator.ClearPreviewData(saveFilePath));
    }

    public Task<SavePreviewDescriptorBuildResult> EnsureDescriptorAsync(
        string saveFilePath,
        CacheWarmupObserver? observer,
        IProgress<SavePreviewDescriptorBuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (_saveWarmupService is not null)
        {
            return _saveWarmupService.EnsureDescriptorReadyAsync(saveFilePath, observer, cancellationToken);
        }

        return _coordinator.BuildPreviewDescriptorAsync(saveFilePath, progress, cancellationToken);
    }

    public Task<(bool Success, SaveHouseholdSnapshot? Snapshot, string Error)> LoadHouseholdsAsync(
        string saveFilePath,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var success = _coordinator.TryLoadHouseholds(saveFilePath, out var snapshot, out var error);
            cancellationToken.ThrowIfCancellationRequested();
            return (success, snapshot, error);
        }, cancellationToken);
    }

    public bool ShouldQueueIdleArtifactPrime(
        bool isActive,
        bool isBusy,
        SaveFileEntry? selectedSave,
        string? trayItemKey,
        SaveHouseholdItem? selectedHousehold)
    {
        return isActive &&
               !isBusy &&
               selectedSave is not null &&
               selectedHousehold is not null &&
               !string.IsNullOrWhiteSpace(trayItemKey) &&
               GetConfigBool("Performance.IdlePrewarm.SaveArtifactPrimeEnabled", true);
    }

    public async Task QueueOrEnsureIdleArtifactPrimeAsync(
        string saveFilePath,
        string trayItemKey,
        Func<bool> isStillRelevant,
        CancellationToken cancellationToken)
    {
        var idleDelay = TimeSpan.FromMilliseconds(Math.Max(1000, GetConfigInt("Performance.IdlePrewarm.DelayMs", 10000)));
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!isStillRelevant())
            {
                return;
            }

            if (DateTimeOffset.UtcNow - _uiActivityMonitor.LastInteractionUtc >= idleDelay)
            {
                if (_saveWarmupService is not null)
                {
                    _ = _saveWarmupService.QueueArtifactIdlePrewarm(saveFilePath, trayItemKey, "workspace-idle");
                }
                else
                {
                    _ = await _coordinator
                        .EnsurePreviewArtifactAsync(saveFilePath, trayItemKey, "workspace-idle", cancellationToken)
                        .ConfigureAwait(false);
                }

                return;
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
    }

    private bool GetConfigBool(string key, bool defaultValue)
    {
        if (_configurationProvider is null)
        {
            return defaultValue;
        }

        return _configurationProvider.GetConfigurationAsync<bool?>(key).GetAwaiter().GetResult() ?? defaultValue;
    }

    private int GetConfigInt(string key, int defaultValue)
    {
        if (_configurationProvider is null)
        {
            return defaultValue;
        }

        return _configurationProvider.GetConfigurationAsync<int?>(key).GetAwaiter().GetResult() ?? defaultValue;
    }
}
