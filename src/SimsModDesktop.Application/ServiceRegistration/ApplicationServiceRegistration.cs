using Microsoft.Extensions.DependencyInjection;

namespace SimsModDesktop.Application.ServiceRegistration;

public static class ApplicationServiceRegistration
{
    public static IServiceCollection AddSimsModDesktopApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
