using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Composition;
using SimsModDesktop.Views;

namespace SimsModDesktop;

public partial class App : Avalonia.Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _serviceProvider = BuildServiceProvider();
            var themeService = _serviceProvider.GetRequiredService<IAppThemeService>();
            var requestedTheme = themeService.LoadRequestedThemeAsync().GetAwaiter().GetResult();
            themeService.Apply(requestedTheme);
            desktop.MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
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
