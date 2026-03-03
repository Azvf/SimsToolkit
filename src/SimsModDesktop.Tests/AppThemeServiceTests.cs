using System.Text.Json;
using SimsModDesktop.Infrastructure.Settings;

namespace SimsModDesktop.Tests;

public sealed class AppThemeServiceTests
{
    [Fact]
    public async Task LoadRequestedThemeAsync_ReturnsLight_WhenSettingsRequestLight()
    {
        var service = new AppThemeService(new StubSettingsStore(new AppSettings
        {
            Theme = new AppSettings.ThemeSettings
            {
                RequestedTheme = "Light"
            }
        }));

        var theme = await service.LoadRequestedThemeAsync();

        Assert.Equal("Light", theme);
    }

    [Fact]
    public async Task LoadRequestedThemeAsync_ReturnsDark_WhenThemeIsInvalid()
    {
        var service = new AppThemeService(new StubSettingsStore(new AppSettings
        {
            Theme = new AppSettings.ThemeSettings
            {
                RequestedTheme = "blue"
            }
        }));

        var theme = await service.LoadRequestedThemeAsync();

        Assert.Equal("Dark", theme);
    }

    [Fact]
    public async Task LoadRequestedThemeAsync_ReturnsDark_WhenLoadingThrows()
    {
        var service = new AppThemeService(new ThrowingSettingsStore(new JsonException("bad json")));

        var theme = await service.LoadRequestedThemeAsync();

        Assert.Equal("Dark", theme);
    }

    [Theory]
    [InlineData("Light", "Light")]
    [InlineData("light", "Light")]
    [InlineData("Dark", "Dark")]
    [InlineData("bad", "Dark")]
    [InlineData(null, "Dark")]
    public void Normalize_ReturnsExpectedTheme(string? requestedTheme, string expectedTheme)
    {
        var service = new AppThemeService(new StubSettingsStore(new AppSettings()));

        var normalized = service.Normalize(requestedTheme);

        Assert.Equal(expectedTheme, normalized);
    }

    private sealed class StubSettingsStore : ISettingsStore
    {
        private readonly AppSettings _settings;

        public StubSettingsStore(AppSettings settings)
        {
            _settings = settings;
        }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_settings);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSettingsStore : ISettingsStore
    {
        private readonly Exception _exception;

        public ThrowingSettingsStore(Exception exception)
        {
            _exception = exception;
        }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromException<AppSettings>(_exception);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
