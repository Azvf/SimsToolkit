using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Composition;
using SimsModDesktop.Diagnostics;
using SimsModDesktop.Views;

namespace SimsModDesktop;

public partial class App : Avalonia.Application
{
    private ServiceProvider? _serviceProvider;
    private ILogger<App>? _logger;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppStartupTelemetry.RecordMilestone("framework.init.completed");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _serviceProvider = BuildServiceProvider();
            _logger = _serviceProvider.GetRequiredService<ILogger<App>>();
            AppStartupTelemetry.RecordMilestone("service_provider.ready", _logger);
            var themeService = _serviceProvider.GetRequiredService<IAppThemeService>();
            var requestedTheme = themeService.LoadRequestedThemeAsync().GetAwaiter().GetResult();
            themeService.Apply(requestedTheme);
            AppStartupTelemetry.RecordMilestone("theme.applied", _logger);
            desktop.MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            AppStartupTelemetry.RecordMilestone("main_window.resolved", _logger);
            desktop.Exit += (_, _) => _serviceProvider.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider BuildServiceProvider()
    {
        return new ServiceCollection()
            .AddSimsDesktopShell()
            .BuildServiceProvider();
    }
}
