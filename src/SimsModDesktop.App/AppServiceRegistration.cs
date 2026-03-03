using Microsoft.Extensions.DependencyInjection;
using SimsModDesktop.Application.ServiceRegistration;
using SimsModDesktop.Infrastructure.ServiceRegistration;
using SimsModDesktop.Presentation.ServiceRegistration;

namespace SimsModDesktop.App;

public static class AppServiceRegistration
{
    public static IServiceCollection AddSimsModDesktopApp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging();
        services.AddSimsModDesktopApplication();
        services.AddSimsModDesktopPresentation();
        services.AddSimsModDesktopInfrastructure();

        return services;
    }
}
