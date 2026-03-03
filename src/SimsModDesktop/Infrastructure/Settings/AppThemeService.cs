using System.Text.Json;
using Avalonia;
using Avalonia.Styling;

namespace SimsModDesktop.Infrastructure.Settings;

public sealed class AppThemeService : IAppThemeService
{
    private const string DarkThemeName = "Dark";
    private const string LightThemeName = "Light";
    private readonly ISettingsStore _settingsStore;

    public AppThemeService(ISettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public string Normalize(string? requestedTheme)
    {
        return string.Equals(requestedTheme, LightThemeName, StringComparison.OrdinalIgnoreCase)
            ? LightThemeName
            : DarkThemeName;
    }

    public async Task<string> LoadRequestedThemeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            return Normalize(settings.Theme.RequestedTheme);
        }
        catch (IOException)
        {
            return DarkThemeName;
        }
        catch (UnauthorizedAccessException)
        {
            return DarkThemeName;
        }
        catch (JsonException)
        {
            return DarkThemeName;
        }
    }

    public void Apply(string? requestedTheme)
    {
        if (Avalonia.Application.Current is null)
        {
            return;
        }

        Avalonia.Application.Current.RequestedThemeVariant =
            string.Equals(Normalize(requestedTheme), LightThemeName, StringComparison.Ordinal)
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
    }
}
