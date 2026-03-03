using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Presentation.Shell;
using SimsModDesktop.Presentation.Workspaces;
using SimsModDesktop.Services;

namespace SimsModDesktop.Presentation.ServiceRegistration;

public static class PresentationServiceRegistration
{
    public static IServiceCollection AddSimsModDesktopPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ShellNavigationState>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<ToolkitWorkspaceViewModel>();
        services.AddSingleton<TrayWorkspaceViewModel>();
        services.AddSingleton<ModsWorkspaceViewModel>();
        services.AddSingleton<SavesWorkspaceViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainShellViewModel>();

        return services;
    }
}
