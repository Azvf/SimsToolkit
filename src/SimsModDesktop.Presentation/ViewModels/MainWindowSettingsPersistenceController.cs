using SimsModDesktop.Application.Models;
using SimsModDesktop.Application.Settings;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowSettingsPersistenceController
{
    private readonly ISettingsStore _settingsStore;
    private CancellationTokenSource? _debounceCts;

    public MainWindowSettingsPersistenceController(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        _settingsStore.LoadAsync(cancellationToken);

    public async Task SaveAsync(Action<AppSettings> applySettings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(applySettings);

        var settings = await _settingsStore.LoadAsync(cancellationToken);
        applySettings(settings);
        await _settingsStore.SaveAsync(settings, cancellationToken);
    }

    public void QueuePersist(Action<AppSettings> applySettings)
    {
        ArgumentNullException.ThrowIfNull(applySettings);

        CancelPending();
        _debounceCts = new CancellationTokenSource();
        var cancellationToken = _debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cancellationToken);
                await SaveAsync(applySettings, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }, cancellationToken);
    }

    public void CancelPending()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }
}
