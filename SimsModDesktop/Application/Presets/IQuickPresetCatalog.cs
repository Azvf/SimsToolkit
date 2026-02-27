namespace SimsModDesktop.Application.Presets;

public interface IQuickPresetCatalog
{
    IReadOnlyList<QuickPresetDefinition> GetAll();
    IReadOnlyList<string> LastWarnings { get; }
    string UserPresetDirectory { get; }
    string UserPresetPath { get; }
    Task ReloadAsync(CancellationToken cancellationToken = default);
}
