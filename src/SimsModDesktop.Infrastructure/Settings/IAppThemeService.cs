namespace SimsModDesktop.Infrastructure.Settings;

public interface IAppThemeService
{
    string Normalize(string? requestedTheme);
    Task<string> LoadRequestedThemeAsync(CancellationToken cancellationToken = default);
    void Apply(string? requestedTheme);
}
